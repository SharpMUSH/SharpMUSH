using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Common;

/// <summary>
/// Why an exit could not say where it leads.
/// </summary>
public enum ExitDestinationFailure
{
	/// <summary>No destination at all — never linked, or <c>@unlink</c>ed.</summary>
	Unlinked,

	/// <summary>A variable exit failed to resolve one, and has already told the executor why.</summary>
	AlreadyReported
}

/// <summary>
/// Where an exit leads for one mover, or why it leads nowhere.
/// </summary>
public union ExitDestination(AnySharpContainer, ExitDestinationFailure);

/// <summary>
/// The three teleport modes. <c>@teleport</c> names them with switches, <c>tel()</c> with its third
/// and fourth arguments (<c>TEL_SILENT</c> and <c>TEL_INSIDE</c>, <c>src/fundb.c:2320-2323</c>);
/// <c>TEL_LIST</c> has no function spelling, because <c>fun_tel</c> moves one object.
/// </summary>
public readonly record struct TeleportOptions(bool List, bool Inside, bool Silent);

/// <summary>
/// The services <see cref="TeleportHelpers"/> works through. Bundled rather than passed one at a
/// time because <c>do_teleport</c> reaches most of the engine: it locates, notifies, evaluates
/// locks, runs attribute triads and moves.
/// </summary>
public sealed record TeleportServices(
	IMediator Mediator,
	INotifyService NotifyService,
	ILocateService LocateService,
	IAttributeService AttributeService,
	IPermissionService PermissionService,
	ILockService LockService,
	IMoveService MoveService,
	IDidItService DidItService);

/// <summary>
/// The teleport <c>@teleport</c> and <c>tel()</c> share, and the exit-destination resolution
/// <c>GOTO</c> shares with both.
/// </summary>
/// <remarks>
/// PennMUSH's <c>fun_tel</c> (<c>src/fundb.c:2309-2327</c>) is four lines: the side-effect gate, the
/// <c>@tel</c> command restriction, the two flags, then <c>do_teleport</c> — the same routine
/// <c>@teleport</c> calls. Written out a second time, <c>tel()</c> reached none of the policy:
/// destination authorization, the TELEPORT lock against the victim, NO_TEL / Leave / Z_TEL on the
/// absolute source room, HEAVY / FIXED / Tel_Anything, exit relocation, the
/// OXTPORT/TPORT/OTPORT/ATPORT triads or TEL_INSIDE.
/// <para><see cref="TeleportAsync"/> is <c>do_teleport</c> (<c>src/wiz.c:359-387</c>): it resolves the
/// destination once and loops. <see cref="TeleportOneAsync"/> is <c>do_teleport_one</c>
/// (<c>src/wiz.c:402</c>).</para>
/// </remarks>
public static class TeleportHelpers
{
	/// <summary>
	/// The attribute SharpMUSH records an exit's HOME/AMBIGUOUS linkage in, and the two values it
	/// takes. <c>@link</c>, <c>link()</c> and <c>loc()</c> write and read the same three.
	/// </summary>
	public const string AttrLinkType = "_LINKTYPE";

	/// <inheritdoc cref="AttrLinkType"/>
	public const string LinkTypeVariable = "variable";

	/// <inheritdoc cref="AttrLinkType"/>
	public const string LinkTypeHome = "home";

	/// <summary>
	/// PennMUSH <c>do_teleport</c> (<c>src/wiz.c:359</c>). The destination is resolved once for the
	/// whole list; where it is an exit, only the resolution that cannot depend on the mover happens
	/// here, because a home-linked exit leads somewhere different for each of them.
	/// </summary>
	/// <returns>The destination that was named, or <see cref="NotFound"/> when it named nothing.</returns>
	public static async ValueTask<Found<AnySharpObject>> TeleportAsync(
		IMUSHCodeParser parser,
		TeleportServices services,
		AnySharpObject executor,
		string toTeleport,
		string destinationName,
		TeleportOptions options)
	{
		IEnumerable<DbRefOrName> toTeleportList;
		if (options.List)
		{
			toTeleportList = ArgHelpers.NameList(toTeleport);
		}
		else
		{
			var isDbRef = DBRef.TryParse(toTeleport, out var objToTeleport);
			toTeleportList = [isDbRef ? objToTeleport!.Value : toTeleport];
		}

		var destination = await services.LocateService.LocateAndNotifyIfInvalid(parser,
			executor,
			executor,
			destinationName,
			LocateFlags.All);

		if (destination is not AnySharpObject validDestination)
		{
			await services.NotifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.CantGoThatWay), executor);
			return new NotFound();
		}

		// Teleporting to an exit means going where it leads. That is resolved per target inside the loop,
		// because a home-linked exit leads somewhere different for each mover.
		var destinationExit = validDestination is SharpExit exitDestination ? exitDestination : null;
		var fixedDestination = destinationExit is null ? validDestination.AsContainer : null;

		foreach (var obj in toTeleportList.Select(x => x switch
		{
			DBRef dbref => dbref.ToString(),
			string str => str
		}))
		{
			await TeleportOneAsync(parser, services, executor, obj, destinationName, destinationExit,
				fixedDestination, options);
		}

		return validDestination;
	}

	/// <summary>
	/// PennMUSH <c>do_teleport_one</c> (<c>src/wiz.c:402</c>): everything that happens to one victim.
	/// </summary>
	private static async ValueTask TeleportOneAsync(
		IMUSHCodeParser parser,
		TeleportServices services,
		AnySharpObject executor,
		string objectName,
		string destinationName,
		SharpExit? destinationExit,
		AnySharpContainer? fixedDestination,
		TeleportOptions options)
	{
		var locateTarget = await services.LocateService.LocateAndNotifyIfInvalid(parser, executor, executor,
			objectName, LocateFlags.All);
		if (locateTarget is not AnySharpObject target || target.IsRoom)
		{
			// Rooms cannot be teleported (PennMUSH src/wiz.c).
			if (locateTarget.IsRoom)
			{
				await services.NotifyService.NotifyLocalized(executor,
					nameof(ErrorMessages.Notifications.CantTeleportRooms), executor);
			}
			else
			{
				await services.NotifyService.Notify(executor, ErrorMessages.Returns.NotVisible, executor);
			}

			return;
		}

		var targetContent = target.AsContent;

		AnySharpContainer destinationContainer;

		if (destinationExit is null)
		{
			destinationContainer = fixedDestination!;
		}
		else
		{
			var resolvedExit = await ResolveExitDestination(
				parser, services, executor, target, destinationExit, destinationName);

			if (resolvedExit is not AnySharpContainer resolvedContainer)
			{
				if (resolvedExit is ExitDestinationFailure.Unlinked)
				{
					await services.NotifyService.NotifyLocalized(executor,
						nameof(ErrorMessages.Notifications.ExitGoesNowhere), executor);
				}

				return;
			}

			destinationContainer = resolvedContainer;
		}

		// recursive_member(destination, victim, 0) || victim == destination (wiz.c:440). This is a
		// refusal, so it has to be decided before anything announces the departure — safe_tel
		// declining the move afterwards would leave the room told about a move that never happened.
		if (targetContent.Object().DBRef.Equals(destinationContainer.Object().DBRef)
				|| await services.MoveService.WouldCreateLoop(targetContent, destinationContainer))
		{
			await services.NotifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.BadDestination), executor);
			return;
		}

		// Two privilege sets run through the rest of this, and they are not the same one.
		// Tel_Anywhere (hdrs/mushdb.h:17) waives the restrictions on WHERE something may go;
		// Tel_Anything (hdrs/mushdb.h:19) waives the restrictions on WHAT may be moved. Each is
		// Hasprivs — Wizard or Royalty — or the matching power (SharpMUSH.Database/Seed/PowerSeed.cs:50-51).
		var hasPrivs = await executor.IsWizard() || await executor.IsRoyalty();
		var telAnywhere = hasPrivs || await executor.HasPower("Tport_Anywhere");
		var telAnything = hasPrivs || await executor.HasPower("Tport_Anything");

		// wiz.c:442: without Tel_Anywhere, another player is not a destination at all — the
		// /INSIDE question below only arises for someone who could have gone there.
		if (!telAnywhere && target.IsPlayer && destinationContainer.IsPlayer)
		{
			await services.NotifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.BadDestination), executor);
			return;
		}

		// wiz.c:450-479: an exit is not carried to the destination. Its SOURCE is rewritten, so it
		// now leads out of the destination room instead, keeping where it leads. Penn returns here
		// and never reaches safe_tel, which is why safe_tel has nothing to say about exits.
		if (target.IsExit)
		{
			await TeleportExitAsync(services, executor, target, targetContent, destinationContainer, telAnything);
			return;
		}

		// wiz.c:487: a Tel_Anywhere teleporter sending a player TO a player lands them beside that
		// player rather than inside them. /INSIDE is what asks for the containment instead.
		// DEVIATION: Penn's branch (wiz.c:487-497) does its own OXTPORT/safe_tel/TPORT and returns
		// before wiz.c:585, so it never prints "Teleported." here. This falls through to the shared
		// path below instead, which does print it.
		if (telAnywhere && target.IsPlayer && destinationContainer.IsPlayer && !options.Inside)
		{
			destinationContainer = await destinationContainer.Location();
		}

		// wiz.c:519-566. Every restriction here reads the victim's ABSOLUTE room — the room at the
		// end of the containment walk, not the immediate container — so nesting inside a vehicle is
		// no way around the room's policy. Penn checks the VICTIM's room rather than the
		// teleporter's, which is what stops someone in a NO_TEL room having one of their objects
		// @tel them out; the exemption, though, is the command-giving player's, and it is waived
		// for a teleporter who controls that room or holds Tel_Anywhere.
		if (!await SourceRoomAllowsAsync(parser, services, executor, target, destinationContainer, telAnywhere))
		{
			return;
		}

		// PennMUSH do_teleport_one (wiz.c:568-579). /SILENT suppresses the OXTPORT and TPORT
		// triads and, through safe_tel's nomovemsgs, the MOVE triad. It does not reach ENTER or
		// LEAVE, and it does not reach the automatic look enter_room ends with.
		var currentLocation = await targetContent.Location();
		var changesRoom = !currentLocation.Object().DBRef.Equals(destinationContainer.Object().DBRef);

		// wiz.c:570-574. One conjunction authorises the whole move: authority over the victim where
		// it stands, authority over where it is going, and the FIXED rule. Any of the three failing
		// is the same refusal, and it is the destination's ENTER lock failure triad (wiz.c:590) —
		// not a bare notification — shown to the room the teleporter is standing in.
		if (!await TportControlOk(services, executor, target, currentLocation, telAnything)
				|| !await TportDestOk(services, executor, target, destinationContainer, telAnywhere)
				|| !await FixedAllows(executor, target, destinationContainer, telAnywhere, telAnything))
		{
			await services.DidItService.FailLock(parser, executor, destinationContainer.WithExitOption(),
				LockType.Enter,
				MarkupText.Plain(ErrorMessages.Notifications.PermissionDenied),
				await executor.Where());
			return;
		}

		if (!options.Silent && changesRoom)
		{
			await services.DidItService.DidIt(parser, new DidItRequest(
				Player: target, Thing: target, OWhat: "OXTPORT",
				Loc: currentLocation, Env0: executor.Object().DBRef.ToString()));
		}

		var moveResult = await services.MoveService.SafeTel(
			parser, targetContent, destinationContainer, options.Silent, executor.Object().DBRef, "teleport");

		if (moveResult is Error<string> error)
		{
			await services.NotifyService.Notify(executor, error.Value, executor);
			return;
		}

		if (!options.Silent && changesRoom)
		{
			await services.DidItService.DidIt(parser, new DidItRequest(
				Player: target, Thing: target,
				What: "TPORT", OWhat: "OTPORT", AWhat: "ATPORT",
				Loc: destinationContainer,
				Env0: executor.Object().DBRef.ToString(), Env1: currentLocation.Object().DBRef.ToString()));
		}

		// wiz.c:585-588: the teleporter is told the move happened, unless they were the one moved,
		// unless the victim is their own puppet (which reports for itself), and unless AreQuiet.
		if (!target.Object().DBRef.Equals(executor.Object().DBRef)
				&& !await IsOwnPuppet(target, executor)
				&& !await target.Object().AreQuietAsync(executor))
		{
			await services.NotifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.Teleported), executor);
		}
	}

	/// <summary>
	/// PennMUSH <c>wiz.c:450-479</c>: an exit's SOURCE is rewritten, and where it leads is untouched.
	/// </summary>
	private static async ValueTask TeleportExitAsync(
		TeleportServices services,
		AnySharpObject executor,
		AnySharpObject target,
		AnySharpContent targetContent,
		AnySharpContainer destinationContainer,
		bool telAnything)
	{
		if (!destinationContainer.IsRoom)
		{
			await services.NotifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.ExitsOnlyTeleportToRooms), executor);
			return;
		}

		if (await destinationContainer.WithExitOption().HasFlag("GOING"))
		{
			await services.NotifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.ExitDestinationCrumbling), executor);
			return;
		}

		var oldSource = await targetContent.Location();

		// wiz.c:468: the room the exit sits in decides the eviction, and the new room has to be
		// one the teleporter could have opened an exit in to begin with.
		if (!await TportControlOk(services, executor, target, oldSource, telAnything)
				|| !await CanOpenFrom(services, executor, destinationContainer))
		{
			await services.NotifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return;
		}

		await services.Mediator.Send(new MoveObjectCommand(
			targetContent, destinationContainer, oldSource.Object().DBRef,
			executor.Object().DBRef, IsSilent: true, Cause: "teleport"));

		// wiz.c:478: the exit branch has no victim==player case to exclude, so AreQuiet is the
		// whole of it.
		if (!await target.Object().AreQuietAsync(executor))
		{
			await services.NotifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.Teleported), executor);
		}
	}

	/// <summary>
	/// PennMUSH <c>wiz.c:519-566</c>: NO_TEL, the LEAVE lock and Z_TEL, each read off the victim's
	/// absolute room. Returns <see langword="false"/> once the victim has been refused and told why.
	/// </summary>
	private static async ValueTask<bool> SourceRoomAllowsAsync(
		IMUSHCodeParser parser,
		TeleportServices services,
		AnySharpObject executor,
		AnySharpObject target,
		AnySharpContainer destinationContainer,
		bool telAnywhere)
	{
		if (await services.MoveService.AbsoluteRoom(target) is not { } absoluteRoom)
		{
			return true;
		}

		var absoluteRoomObject = absoluteRoom.WithExitOption();
		var sourceExempt = telAnywhere || await services.PermissionService.Controls(executor, absoluteRoomObject);

		if (sourceExempt)
		{
			return true;
		}

		// wiz.c:543.
		if (await absoluteRoomObject.HasFlag("NO_TEL"))
		{
			await services.NotifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.TeleportsNotAllowed), executor);
			return false;
		}

		// wiz.c:549: the room's LEAVE lock, evaluated against the teleporter, with its failure
		// triad run once.
		if (!await services.PermissionService.PassesLock(executor, absoluteRoomObject, LockType.Leave))
		{
			await services.DidItService.FailLock(parser, executor, absoluteRoomObject, LockType.Leave,
				MarkupText.Plain(ErrorMessages.Notifications.TeleportsNotAllowed));
			return false;
		}

		// wiz.c:561: Z_TEL on the room, or on the room's zone object, pins the victim inside
		// that zone. The Zone lock has no part in this — where it matters is `controls`, which
		// `sourceExempt` already went through.
		//
		// The zone test comes FIRST, and deliberately: Penn opens the condition with
		// `GoodObject(Zone(absroom))`, so a Z_TEL room carrying no zone is not restricted at
		// all, because `ZTel(absroom)` is never reached. That reads like an oversight and is
		// not one — Z_TEL confines you to a zone, and a room in none has none to confine you
		// to — and it is also what makes reading the zone object's own Z_TEL safe, which
		// would otherwise be the zone of NOTHING. Penn never compares a missing source zone
		// against the destination's.
		if (await absoluteRoomObject.Object().Zone.WithCancellation(CancellationToken.None) is AnySharpObject sourceZone
				&& (await absoluteRoomObject.HasFlag("Z_TEL") || await sourceZone.HasFlag("Z_TEL")))
		{
			var destinationZone = await destinationContainer.WithExitOption().Object().Zone
				.WithCancellation(CancellationToken.None);

			var sameZone = destinationZone is AnySharpObject destinationZoneObject
				&& sourceZone.Object().DBRef.Equals(destinationZoneObject.Object().DBRef);

			if (!sameZone)
			{
				await services.NotifyService.NotifyLocalized(executor,
					nameof(ErrorMessages.Notifications.NoZoneTeleport), executor);
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// PennMUSH <c>Puppet(victim) &amp;&amp; (Owner(victim) == Owner(player))</c> (<c>src/wiz.c:585</c>):
	/// a puppet relays everything it is told to its owner, so an owner acting on their own puppet does
	/// not need a second confirmation.
	/// </summary>
	private static async ValueTask<bool> IsOwnPuppet(AnySharpObject thing, AnySharpObject player)
	{
		if (!await thing.HasFlag("PUPPET"))
		{
			return false;
		}

		var thingOwner = (await thing.Object().Owner.WithCancellation(CancellationToken.None)).Object.DBRef;
		var playerOwner = (await player.Object().Owner.WithCancellation(CancellationToken.None)).Object.DBRef;

		return thingOwner.Equals(playerOwner);
	}

	/// <summary>
	/// PennMUSH <c>tport_control_ok</c> (<c>src/wiz.c:330</c>): may <paramref name="player"/> move
	/// <paramref name="victim"/> out of <paramref name="loc"/> at all. Owning the room something is
	/// standing in is authority enough to evict it — that is how a room owner clears their own room —
	/// except for a HEAVY object belonging to someone else.
	/// </summary>
	private static async ValueTask<bool> TportControlOk(
		TeleportServices services, AnySharpObject player, AnySharpObject victim, AnySharpContainer loc,
		bool telAnything)
	{
		// wiz.c:334: nobody but God moves God.
		if (victim.IsGod() && !player.IsGod())
		{
			return false;
		}

		if (telAnything || await services.PermissionService.Controls(player, victim))
		{
			return true;
		}

		if (!await services.PermissionService.Controls(player, loc.WithExitOption()))
		{
			return false;
		}

		// wiz.c:345: "mortals can't @tel HEAVY players just on basis of location ownership".
		if (!await victim.HasFlag("HEAVY"))
		{
			return true;
		}

		var playerOwner = (await player.Object().Owner.WithCancellation(CancellationToken.None)).Object.DBRef;
		var victimOwner = (await victim.Object().Owner.WithCancellation(CancellationToken.None)).Object.DBRef;

		return playerOwner.Equals(victimOwner);
	}

	/// <summary>
	/// PennMUSH <c>tport_dest_ok</c> (<c>src/wiz.c:302</c>): may <paramref name="player"/> legitimately
	/// send <paramref name="victim"/> to <paramref name="destination"/>. Controlling the destination is
	/// enough on its own; short of that only a room takes a stranger, and only one that is JUMP_OK and
	/// whose TELEPORT lock admits the victim.
	/// </summary>
	private static async ValueTask<bool> TportDestOk(
		TeleportServices services, AnySharpObject player, AnySharpObject victim, AnySharpContainer destination,
		bool telAnywhere)
	{
		if (telAnywhere)
		{
			return true;
		}

		var destinationObject = destination.WithExitOption();

		if (await services.PermissionService.Controls(player, destinationObject))
		{
			return true;
		}

		// wiz.c:313: past here, something you do not control and that is not a room is hopeless.
		if (!destination.IsRoom)
		{
			return false;
		}

		// wiz.c:320: the unlocker is the VICTIM, not the teleporter — the room says who may arrive,
		// not who may send.
		if (!await services.LockService.Evaluate(LockType.Teleport, destinationObject, victim))
		{
			return false;
		}

		return await destinationObject.HasFlag("JUMP_OK");
	}

	/// <summary>
	/// PennMUSH <c>wiz.c:572</c>. A FIXED player is pinned: nothing they own is teleported, and they
	/// teleport nothing — unless the teleporter has Tel_Anything, or is Tel_Anywhere and is moving
	/// only themselves, or the destination is the victim's own owner.
	/// </summary>
	private static async ValueTask<bool> FixedAllows(
		AnySharpObject player, AnySharpObject victim, AnySharpContainer destination,
		bool telAnywhere, bool telAnything)
	{
		if (telAnything || (telAnywhere && player.Object().DBRef.Equals(victim.Object().DBRef)))
		{
			return true;
		}

		// dbdefs.h:84: Fixed() is read on the OWNER, never on the object itself.
		AnySharpObject victimOwner = await victim.Object().Owner.WithCancellation(CancellationToken.None);

		if (destination.Object().DBRef.Equals(victimOwner.Object().DBRef))
		{
			return true;
		}

		AnySharpObject playerOwner = await player.Object().Owner.WithCancellation(CancellationToken.None);

		return !await victimOwner.HasFlag("FIXED") && !await playerOwner.HasFlag("FIXED");
	}

	/// <summary>
	/// PennMUSH <c>can_open_from</c> (<c>hdrs/mushdb.h:94</c>): may <paramref name="player"/> source an
	/// exit in <paramref name="room"/>. Relocating an exit is held to the same standard as opening one
	/// there in the first place (<c>wiz.c:469</c>).
	/// </summary>
	public static async ValueTask<bool> CanOpenFrom(
		TeleportServices services, AnySharpObject player, AnySharpContainer room)
	{
		if (!room.IsRoom || await player.IsGuest())
		{
			return false;
		}

		var roomObject = room.WithExitOption();

		if (await services.PermissionService.Controls(player, roomObject)
				|| await player.IsWizard()
				|| await player.IsRoyalty()
				|| await player.HasPower("Open_Anywhere"))
		{
			return true;
		}

		return await roomObject.HasFlag("OPEN_OK")
			&& await services.LockService.Evaluate(LockType.Open, roomObject, player);
	}

	/// <summary>
	/// Where an exit leads for a particular mover. <c>goto</c>, <c>@teleport</c> and <c>tel()</c> all
	/// need this and must not drift apart: a variable exit computes its destination from
	/// <c>@DESTINATION</c>, a home-linked exit sends the mover to <em>their own</em> home — which is why
	/// the mover is a separate parameter from the executor — and otherwise it is the stored destination
	/// edge.
	/// </summary>
	public static async ValueTask<ExitDestination> ResolveExitDestination(
		IMUSHCodeParser parser, TeleportServices services, AnySharpObject executor, AnySharpObject mover,
		SharpExit exitObj, string typedName)
	{
		var exitObject = new AnySharpObject(exitObj);
		var linkType = await LinkTypeOf(services, executor, exitObject);

		if (linkType == LinkTypeVariable)
		{
			var variableDestination = await FindVariableDestination(parser, services, executor, exitObject, typedName);

			return variableDestination is null
				? ExitDestinationFailure.AlreadyReported
				: variableDestination;
		}

		if (linkType == LinkTypeHome)
		{
			// PennMUSH do_move (move.c:451): an exit linked to HOME sends the mover to their own home.
			if (!mover.IsContent)
			{
				return ExitDestinationFailure.Unlinked;
			}

			return await mover.AsContent.Home() switch
			{
				AnySharpContainer moverHome => moverHome,
				None => ExitDestinationFailure.Unlinked
			};
		}

		return await exitObj.Home.WithCancellation(CancellationToken.None) switch
		{
			AnySharpContainer destination => destination,
			None => ExitDestinationFailure.Unlinked
		};
	}

	/// <summary>
	/// How the exit was linked. PennMUSH stores HOME and AMBIGUOUS directly in Destination(); SharpMUSH
	/// records them in a <c>_LINKTYPE</c> attribute instead, which is the convention <c>loc()</c> already
	/// reads to answer <c>#-3</c> and <c>#-2</c>.
	/// </summary>
	private static async ValueTask<string?> LinkTypeOf(
		TeleportServices services, AnySharpObject executor, AnySharpObject exitObject)
	{
		var linkTypeAttr = await services.AttributeService.GetAttributeAsync(
			executor, exitObject, AttrLinkType, IAttributeService.AttributeMode.Read, false);

		if (linkTypeAttr is not SharpAttribute[] { Length: > 0 } linkTypeChain)
		{
			return null;
		}

		var linkType = linkTypeChain[0].Value.ToPlainText().Trim();

		return string.IsNullOrEmpty(linkType) ? null : linkType.ToLowerInvariant();
	}

	/// <summary>
	/// PennMUSH <c>find_var_dest</c> (<c>move.c:360</c>): a variable exit works out where it leads at move
	/// time by evaluating its <c>DESTINATION</c> attribute — with <c>%0</c> set to the exit name or alias
	/// the mover typed — falling back to <c>EXITTO</c>. The result is parsed as an objid, so it must name
	/// an object rather than merely matching something nearby.
	/// <para>Returns <c>null</c> after notifying the mover when no usable destination comes back.</para>
	/// </summary>
	private static async ValueTask<AnySharpContainer?> FindVariableDestination(
		IMUSHCodeParser parser, TeleportServices services, AnySharpObject executor, AnySharpObject exitObject,
		string typedName)
	{
		var attributeArgs = new Dictionary<string, CallState> { { "0", new CallState(typedName) } };

		var resolved = await AttributeHelpers.EvaluateFormatAttribute(
			services.AttributeService, parser, executor, exitObject, "DESTINATION", attributeArgs, MarkupText.Empty);

		if (resolved.Length == 0)
		{
			resolved = await AttributeHelpers.EvaluateFormatAttribute(
				services.AttributeService, parser, executor, exitObject, "EXITTO", attributeArgs, MarkupText.Empty);
		}

		var destinationText = resolved.ToPlainText().Trim();
		var located = DBRef.TryParse(destinationText, out var destinationRef)
			? await services.Mediator.Send(new GetObjectNodeQuery(destinationRef!.Value))
			: new AnyOptionalSharpObject(new None());

		// PennMUSH only permits a variable destination the exit itself could have been linked to
		// (move.c:457), and an exit is not somewhere you can end up.
		if (located is not AnySharpObject destination || !destination.IsContainer
				|| !await ExitCanLinkTo(services, exitObject, destination))
		{
			await services.NotifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.VariableExitDestinationInvalidFormat), executor,
				located switch
				{
					AnySharpObject found => found.Object().DBRef.Number.ToString(),
					None => "#-1"
				});

			return null;
		}

		return destination.AsContainer;
	}

	/// <summary>
	/// PennMUSH <c>can_link_to</c> (<c>mushdb.h:87</c>), asked of the exit rather than of the player: the
	/// exit controls the destination, is allowed to link anywhere, or the destination is LINK_OK and the
	/// exit passes its link lock.
	/// </summary>
	private static async ValueTask<bool> ExitCanLinkTo(
		TeleportServices services, AnySharpObject exitObject, AnySharpObject destination)
	{
		if (await services.PermissionService.Controls(exitObject, destination))
		{
			return true;
		}

		if (await exitObject.HasPower("Link_Anywhere"))
		{
			return true;
		}

		return await destination.HasFlag("LINK_OK")
					 && await services.LockService.Evaluate(LockType.Link, destination, exitObject);
	}
}
