using Mediator;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public class MoveService(
	IMediator mediator,
	IPermissionService permissionService,
	INotifyService notifyService,
	IDidItService didItService,
	IOptionsMonitor<SharpMUSHOptions> configuration) : IMoveService
{
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

		// The absolute room is walked once before the write and once after, and each side's zone is
		// read once from it. Both are handed to the zone triads, which re-walk nothing.
		var absOld = await AbsoluteRoom(mover);

		await mediator.Send(new MoveObjectCommand(
			what, where, enactor, noMoveMsgs, cause, OldContainer: old));

		var absNew = await AbsoluteRoom(mover);
		var oldZone = absOld is null ? null : await ZoneOf(absOld);
		var newZone = absNew is null ? null : await ZoneOf(absNew);
		var destinationObject = where.WithExitOption();
		var oldObject = oldContainer.WithExitOption();

		var wizardSuppressed = configuration.CurrentValue.Command.WizardNoAEnter
			&& await mover.IsWizard() && await mover.IsDarkLegal();

		if (!wizardSuppressed && !old.Equals(destination))
		{
			await didItService.DidIt(parser, new DidItRequest(
				Player: mover, Thing: mover, OWhat: "OXMOVE",
				Loc: oldContainer, Env0: destination, Env1: old,
				Interact: IPermissionService.InteractType.Hear));

			if (await permissionService.IsHearer(mover))
			{
				await didItService.DidIt(parser, new DidItRequest(
					Player: mover, Thing: oldObject,
					What: "LEAVE", OWhat: "OLEAVE", ODef: ErrorMessages.Notifications.DefaultOLeave,
					AWhat: "ALEAVE", Loc: oldContainer, Env0: destination,
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
					AWhat: "AENTER", Loc: where, Env0: old,
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
				Loc: where, Env0: destination, Env1: old,
				Interact: IPermissionService.InteractType.See));
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
		return zone.IsNone() ? null : zone.Known();
	}

	/// <summary>
	/// Checks if a move is permitted based on locks and permissions.
	/// </summary>
	public async ValueTask<bool> CanMoveAsync(
		AnySharpObject who,
		AnySharpContent objectToMove,
		AnySharpContainer destination)
	{
		var target = objectToMove.Match<AnySharpObject>(
			player => player,
			exit => exit,
			thing => thing);

		var dest = destination.Match<AnySharpObject>(
			player => player,
			room => room,
			thing => thing);

		if (!await permissionService.Controls(who, target))
		{
			return false;
		}

		if (!await permissionService.PassesLock(who, dest, LockType.Enter))
		{
			return false;
		}

		var currentLocation = await objectToMove.Match<ValueTask<DBRef?>>(
			async player =>
			{
				var location = await player.Location.WithCancellation(CancellationToken.None);
				return (DBRef?)location.Object().DBRef;
			},
			async exit =>
			{
				var location = await exit.Location.WithCancellation(CancellationToken.None);
				return (DBRef?)location.Object().DBRef;
			},
			async thing =>
			{
				var location = await thing.Location.WithCancellation(CancellationToken.None);
				return (DBRef?)location.Object().DBRef;
			});

		if (currentLocation.HasValue)
		{
			var locQuery = await mediator.Send(new GetObjectNodeQuery(currentLocation.Value));
			if (!locQuery.IsNone)
			{
				var locObj = locQuery.Known;
				if (!await permissionService.PassesLock(who, locObj, LockType.Leave))
				{
					return false;
				}
			}
		}

		return true;
	}

	/// <summary>
	/// Calculates the cost of moving an object.
	/// </summary>
	public ValueTask<int> CalculateMoveCostAsync(
		AnySharpContent objectToMove,
		AnySharpContainer destination)
	{
		// In PennMUSH, basic moves are typically free
		// Cost might apply for teleporting or special circumstances
		// For now, return 0 - this can be extended later
		return ValueTask.FromResult(0);
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
		DBRef? oldContainer = null;

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
					Enactor: null,
					IsSilent: true,
					Cause: "void_rescue",
					OldContainer: oldContainer));
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
						Enactor: null,
						IsSilent: true,
						Cause: "void_rescue",
						OldContainer: oldContainer));
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
