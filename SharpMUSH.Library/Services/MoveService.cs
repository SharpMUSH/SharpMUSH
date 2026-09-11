using Mediator;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public class MoveService(
	IMediator mediator,
	IPermissionService permissionService,
	INotifyService notifyService,
	IRealityPolicy reality,
	IDidItService didItService,
	ILookService lookService,
	IConnectionService connectionService,
	IOptionsMonitor<SharpMUSHOptions> configuration) : IMoveService
{
	/// <summary>
	/// PennMUSH's <c>enter_room</c> bails once more than fifteen frames are already on the stack
	/// (<c>src/move.c:232</c>). That number is Penn's own and is kept literal rather than folded into
	/// <c>Limit.MaxDepth</c>: <c>MaxDepth</c> bounds a walk over containment, this bounds re-entry
	/// through a drop-to or a <c>safe_tel</c> home, and they are not the same budget.
	/// </summary>
	private const int MaxMoveDepth = 15;

	/// <inheritdoc />
	public async ValueTask<AnySharpContainer?> AbsoluteRoom(AnySharpObject obj)
	{
		if (obj.IsRoom)
		{
			return obj.AsRoom;
		}

		if (!obj.IsContent)
		{
			return null;
		}

		// The walk starts at an exit's home — its destination — and at everything else's location
		// (utils.c:802). Only the seed uses home; the walk out of the containers uses location.
		AnySharpContainer current;

		if (obj.IsExit)
		{
			var home = await obj.AsExit.Home.WithCancellation(CancellationToken.None);

			if (home.IsNone)
			{
				return null;
			}

			current = home.WithoutNone();
		}
		else
		{
			current = await obj.AsContent.Location();
		}

		// PennMUSH caps this walk at a hard 20 (utils.c:801); SharpMUSH uses the configured
		// Limit.MaxDepth instead, so one setting bounds every recursive walk in the server.
		var maxDepth = (int)configuration.CurrentValue.Limit.MaxDepth;

		for (var depth = 0; depth < maxDepth; depth++)
		{
			if (current.IsRoom)
			{
				return current;
			}

			var next = await current.Location();

			if (next.Object().DBRef.Equals(current.Object().DBRef))
			{
				return null;
			}

			current = next;
		}

		return null;
	}

	/// <summary>
	/// Checks if moving an object to a destination would create a containment loop.
	/// This prevents scenarios like: A contains B, B contains C, then moving A into C would create a loop.
	/// </summary>
	/// <remarks>
	/// Bounded by the same <c>Limit.MaxDepth</c> as <see cref="AbsoluteRoom"/>, and fails closed:
	/// a walk that ran out of depth cannot know whether the destination is a descendant further
	/// down, and permitting the move on a truncated answer would build the cycle it could not see.
	/// PennMUSH's <c>recursive_member</c> caps at a hard 50 (<c>src/utils.c:512</c>); SharpMUSH uses
	/// the configured <c>Limit.MaxDepth</c> so one setting bounds every recursive walk.
	/// </remarks>
	public async ValueTask<bool> WouldCreateLoop(AnySharpContent objectToMove, AnySharpContainer destination)
	{
		if (!destination.IsThing && !destination.IsPlayer)
		{
			return false;
		}

		var objectDBRef = objectToMove.Object().DBRef;

		var current = destination;
		var visited = new HashSet<string> { current.Object().DBRef.ToString() };
		var maxDepth = (int)configuration.CurrentValue.Limit.MaxDepth;

		for (var depth = 0; depth < maxDepth; depth++)
		{
			if (current.Object().DBRef.Equals(objectDBRef))
			{
				return true;
			}

			var location = await current.Location();

			if (location.IsRoom || visited.Contains(location.Object().DBRef.ToString()))
			{
				return false;
			}

			visited.Add(location.Object().DBRef.ToString());
			current = location;
		}

		return true;
	}

	/// <summary>
	/// Sends an object somewhere and runs every triad the move fires, in PennMUSH's order.
	/// PennMUSH <c>moveit</c> (<c>src/move.c:66</c>).
	/// </summary>
	public async ValueTask MoveIt(
		IMUSHCodeParser parser,
		AnySharpContent what,
		AnySharpContainer where,
		bool noMoveMsgs,
		DBRef enactor,
		string cause)
	{
		// "Don't move something into something it's holding" (move.c:73).
		if (await WouldCreateLoop(what, where))
		{
			return;
		}

		var mover = what.WithRoomOption();
		var oldContainer = await what.Location();
		var old = oldContainer.Object().DBRef;
		var destination = where.Object().DBRef;

		var destinationObject = where.WithExitOption();
		var oldObject = oldContainer.WithExitOption();

		// move.c:77 and :100. Both are read while the mover is still in `old`, which is where Penn
		// reads them too: `oldSeeswhat` is computed after the contents lists have been shuffled but
		// before `Location(what)` is rewritten, and `where_is` answers off Location. They gate the
		// LEAVE and ENTER *environment*: a side that cannot Can_Locate the mover is given
		// did_it_interact rather than did_it_with, and did_it_interact carries no PE_REGS at all
		// (predicat.c:186-192), so %0 is unset for that triad.
		var whereSeesWhat = await permissionService.CanLocate(destinationObject, mover);
		var oldSeesWhat = await permissionService.CanLocate(oldObject, mover);

		// The absolute room is walked once before the write and once after, and each side's zone is
		// read once from it. Both are handed to the zone triads, which re-walk nothing.
		var absOld = await AbsoluteRoom(mover);

		await mediator.Send(new MoveObjectCommand(
			what, where, old, enactor, noMoveMsgs, cause));

		var absNew = await AbsoluteRoom(mover);
		var oldZone = absOld is null ? null : await ZoneOf(absOld);
		var newZone = absNew is null ? null : await ZoneOf(absNew);

		var wizardSuppressed = configuration.CurrentValue.Command.WizardNoAEnter
			&& await mover.IsWizard() && await mover.IsDarkLegal();

		if (!wizardSuppressed && !old.Equals(destination))
		{
			await didItService.DidIt(parser, new DidItRequest(
				Player: mover, Thing: mover, OWhat: "OXMOVE",
				Loc: oldContainer, Env0: destination.ToString(), Env1: old.ToString(),
				Interact: IPermissionService.InteractType.Hear));

			if (await permissionService.IsHearer(mover))
			{
				await didItService.DidIt(parser, new DidItRequest(
					Player: mover, Thing: oldObject,
					What: "LEAVE", OWhat: "OLEAVE", ODef: ErrorMessages.Notifications.DefaultOLeave,
					AWhat: "ALEAVE", Loc: oldContainer,
					Env0: oldSeesWhat ? destination.ToString() : null,
					Interact: IPermissionService.InteractType.Presence));

				await ZoneTriad(parser, mover, oldZone, newZone, leaving: true, loc: oldContainer);

				if (!oldObject.IsRoom)
				{
					// OXLEAVE lives on the container being left and is shown where the mover arrives.
					await didItService.DidIt(parser, new DidItRequest(
						Player: mover, Thing: oldObject, OWhat: "OXLEAVE",
						Loc: where,
						Interact: IPermissionService.InteractType.See));
				}

				if (!destinationObject.IsRoom)
				{
					await didItService.DidIt(parser, new DidItRequest(
						Player: mover, Thing: destinationObject, OWhat: "OXENTER",
						Loc: oldContainer,
						Interact: IPermissionService.InteractType.See));
				}

				await ZoneTriad(parser, mover, oldZone, newZone, leaving: false, loc: where);

				await didItService.DidIt(parser, new DidItRequest(
					Player: mover, Thing: destinationObject,
					What: "ENTER", OWhat: "OENTER", ODef: ErrorMessages.Notifications.DefaultOEnter,
					AWhat: "AENTER", Loc: where,
					Env0: whereSeesWhat ? old.ToString() : null,
					Interact: IPermissionService.InteractType.Presence));
			}
			else
			{
				// A non-hearer triggers the actions and none of the messages.
				await didItService.DidIt(parser, new DidItRequest(
					Player: mover, Thing: oldObject, AWhat: "ALEAVE", Loc: oldContainer));
				await ZoneTriad(parser, mover, oldZone, newZone, leaving: true, loc: oldContainer, actionsOnly: true);
				await ZoneTriad(parser, mover, oldZone, newZone, leaving: false, loc: where, actionsOnly: true);
				await didItService.DidIt(parser, new DidItRequest(
					Player: mover, Thing: destinationObject, AWhat: "AENTER", Loc: where));
			}
		}

		if (!noMoveMsgs)
		{
			await didItService.DidIt(parser, new DidItRequest(
				Player: mover, Thing: mover,
				What: "MOVE", OWhat: "OMOVE", AWhat: "AMOVE",
				Loc: where, Env0: destination.ToString(), Env1: old.ToString(),
				Interact: IPermissionService.InteractType.See));

			if (!old.Equals(destination) && (what.IsPlayer || what.IsThing))
			{
				await NotifyContentsOfMove(mover, old, destination);
			}
		}
	}

	/// <summary>
	/// Deviation from PennMUSH, which has no such message: whoever is riding inside a container that
	/// moved is told so. Each rider is answered in its own reality — a place it cannot perceive is
	/// named "somewhere" rather than by name, and a rider that cannot perceive the vehicle it is in
	/// is told nothing at all.
	/// </summary>
	private async ValueTask NotifyContentsOfMove(AnySharpObject container, DBRef oldLocation, DBRef newLocation)
	{
		var riders = await mediator
			.CreateStream(new GetContentsQuery(container.Object().DBRef), ExecutionBudget.CurrentToken)
			.ToArrayAsync(ExecutionBudget.CurrentToken);

		if (riders.Length == 0)
		{
			return;
		}

		var oldLocationNode = await mediator.Send(new GetObjectNodeQuery(oldLocation), ExecutionBudget.CurrentToken);
		var newLocationNode = await mediator.Send(new GetObjectNodeQuery(newLocation), ExecutionBudget.CurrentToken);

		if (oldLocationNode.IsNone || newLocationNode.IsNone)
		{
			return;
		}

		var oldName = oldLocationNode.Known.Object().Name;
		var newName = newLocationNode.Known.Object().Name;

		foreach (var rider in riders)
		{
			var receiver = rider.Object().DBRef;

			if (!await reality.CanPerceiveAsync(receiver, container.Object().DBRef)) continue;

			var visibleOrigin = await reality.CanPerceiveAsync(receiver, oldLocation) ? oldName : "somewhere";
			var visibleDestination = await reality.CanPerceiveAsync(receiver, newLocation) ? newName : "somewhere";

			await notifyService.Notify(receiver,
				$"You sense that you have moved from {visibleOrigin} to {visibleDestination}.", container);
		}
	}

	/// <summary>
	/// The zone triad, fired only when the absolute room's zone actually changed.
	/// PennMUSH <c>move.c:114-133</c>.
	/// </summary>
	private async ValueTask ZoneTriad(
		IMUSHCodeParser parser,
		AnySharpObject mover,
		AnySharpObject? oldZone,
		AnySharpObject? newZone,
		bool leaving,
		AnySharpContainer loc,
		bool actionsOnly = false)
	{
		var zone = leaving ? oldZone : newZone;

		if (zone is null)
		{
			return;
		}

		var other = leaving ? newZone : oldZone;

		if (other is not null && other.Object().DBRef.Equals(zone.Object().DBRef))
		{
			return;
		}

		await didItService.DidIt(parser, new DidItRequest(
			Player: mover,
			Thing: zone,
			What: actionsOnly ? null : leaving ? "ZLEAVE" : "ZENTER",
			OWhat: actionsOnly ? null : leaving ? "OZLEAVE" : "OZENTER",
			AWhat: leaving ? "AZLEAVE" : "AZENTER",
			Loc: loc,
			Interact: IPermissionService.InteractType.See));
	}

	private async ValueTask<AnySharpObject?> ZoneOf(AnySharpContainer container)
	{
		var zone = await container.WithExitOption().Object().Zone.WithCancellation(CancellationToken.None);
		return zone.IsNone ? null : zone.Known;
	}

	/// <inheritdoc />
	public async ValueTask<Result<Success>> EnterRoom(
		IMUSHCodeParser parser,
		AnySharpContent what,
		AnySharpContainer where,
		bool noMoveMsgs,
		DBRef enactor,
		string cause)
	{
		// move.c:232-235. Penn's counter is a process-global `static int deep`, which is only safe
		// under its single-threaded queue; SharpMUSH moves objects concurrently, so the counter rides
		// the evaluation instead — one player's deep move must not abort another's shallow one.
		// A parser without the counter has no bound at all, so it fails here rather than quietly
		// running unbounded.
		var depth = parser.CurrentState.MoveDepth
			?? throw new InvalidOperationException(
				$"{nameof(EnterRoom)} requires {nameof(ParserState)}.{nameof(ParserState.MoveDepth)}; "
				+ "a parser built without it would silently lose the move recursion bound.");

		if (depth.Count > MaxMoveDepth)
		{
			return new Error<string>(ErrorMessages.Notifications.TooManyContainers);
		}

		depth.Increment();

		try
		{
			var mover = what.WithRoomOption();

			// move.c:243-247: only a Mobile — a thing or a player — is moved by enter_room.
			// Penn's IsExit(loc) guard (move.c:249) has no analogue: AnySharpContainer is
			// player/room/thing, so an exit cannot be named as a destination in the first place.
			if (what.IsExit)
			{
				return new Error<string>(ErrorMessages.Notifications.CantGoThatWay);
			}

			// move.c:254: nothing enters itself.
			if (where.Object().DBRef.Equals(what.Object().DBRef))
			{
				return new Error<string>(ErrorMessages.Notifications.CantGoThatWay);
			}

			// move.c:259, recursive_member: nothing enters something it is already carrying. MoveIt
			// checks this too (move.c:73), and Penn keeps both: enter_room reports the refusal to the
			// mover, moveit only declines to write. A caller reaching MoveIt directly still needs the
			// guard, so neither is redundant.
			if (await WouldCreateLoop(what, where))
			{
				return new Error<string>(ErrorMessages.Notifications.CantGoThatWayContainmentLoop);
			}

			// Deviation from PennMUSH: the reality layer has no Penn counterpart. A destination the
			// mover cannot perceive is not one it can arrive in, so the gate sits with the other
			// enter_room validity checks rather than inside a triad.
			if (!await reality.CanPerceiveAsync(what.Object().DBRef, where.Object().DBRef))
			{
				return new Error<string>(ErrorMessages.Notifications.CantGoThatWay);
			}

			var oldContainer = await what.Location();

			await MoveIt(parser, what, where, noMoveMsgs, enactor, cause);

			// move.c:270-273: a STICKY room a Dropper just left empties through its drop-to, which for
			// a room is its own location.
			if (!oldContainer.Object().DBRef.Equals(where.Object().DBRef)
					&& oldContainer.IsRoom
					&& await IsDropper(mover)
					&& await oldContainer.WithExitOption().HasFlag("STICKY"))
			{
				var dropTo = await oldContainer.AsRoom.Location.WithCancellation(CancellationToken.None);

				if (!dropTo.IsNone)
				{
					await MaybeDropTo(parser, oldContainer, dropTo.WithoutNone(), enactor);
				}
			}

			// move.c:279: the automatic look. Unconditional — nomovemsgs never reaches it, and only
			// TERSE shortens it, which LookKey.Auto carries.
			await lookService.LookRoom(
				parser, mover, where.WithNoneOption().WithExitOption(), LookKey.Auto);

			return new Success();
		}
		finally
		{
			depth.Decrement();
		}
	}

	/// <inheritdoc />
	public async ValueTask<Result<Success>> SafeTel(
		IMUSHCodeParser parser,
		AnySharpContent what,
		AnySharpContainer where,
		bool noMoveMsgs,
		DBRef enactor,
		string cause)
	{
		// safe_tel resolves `dest == HOME` first (move.c:293). SharpMUSH has no HOME sentinel in
		// AnySharpContainer, so a caller that means home resolves it before calling.
		var mover = what.WithRoomOption();
		var currentLocation = await what.Location();

		var currentOwner = (await currentLocation.WithExitOption().Object().Owner
			.WithCancellation(CancellationToken.None)).Object.DBRef;
		var destinationOwner = (await where.WithExitOption().Object().Owner
			.WithCancellation(CancellationToken.None)).Object.DBRef;

		// move.c:294: same owner on both ends and nothing is stripped.
		if (currentOwner.Equals(destinationOwner))
		{
			return await EnterRoom(parser, what, where, noMoveMsgs, enactor, cause);
		}

		// wiz.c:450-479: do_teleport_one handles an exit by rewriting its Source and returns, so Penn's
		// safe_tel never sees one. SharpMUSH has no such branch, and an exit IS AnySharpContent, so one
		// still arrives here — `@force <exit>=goto <exit leading to a thing>` (GeneralCommands' IsContent
		// guard admits it) and `tel(<exit>,<thing>)` both reach this. An exit is not a container and has
		// nothing to strip, so the stripping pass is skipped; EnterRoom below then refuses the move
		// itself, because only a Mobile is moved by enter_room (move.c:243).
		if (what.IsExit)
		{
			return await EnterRoom(parser, what, where, noMoveMsgs, enactor, cause);
		}

		// The list is materialised before anything moves, because each EnterRoom below rewrites the
		// contents it is being read from.
		var carried = await mover.AsContainer.Content(mediator).ToListAsync();

		foreach (var item in carried)
		{
			var carriedObject = item.WithRoomOption();

			// move.c:311: an item goes home only when the mover does not control it, it is STICKY, and
			// the mover is not already its home. Everything else travels along.
			if (await permissionService.Controls(mover, carriedObject)
					|| !await carriedObject.HasFlag("STICKY"))
			{
				continue;
			}

			var home = await item.Home();

			// An item with no home has nowhere to be sent; it stays where it is rather than being
			// pushed at dbref -1.
			if (home.IsNone)
			{
				continue;
			}

			var homeContainer = home.WithoutNone();

			if (homeContainer.Object().DBRef.Equals(mover.Object().DBRef))
			{
				continue;
			}

			await EnterRoom(parser, item, homeContainer, noMoveMsgs, enactor, cause);
		}

		return await EnterRoom(parser, what, where, noMoveMsgs, enactor, cause);
	}

	/// <summary>
	/// PennMUSH <c>maybe_dropto</c> (<c>src/move.c:203</c>) together with the <c>send_contents</c>
	/// (<c>src/move.c:173</c>) it tail-calls.
	/// </summary>
	private async ValueTask MaybeDropTo(
		IMUSHCodeParser parser,
		AnySharpContainer room,
		AnySharpContainer dropTo,
		DBRef enactor)
	{
		// move.c:207-210: a room dropping to itself, or something that is not a room, keeps its
		// contents. The caller has already established the room half; both are kept here so this
		// reads as maybe_dropto does.
		if (dropTo.Object().DBRef.Equals(room.Object().DBRef) || !room.IsRoom)
		{
			return;
		}

		// The contents are read once: send_contents walks a list Penn has already detached, and each
		// EnterRoom below rewrites the live one.
		var contents = await room.Content(mediator).ToListAsync();

		// move.c:212-215: one Dropper still present and the room keeps everything.
		foreach (var content in contents)
		{
			if (await IsDropper(content.WithRoomOption()))
			{
				return;
			}
		}

		foreach (var content in contents)
		{
			var thing = content.WithRoomOption();

			// move.c:186: a Dropper stays, and so does anything the room's drop-to lock refuses.
			if (await IsDropper(thing)
					|| !await permissionService.PassesLock(thing, room.WithExitOption(), LockType.DropTo))
			{
				continue;
			}

			// move.c:187: a STICKY object goes home instead of through the drop-to.
			var destination = dropTo;

			if (await thing.HasFlag("STICKY"))
			{
				var home = await content.Home();

				if (home.IsNone)
				{
					continue;
				}

				destination = home.WithoutNone();
			}

			// Penn attributes this move to SYSEVENT (move.c:187). SharpMUSH has no such dbref, so the
			// enactor that caused the emptying move is carried through instead — a deliberate
			// deviation, visible only in the OBJECT`MOVE event's enactor field.
			await EnterRoom(parser, content, destination, noMoveMsgs: false, enactor, "dropto");
		}
	}

	/// <summary>
	/// PennMUSH <c>Dropper</c> (<c>src/move.c:171</c>): something that can hear and whose owner is
	/// connected.
	/// </summary>
	private async ValueTask<bool> IsDropper(AnySharpObject thing)
	{
		if (!await permissionService.IsHearer(thing))
		{
			return false;
		}

		var owner = await thing.Object().Owner.WithCancellation(CancellationToken.None);

		return await connectionService.IsConnected(owner);
	}

	/// <inheritdoc />
	public async ValueTask<bool> RescueFromVoidAsync(AnySharpObject player, DBRef fallbackHome)
	{
		if (!player.IsPlayer)
		{
			return false;
		}

		// Carried to every MoveObjectCommand below: without it the command falls back to the global
		// ObjectContents tag and wipes every container's cached contents list.
		// A player in the void may have no resolvable location at all, which is the condition this
		// method exists to repair. The move still has to name an origin container, so an unresolvable
		// one becomes #-1: expiring a contents list nothing holds is a no-op, and it keeps the write
		// off the tag that would expire every container in the game.
		var oldContainer = new DBRef(-1);

		try
		{
			var location = await player.AsPlayer.Location.WithCancellation(CancellationToken.None);
			var locationDbRef = location.Object().DBRef;
			oldContainer = locationDbRef;

			// Valid location - not in the void
			if (locationDbRef.Number >= 0)
			{
				return false;
			}
		}
		catch
		{
			// If we can't even resolve the location, player is definitely in the void
		}

		await notifyService.Notify(player, ErrorMessages.Notifications.InTheVoid);
		await notifyService.Notify(player, ErrorMessages.Notifications.VoidSendingHome);

		try
		{
			var home = await player.AsPlayer.Home.WithCancellation(CancellationToken.None);
			var homeDbRef = home.Object().DBRef;

			if (homeDbRef.Number >= 0)
			{
				await mediator.Send(new MoveObjectCommand(
					player.AsContent,
					home,
					oldContainer,
					Enactor: null,
					IsSilent: true,
					Cause: "void_rescue"));
				return true;
			}
		}
		catch
		{
			// Home is also invalid - fall through to fallback
		}

		// Fall back to configured PlayerStart / DefaultHome
		try
		{
			var fallbackResult = await mediator.Send(new GetObjectNodeQuery(fallbackHome));
			if (!fallbackResult.IsNone)
			{
				var fallbackObj = fallbackResult.Known;
				if (fallbackObj.IsRoom || fallbackObj.IsThing || fallbackObj.IsPlayer)
				{
					var fallbackContainer = await fallbackObj.Where();
					await mediator.Send(new MoveObjectCommand(
						player.AsContent,
						fallbackContainer,
						oldContainer,
						Enactor: null,
						IsSilent: true,
						Cause: "void_rescue"));
					return true;
				}
			}
		}
		catch
		{
			// Last resort failed - player remains in the void
		}

		return false;
	}
}
