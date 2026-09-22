using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	private const string AttrDrop = "DROP";

	private const string AttrODrop = "ODROP";

	private const string AttrADrop = "ADROP";

	private const string AttrSuccess = "SUCCESS";

	private const string AttrOSuccess = "OSUCCESS";

	private const string AttrASuccess = "ASUCCESS";

	private const string AttrGive = "GIVE";

	private const string AttrOGive = "OGIVE";

	private const string AttrAGive = "AGIVE";

	private const string AttrReceive = "RECEIVE";

	private const string AttrOReceive = "ORECEIVE";

	private const string AttrAReceive = "ARECEIVE";

	[SharpCommand(Name = "BUY", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 1, MaxArgs = 3, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Buy(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		var itemName = args["0"].Message!.ToPlainText();
		await NotifyService.Notify(executor, $"You try to buy '{itemName}'.", executor);
		await NotifyService.Notify(executor, "The BUY command requires a full economy system implementation.", executor);
		await NotifyService.Notify(executor, "Features needed: PRICELIST attribute parsing, @lock/pay checking, penny transfers.", executor);

		return CallState.Empty;
	}

	[SharpCommand(Name = "DROP", Switches = [], Behavior = CB.Player | CB.Thing, MinArgs = 1, MaxArgs = 1, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Drop(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var objectName = args["0"].Message!.ToPlainText();

		var locateResult = await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, objectName, LocateFlags.All);

		if (locateResult is not AnySharpObject objectToDrop || objectToDrop.IsRoom || objectToDrop.IsExit)
		{
			await NotifyService.Notify(executor, "You can't drop that.", executor);
			return CallState.Empty;
		}

		var executorLocation = await executor.Where();

		var objectLocation = await objectToDrop.Where();

		var isCarrying = objectLocation.Object().DBRef.Equals(executor.Object().DBRef);

		if (!isCarrying)
		{
			await NotifyService.Notify(executor, "You aren't carrying that.", executor);
			return CallState.Empty;
		}

		var currentRoom = executorLocation;

		// The drop lock fails on the object being dropped (move.c:736-738) and again on the room, when
		// the location is one (move.c:740-744); both return. The drop-in lock is the room's
		// (move.c:745-747), and its branch is the one `else if` in the chain with no `return` — the
		// object stays put, but do_drop still falls through to the DROP triad at move.c:768.
		if (!await LockService.Evaluate(LockType.Drop, objectToDrop, executor))
		{
			await DidItService.FailLock(parser, executor, objectToDrop, LockType.Drop,
				MarkupText.Plain(ErrorMessages.Notifications.CantSeemToGetRidOfThat));
			return CallState.Empty;
		}

		if (currentRoom.IsRoom && !await LockService.Evaluate(LockType.Drop, currentRoom.WithExitOption(), executor))
		{
			await DidItService.FailLock(parser, executor, currentRoom.WithExitOption(), LockType.Drop,
				MarkupText.Plain(ErrorMessages.Notifications.CantSeemToDropThingsHere));
			return CallState.Empty;
		}

		var dropInRefused = !await LockService.Evaluate(LockType.DropIn, currentRoom.WithExitOption(), executor);

		if (dropInRefused)
		{
			await DidItService.FailLock(parser, executor, currentRoom.WithExitOption(), LockType.DropIn,
				MarkupText.Plain(ErrorMessages.Notifications.CantSeemToDropThingsHere));
		}
		else
		{
			await DropTo(parser, executor, objectToDrop, currentRoom, "drop");
		}

		// did_it(player, thing, "DROP", "You drop X.", "ODROP", "drops X.", "ADROP", NOTHING)
		// (move.c:768-769). It is the tail of do_drop and runs whichever branch the object took —
		// the room, the drop-to, home, or the drop-in refusal that moves it nowhere — so it sits
		// after the drop-to handling rather than inside it, and it is the dropper's only success
		// message.
		await DidItService.DidIt(parser, new DidItRequest(
			Player: executor, Thing: objectToDrop,
			What: AttrDrop,
			Def: MarkupText.Plain(string.Format(ErrorMessages.Notifications.YouDrop, objectToDrop.Object().Name)),
			OWhat: AttrODrop,
			ODef: string.Format(ErrorMessages.Notifications.Drops, objectToDrop.Object().Name),
			AWhat: AttrADrop,
			Loc: currentRoom));

		return CallState.Empty;
	}

	/// <summary>
	/// Where a dropped object actually lands, and the single move that puts it there. PennMUSH
	/// <c>do_drop</c>'s three-way chain (<c>src/move.c:748-759</c>), shared with <c>EMPTY</c>, whose
	/// drop half is the same chain (<c>src/move.c:890-899</c>).
	/// </summary>
	/// <remarks>
	/// One move, straight to the destination. Landing the object in the room first and then pushing it
	/// through the drop-to would announce an arrival in a room it never stayed in, and would run the
	/// enter and leave triads for it.
	/// <para>
	/// DEVIATION on the STICKY branch, for <c>EMPTY</c> only: <c>do_empty</c> sends <c>thing</c> — the
	/// container being emptied — home rather than the item it just took out, and skips the
	/// <c>Dropped.</c> message <c>do_drop</c> sends. Both commands use <c>do_drop</c>'s shape here.
	/// </para>
	/// </remarks>
	private async ValueTask DropTo(
		IMUSHCodeParser parser,
		AnySharpObject dropper,
		AnySharpObject thing,
		AnySharpContainer location,
		string cause)
	{
		var content = thing.AsContent;
		AnySharpObject owner = await thing.Object().Owner.WithCancellation(CancellationToken.None);

		// move.c:748-750. Fixed(x) is a flag on the OWNER (hdrs/dbdefs.h:84), not on the object.
		if (await thing.HasFlag("STICKY") && !await owner.HasFlag("FIXED"))
		{
			await NotifyService.Notify(thing, ErrorMessages.Notifications.Dropped);

			var home = await content.Home();

			// safe_tel resolves HOME itself; MoveService takes a resolved container, so an object with
			// no home has nowhere to be sent and stays where it is.
			if (home is AnySharpContainer homeContainer)
			{
				await MoveService.SafeTel(parser, content, homeContainer, noMoveMsgs: false,
					dropper.Object().DBRef, cause);
			}

			return;
		}

		// move.c:751-755: the room's immediate drop-to, gated on the room NOT being STICKY — a STICKY
		// room holds its contents until the last Dropper leaves, which is what MoveService.EnterRoom's
		// maybe_dropto handles.
		var destination = location;

		if (location is SharpRoom room && !await location.WithExitOption().HasFlag("STICKY"))
		{
			var dropTo = await room.Location.WithCancellation(CancellationToken.None);

			// The drop-to lock is evaluated against the room with the OBJECT as the one being tested,
			// not the dropper (move.c:754).
			if (dropTo is AnySharpContainer dropToContainer
					&& await LockService.Evaluate(LockType.DropTo, location.WithExitOption(), thing))
			{
				destination = dropToContainer;
			}
		}

		// move.c:752 and :757 — the dropped object is told who dropped it, before the move.
		await NotifyService.Notify(thing,
			string.Format(ErrorMessages.Notifications.DropsYou, dropper.Object().Name));

		await MoveService.EnterRoom(parser, content, destination, noMoveMsgs: false,
			dropper.Object().DBRef, cause);
	}

	/// <summary>
	/// PennMUSH <c>do_empty</c> (<c>src/move.c:796</c>): every item in a container passes through the
	/// emptier's hands, so each one runs the same get and drop triads <c>GET</c> and <c>DROP</c> run.
	/// </summary>
	/// <remarks>
	/// The locks are re-evaluated per item rather than once for the whole container, which is Penn's
	/// documented choice (<c>src/move.c:783-789</c>): a lock that counts what is left has to see each
	/// move.
	/// <para>
	/// DEVIATION: Penn walks <c>first_visible</c> (<c>src/predicat.c:292</c>), so an item the emptier
	/// cannot see is skipped. SharpMUSH walks the whole contents list — the visibility walk is
	/// <c>LookService</c>'s and has no shared seam yet.
	/// </para>
	/// </remarks>
	[SharpCommand(Name = "EMPTY", Switches = [], CommandLock = "(TYPE^PLAYER|TYPE^THING)&!FLAG^GAGGED",
		Behavior = CB.Player | CB.Thing | CB.NoGagged, MinArgs = 1, MaxArgs = 1, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Empty(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var objectName = args["0"].Message!.ToPlainText();

		// move.c:809-812: an unmatchable name is noisy_match_result's refusal, not a usage line.
		if (string.IsNullOrWhiteSpace(objectName))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontSeeThatHere), executor);
			return CallState.Empty;
		}

		var locateResult = await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, objectName, LocateFlags.All);

		if (locateResult is not AnySharpObject objectToEmpty)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontSeeThatHere), executor);
			return CallState.Empty;
		}

		// move.c:809: TYPE_THING | TYPE_PLAYER only.
		if (!objectToEmpty.IsThing && !objectToEmpty.IsPlayer)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CantEmptyThatFromHere), executor);
			return CallState.Empty;
		}

		var executorLocation = await executor.Where();
		var containerLocation = await objectToEmpty.Where();
		var containerObject = containerLocation.WithExitOption();

		var heldByEmptier = containerLocation.Object().DBRef.Equals(executor.Object().DBRef);
		var besideEmptier = containerLocation.Object().DBRef.Equals(executorLocation.Object().DBRef);

		// move.c:816-819: the container has to be in the emptier's inventory or beside them.
		if (!heldByEmptier && !besideEmptier)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CantEmptyThatFromHere), executor);
			return CallState.Empty;
		}

		var emptyingSelf = objectToEmpty.Object().DBRef.Equals(executor.Object().DBRef);

		// move.c:820 walks the contents with first_visible, which skips anything the emptier cannot
		// interact with: can_interact(item, player, INTERACT_SEE) (predicat.c:306).
		var contents = await objectToEmpty.AsContainer.Content(Mediator)
			.Where(async (item, _) =>
				await PermissionService.CanInteract(executor, item.WithRoomOption(), IPermissionService.InteractType.See))
			.ToListAsync();
		var count = 0;

		foreach (var item in contents)
		{
			var itemObject = item.WithRoomOption();

			// move.c:822-823: exits are not dropped.
			if (itemObject.IsExit)
			{
				continue;
			}

			// Deviation from PennMUSH: the reality layer. do_empty is two moves — into the emptier's
			// hands (move.c:874) and back out to where the container stands (move.c:881-899) — so a
			// destination the item cannot reach has to refuse the pair up front rather than leave the
			// item stranded in the emptier's inventory.
			if (!await CanMoveInReality(parser, itemObject.Object().DBRef, executor.Object().DBRef)
					|| (!heldByEmptier
							&& !await CanMoveInReality(parser, itemObject.Object().DBRef, containerLocation.Object().DBRef)))
			{
				continue;
			}

			bool emptyOk;

			if (emptyingSelf)
			{
				// move.c:825-832, "empty me": nothing is taken, because the items are already in hand,
				// so only the drop half is gated. This is the one branch that consults the drop-in lock,
				// and it consults it against the EMPTIER.
				emptyOk = await LockService.Evaluate(LockType.Drop, itemObject, executor)
									&& await LockService.Evaluate(LockType.DropIn, containerObject, executor)
									&& (!containerLocation.IsRoom
											|| await LockService.Evaluate(LockType.Drop, containerObject, executor));
			}
			else if (await PermissionService.Controls(executor, objectToEmpty)
							 || (await objectToEmpty.HasFlag("ENTER_OK")
									 && await LockService.Evaluate(LockType.Enter, objectToEmpty, executor)))
			{
				// move.c:838-843: could_doit on the ITEM, reported as a fail_lock on the CONTAINER with a
				// null default — silent unless the container carries a FAILURE attribute of its own.
				if (!await LockService.Evaluate(LockType.Basic, itemObject, executor))
				{
					await DidItService.FailLock(parser, executor, objectToEmpty, LockType.Basic);
					continue;
				}

				// move.c:846-853: taking it into your own hands is enough on its own; dropping it where
				// the container stands needs the drop locks as well.
				emptyOk = heldByEmptier
									|| (await LockService.Evaluate(LockType.Drop, itemObject, executor)
											&& (!containerLocation.IsRoom
													|| await LockService.Evaluate(LockType.Drop, containerObject, executor)));
			}
			else
			{
				emptyOk = false;
			}

			if (!emptyOk)
			{
				continue;
			}

			count++;

			var itemName = itemObject.Object().Name;

			// move.c:864-878, the get half — skipped when the emptier IS the container.
			if (!emptyingSelf)
			{
				await NotifyService.Notify(objectToEmpty,
					string.Format(ErrorMessages.Notifications.WasTakenFromYou, itemName));
				await NotifyService.Notify(itemObject,
					string.Format(ErrorMessages.Notifications.TookYou, executor.Object().Name));

				var takeMove = await MoveService.EnterRoom(parser, item, executor.AsContainer,
					noMoveMsgs: false, executor.Object().DBRef, "empty");

				// A refused take leaves the item in the container, so it is not one of the objects the
				// tally at the end reports, and none of its triads describe anything that happened.
				if (takeMove is Error<string> error)
				{
					count--;
					await NotifyService.Notify(executor, error.Value, executor);
					continue;
				}

				// did_it_with(player, item, "SUCCESS", …, NOTHING, thing_loc, NOTHING, NA_INTER_HEAR)
				// (move.c:874-876). The 8th argument is `loc` and it is NOTHING, which real_did_it
				// resolves to the emptier's own room; the 9th is `env0`, and it is the CONTAINER'S
				// location rather than the container itself — where do_get puts the source container.
				await DidItService.DidIt(parser, new DidItRequest(
					Player: executor, Thing: itemObject,
					What: AttrSuccess,
					Def: MarkupText.Plain(string.Format(ErrorMessages.Notifications.YouTakeFrom,
						itemName, objectToEmpty.Object().Name)),
					OWhat: AttrOSuccess,
					ODef: string.Format(ErrorMessages.Notifications.TakesFrom,
						itemName, objectToEmpty.Object().Name),
					AWhat: AttrASuccess,
					Env0: containerLocation.Object().DBRef.ToString()));

				// move.c:877-878: the emptier's own receive triad, with the item in %0.
				await DidItService.DidIt(parser, new DidItRequest(
					Player: executor, Thing: executor,
					What: AttrReceive, OWhat: AttrOReceive, AWhat: AttrAReceive,
					Env0: itemObject.Object().DBRef.ToString()));
			}

			// move.c:881-903, the drop half — skipped when the container is already in the emptier's
			// inventory, because its items have nowhere further to go.
			if (!heldByEmptier)
			{
				await DropTo(parser, executor, itemObject, containerLocation, "empty");

				// did_it(player, item, "DROP", …, "ADROP", NOTHING) (move.c:901-902): `loc` NOTHING is
				// the emptier's own room.
				await DidItService.DidIt(parser, new DidItRequest(
					Player: executor, Thing: itemObject,
					What: AttrDrop,
					Def: MarkupText.Plain(string.Format(ErrorMessages.Notifications.YouDrop, itemName)),
					OWhat: AttrODrop,
					ODef: string.Format(ErrorMessages.Notifications.Drops, itemName),
					AWhat: AttrADrop));
			}
		}

		// move.c:906-911: the tally, printed whatever the count — nothing moved still reports zero.
		await NotifyService.Notify(executor,
			count == 1
				? string.Format(ErrorMessages.Notifications.RemovedOneObjectFrom, objectToEmpty.Object().Name)
				: string.Format(ErrorMessages.Notifications.RemovedObjectsFrom, count, objectToEmpty.Object().Name),
			executor);

		return CallState.Empty;
	}

	/// <summary>
	/// Ordinary GET's matcher: <c>MAT_NEIGHBOR | MAT_CHECK_KEYS | MAT_NEAR | MAT_ENGLISH</c>, preferring a
	/// thing (<c>move.c:566</c>, <c>:578</c>). Long_Fingers adds <c>MAT_ABSOLUTE</c> (<c>move.c:575-576</c>).
	/// </summary>
	private const LocateFlags GetMatchFlags = LocateFlags.MatchObjectsInLookerLocation | LocateFlags.PreferLockPass
		| LocateFlags.OnlyMatchObjectsInLookerLocation | LocateFlags.EnglishStyleMatching | LocateFlags.ThingsPreference;

	/// <summary>
	/// <c>parse_match_possessor</c>'s container match, <c>MAT_NEAR_THINGS | MAT_ENGLISH</c>
	/// (<c>predicat.c:1186-1187</c>).
	/// </summary>
	private const LocateFlags GetPossessorMatchFlags = LocateFlags.MatchMeForLooker | LocateFlags.AbsoluteMatch
		| LocateFlags.MatchWildCardForPlayerName | LocateFlags.MatchObjectsInLookerLocation
		| LocateFlags.MatchObjectsInLookerInventory | LocateFlags.OnlyMatchObjectsInLookerLocation
		| LocateFlags.EnglishStyleMatching;

	/// <summary>
	/// <c>MAT_OBJ_CONTENTS</c>, searched from the container on the taker's authority:
	/// <c>match_result_relative(player, box, objname, NOTYPE, MAT_OBJ_CONTENTS)</c> (<c>move.c:595-596</c>).
	/// </summary>
	private const LocateFlags GetPossessedMatchFlags = LocateFlags.MatchObjectsInLookerInventory
		| LocateFlags.MatchWildCardForPlayerName | LocateFlags.AbsoluteMatch
		| LocateFlags.OnlyMatchObjectsInLookerInventory | LocateFlags.EnglishStyleMatching;

	[SharpCommand(Name = "GET", Switches = [], Behavior = CB.Player | CB.Thing | CB.NoGagged, MinArgs = 1, MaxArgs = 0, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Get(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var what = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		if (string.IsNullOrWhiteSpace(what))
		{
			await NotifyService.Notify(executor, "Get what?", executor);
			return CallState.Empty;
		}

		// move.c:571-574: inside anything but a room, the taker must be let in or be in charge before
		// anything is matched.
		var surroundings = (await executor.Where()).WithExitOption();
		if (!surroundings.IsRoom
				&& !await surroundings.HasFlag("ENTER_OK")
				&& !await PermissionService.Controls(executor, surroundings))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return CallState.Empty;
		}

		var matchFlags = await executor.HasLongFingers() ? GetMatchFlags | LocateFlags.AbsoluteMatch : GetMatchFlags;

		// move.c:578-579: the whole argument is an ordinary name first, so a nearby item literally named
		// "Bob's hat" beats whatever Bob carries. Only a miss falls back to the possessive form; an
		// ambiguous match is reported as one.
		switch (await LocateService.Locate(parser, executor, executor, what, matchFlags))
		{
			case AnySharpObject thing:
				await GetNearby(parser, executor, surroundings, thing);
				break;
			case Error<string>:
				// noisy_match_result (move.c:647) matches again to say why.
				await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, what, matchFlags);
				break;
			default:
				if (Configuration.CurrentValue.Command.PossessiveGet)
				{
					await GetPossessed(parser, executor, what);
				}
				else
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontSeeThatHere), executor);
				}

				break;
		}

		return CallState.Empty;
	}

	/// <summary>do_get's ordinary branch (<c>move.c:646-704</c>).</summary>
	private async ValueTask GetNearby(IMUSHCodeParser parser, AnySharpObject executor, AnySharpObject surroundings,
		AnySharpObject thing)
	{
		var oldLocation = await thing.Where();

		if (await GetRefusal(executor, surroundings, thing, oldLocation) is { } refusal)
		{
			await NotifyService.Notify(executor, refusal, executor);
			return;
		}

		// The take lock is evaluated and failed against the object's own location, not against the
		// item: `oldloc = Location(thing)` (move.c:649), then `eval_lock_with(player, oldloc,
		// Take_Lock, pe_info)` and `fail_lock(player, oldloc, Take_Lock, ...)` (move.c:668-671).
		// Aiming it at the item would look for TAKE_LOCK`FAILURE on the item, so a container's own
		// take-failure message and action would never run. It is checked BEFORE the item's own basic
		// lock (move.c:668-675).
		var takeSource = oldLocation.WithExitOption();

		if (!await LockService.Evaluate(LockType.Take, takeSource, executor))
		{
			await DidItService.FailLock(parser, executor, takeSource, LockType.Take,
				MarkupText.Plain(ErrorMessages.Notifications.CantTakeThatFromThere));
			return;
		}

		if (!await LockService.Evaluate(LockType.Basic, thing, executor))
		{
			await DidItService.FailLock(parser, executor, thing, LockType.Basic,
				MarkupText.Plain(ErrorMessages.Notifications.CantPickThatUp));
			return;
		}

		if (!await MovedToTaker(parser, executor, thing))
		{
			return;
		}

		// move.c:676: plain GET tells the item after moveto, so the item has already had its look.
		await NotifyService.Notify(thing,
			string.Format(ErrorMessages.Notifications.TookYou, executor.Object().Name));

		await GetSucceeded(parser, executor, thing, oldLocation, possessive: false);
	}

	/// <summary>
	/// Why ordinary GET will not take <paramref name="thing"/> at all, in Penn's order and before either
	/// lock (<c>move.c:651-666</c>, <c>:690-695</c>); null when nothing stands in the way.
	/// </summary>
	private async ValueTask<string?> GetRefusal(AnySharpObject executor, AnySharpObject surroundings,
		AnySharpObject thing, AnySharpContainer oldLocation)
	{
		var executorRef = executor.Object().DBRef;
		var thingRef = thing.Object().DBRef;
		var isSelf = thingRef.Equals(executorRef);

		if (thing is { IsRoom: false, IsExit: false } && oldLocation.Object().DBRef.Equals(executorRef))
			return "You already have that!";
		if (surroundings.Object().DBRef.Equals(thingRef)) return "It's all around you!";
		if (await HoldsTaker(thing, executor, isSelf)) return ErrorMessages.Notifications.BadDestination;
		if (thing.IsExit) return "You can't pick up exits.";
		if (thing.IsRoom) return "You can't take that!";
		return isSelf ? "You cannot get yourself!" : null;
	}

	/// <summary>
	/// <c>recursive_member(player, thing, 0)</c> (<c>move.c:659</c>): the taker is somewhere inside
	/// <paramref name="thing"/>. A room holds the taker when it is the taker's outermost room; anything
	/// else is asked the containment-loop question a move into the taker would be.
	/// </summary>
	private async ValueTask<bool> HoldsTaker(AnySharpObject thing, AnySharpObject taker, bool isSelf)
		=> thing switch
		{
			_ when isSelf || thing.IsExit => false,
			{ IsRoom: true } => await MoveService.AbsoluteRoom(taker) is { } room
				&& room.Object().DBRef.Equals(thing.Object().DBRef),
			_ => await MoveService.WouldCreateLoop(thing.AsContent, taker.AsContainer)
		};

	/// <summary>
	/// do_get's possessive branch (<c>move.c:580-642</c>): <c>get &lt;container&gt;'s &lt;object&gt;</c>,
	/// reached only when the whole argument matched nothing.
	/// </summary>
	private async ValueTask GetPossessed(IMUSHCodeParser parser, AnySharpObject executor, string what)
	{
		// parse_match_possessor (predicat.c:1172-1184): up to the first apostrophe is the container, which
		// must be followed by an 's' or 'S'; the object name starts after the whitespace that follows.
		var apostrophe = what.IndexOf('\'');
		if (apostrophe < 0 || apostrophe + 1 >= what.Length || what[apostrophe + 1] is not ('s' or 'S'))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontSeeThatHere), executor);
			return;
		}

		var containerName = what[..apostrophe];
		var objectName = what[(apostrophe + 2)..].TrimStart();

		var containerResult = await LocateService.Locate(parser, executor, executor, containerName, GetPossessorMatchFlags);
		if (containerResult is Error<string> { Value: ErrorMessages.Returns.AmbiguousMatch })
		{
			await NotifyService.Notify(executor, $"I can't tell which {what}.", executor);
			return;
		}

		if (containerResult is not AnySharpObject container || (!container.IsPlayer && !container.IsThing))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontSeeThatHere), executor);
			return;
		}

		if (!await container.HasFlag("ENTER_OK") && !await PermissionService.Controls(executor, container))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return;
		}

		var thingResult = await LocateService.Locate(parser, container, executor, objectName, GetPossessedMatchFlags);
		if (thingResult is Error<string> { Value: ErrorMessages.Returns.AmbiguousMatch })
		{
			await NotifyService.Notify(executor, $"I can't tell which {what}.", executor);
			return;
		}

		if (thingResult is not AnySharpObject thing || thing.IsRoom || thing.IsExit)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontSeeThatHere), executor);
			return;
		}

		// move.c:603-610.
		if (container.Object().DBRef.Equals(executor.Object().DBRef))
		{
			await NotifyService.Notify(executor, "You already have that!", executor);
			return;
		}

		if (thing.Object().DBRef.Equals(executor.Object().DBRef))
		{
			await NotifyService.Notify(executor, "You cannot get yourself!", executor);
			return;
		}

		// `box = Location(thing)` (move.c:614). Both locks and possessive_get_d fold into one `if` with a
		// single `else` (move.c:615-642), so every refusal reports as `fail_lock(player, thing,
		// Basic_Lock, "You can't take that from there.")`: the FAILURE family on the *item*, carrying
		// the take lock's text. possessive_get_d is POSSGET_ON_DISCONNECTED (conf.h:491): without it a
		// disconnected player cannot be robbed.
		var source = await thing.Where();
		var sourceObject = source.WithExitOption();
		var canSteal = await LockService.Evaluate(LockType.Basic, thing, executor)
									 && (Configuration.CurrentValue.Command.PossessiveGetD
											 || !sourceObject.IsPlayer
											 || await ConnectionService.IsConnected(sourceObject))
									 && await LockService.Evaluate(LockType.Take, sourceObject, executor);

		if (!canSteal)
		{
			await DidItService.FailLock(parser, executor, thing, LockType.Basic,
				MarkupText.Plain(ErrorMessages.Notifications.CantTakeThatFromThere));
			return;
		}

		// move.c:627-628 — the robbed container and the item hear about it before the move.
		var takenName = thing.Object().Name;
		await NotifyService.Notify(sourceObject, string.Format(ErrorMessages.Notifications.WasTakenFromYou, takenName));
		await NotifyService.Notify(thing, string.Format(ErrorMessages.Notifications.TookYou, executor.Object().Name));

		if (!await MovedToTaker(parser, executor, thing))
		{
			return;
		}

		await GetSucceeded(parser, executor, thing, source, possessive: true);
	}

	/// <summary>
	/// <c>moveto(thing, player, player, "get")</c> (<c>move.c:633</c>, <c>:675</c>), which is
	/// <c>enter_room</c> with movement messages on: the vacated room's drop-to and the item's automatic
	/// look come with it. A move it refuses is reported to the taker and ends the GET, so a pickup that
	/// did not happen never reports success.
	/// </summary>
	private async ValueTask<bool> MovedToTaker(IMUSHCodeParser parser, AnySharpObject executor, AnySharpObject thing)
	{
		switch (await MoveService.EnterRoom(parser, thing.AsContent, executor.AsContainer, noMoveMsgs: false,
					executor.Object().DBRef, "get"))
		{
			case Error<string> refused:
				await NotifyService.Notify(executor, refused.Value, executor);
				return false;
			default:
				return true;
		}
	}

	/// <summary>The SUCCESS and RECEIVE triads of a completed GET (<c>move.c:634-639</c>, <c>:684-689</c>).</summary>
	private async ValueTask GetSucceeded(IMUSHCodeParser parser, AnySharpObject executor, AnySharpObject thing,
		AnySharpContainer source, bool possessive)
	{
		var takenName = thing.Object().Name;
		var sourceName = source.Object().Name;

		// did_it_with(player, thing, "SUCCESS", …, "OSUCCESS", …, "ASUCCESS", NOTHING, box, NOTHING, 0).
		// The 8th argument is `loc` and the 9th is `env0`: `loc` is NOTHING, which real_did_it resolves
		// to Location(player) (predicat.c:230), so the o-message audience is the taker's own room; the
		// source container rides in %0. The final `flags` is 0 on both get paths, so this o-message
		// consults no interaction lock — unlike the RECEIVE triad below, which Penn gives NA_INTER_HEAR.
		await DidItService.DidIt(parser, new DidItRequest(
			Player: executor, Thing: thing,
			What: AttrSuccess,
			Def: MarkupText.Plain(possessive
				? string.Format(ErrorMessages.Notifications.YouTakeFrom, takenName, sourceName)
				: string.Format(ErrorMessages.Notifications.YouTake, takenName)),
			OWhat: AttrOSuccess,
			ODef: possessive
				? string.Format(ErrorMessages.Notifications.TakesFrom, takenName, sourceName)
				: string.Format(ErrorMessages.Notifications.Takes, takenName),
			AWhat: AttrASuccess,
			Env0: source.Object().DBRef.ToString(),
			Interact: IPermissionService.InteractType.None));

		// did_it_with(player, player, "RECEIVE", NULL, "ORECEIVE", NULL, "ARECEIVE", NOTHING, thing,
		// NOTHING, NA_INTER_HEAR, AN_MOVE): the taker's own receive triad. `loc` is NOTHING — the room
		// the taker is in — and the taken object is %0.
		await DidItService.DidIt(parser, new DidItRequest(
			Player: executor, Thing: executor,
			What: AttrReceive, OWhat: AttrOReceive, AWhat: AttrAReceive,
			Env0: thing.Object().DBRef.ToString()));
	}

	[SharpCommand(Name = "GIVE", Switches = ["SILENT"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 2,
		MaxArgs = 0, ParameterNames = ["player", "amount"])]
	public async ValueTask<Option<CallState>> Give(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		var recipientName = args["0"].Message!.ToPlainText();
		var thingToGive = args["1"].Message!.ToPlainText();

		if (string.IsNullOrWhiteSpace(recipientName))
		{
			await NotifyService.Notify(executor, "Give to whom?", executor);
			return CallState.Empty;
		}

		if (string.IsNullOrWhiteSpace(thingToGive))
		{
			await NotifyService.Notify(executor, "Give what?", executor);
			return CallState.Empty;
		}

		var recipientResult = await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, recipientName, LocateFlags.All);

		if (recipientResult is not AnySharpObject recipient || recipient.IsRoom || recipient.IsExit)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontSeeThatHere), executor);
			return CallState.Empty;
		}

		if (!recipient.IsPlayer && !recipient.IsThing)
		{
			await NotifyService.Notify(executor, "You can't give things to that.", executor);
			return CallState.Empty;
		}

		// /SILENT belongs to this branch alone: it hushes the recipient's penny message
		// (rob.c:483) and has no bearing on the object form's triads, which rob.c fires
		// unconditionally.
		if (int.TryParse(thingToGive, out _))
		{
			await NotifyService.Notify(executor, "Money transfer will not be implemented.", executor);
			return CallState.Empty;
		}

		var objectResult = await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, thingToGive, LocateFlags.All);

		if (objectResult is not AnySharpObject objectToGive || objectToGive.IsRoom || objectToGive.IsExit)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontHaveThat), executor);
			return CallState.Empty;
		}

		var objectLocation = await objectToGive.Where();

		var isCarrying = objectLocation.Object().DBRef.Equals(executor.Object().DBRef);

		if (!isCarrying)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontHaveThat), executor);
			return CallState.Empty;
		}

		// rob.c:325-343 orders these give lock, from lock, receive lock, and only then the
		// ENTER_OK/controls gate. Of the three, only the give lock is a fail_lock — the other two
		// report a plain message and trigger nothing on the recipient.
		if (!await LockService.Evaluate(LockType.Give, objectToGive, executor))
		{
			await DidItService.FailLock(parser, executor, objectToGive, LockType.Give,
				MarkupText.Plain(ErrorMessages.Notifications.CantGiveThatAway));
			return CallState.Empty;
		}

		if (!await LockService.Evaluate(LockType.From, recipient, executor))
		{
			await NotifyService.Notify(executor,
				string.Format(ErrorMessages.Notifications.DoesntWantAnythingFromYou, recipient.Object().Name), executor);
			return CallState.Empty;
		}

		// The receive lock is evaluated with the OBJECT as the one being tested, not the giver
		// (rob.c:337).
		if (!await LockService.Evaluate(LockType.Receive, recipient, objectToGive))
		{
			await NotifyService.Notify(executor,
				string.Format(ErrorMessages.Notifications.DoesntWantThat, recipient.Object().Name), executor);
			return CallState.Empty;
		}

		if (!await recipient.HasFlag("ENTER_OK") && !await PermissionService.Controls(executor, recipient))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return CallState.Empty;
		}

		var recipientContainer = recipient.AsContainer;
		if (await MoveService.WouldCreateLoop(objectToGive.AsContent, recipientContainer))
		{
			await NotifyService.Notify(executor, "You can't give that - it would create a containment loop.", executor);
			return CallState.Empty;
		}

		// rob.c:346 hands the gift to moveto, and moveto IS enter_room (move.c:53-56): a gift changes
		// hands through the same pipeline every other move uses, and fires the same move triads.
		var contentToGive = objectToGive.AsContent;
		var giveMove = await MoveService.EnterRoom(parser, contentToGive, recipientContainer,
			noMoveMsgs: false, executor.Object().DBRef, "give");

		// A refused move leaves the gift where it was, so none of the triads below describe anything
		// that happened. rob.c has no analogue because moveto cannot fail there.
		if (giveMove is Error<string> error)
		{
			await NotifyService.Notify(executor, error.Value, executor);
			return CallState.Empty;
		}

		var giverName = executor.Object().Name;
		var giftName = objectToGive.Object().Name;
		var recipientDisplayName = recipient.Object().Name;

		// rob.c:357-358. GIVE/OGIVE/AGIVE live on the GIVER, not on the gift: did_it_with's `thing`
		// argument here is `player`. %0 is the gift and %1 the recipient.
		await DidItService.DidIt(parser, new DidItRequest(
			Player: executor, Thing: executor,
			What: AttrGive,
			Def: MarkupText.Plain(string.Format(ErrorMessages.Notifications.YouGaveTo, giftName, recipientDisplayName)),
			OWhat: AttrOGive,
			AWhat: AttrAGive,
			Env0: objectToGive.Object().DBRef.ToString(),
			Env1: recipient.Object().DBRef.ToString(),
			Interact: IPermissionService.InteractType.See));

		// rob.c:361 — the gift is told what happened to it.
		await NotifyService.Notify(objectToGive,
			string.Format(ErrorMessages.Notifications.GaveYouTo, giverName, recipientDisplayName));

		// rob.c:364-365: the GIFT's success triad, fired with the RECIPIENT as the enactor.
		await DidItService.DidIt(parser, new DidItRequest(
			Player: recipient, Thing: objectToGive,
			What: AttrSuccess, OWhat: AttrOSuccess, AWhat: AttrASuccess));

		// rob.c:369-370: RECEIVE/ORECEIVE/ARECEIVE live on the RECIPIENT and run with the recipient
		// as the enactor, so a recipient who cannot see the giver still gets their own message.
		// %0 is the gift and %1 the giver.
		await DidItService.DidIt(parser, new DidItRequest(
			Player: recipient, Thing: recipient,
			What: AttrReceive,
			Def: MarkupText.Plain(string.Format(ErrorMessages.Notifications.GaveYou, giverName, giftName)),
			OWhat: AttrOReceive,
			AWhat: AttrAReceive,
			Env0: objectToGive.Object().DBRef.ToString(),
			Env1: executor.Object().DBRef.ToString(),
			Interact: IPermissionService.InteractType.See));

		return CallState.Empty;
	}

	[SharpCommand(Name = "INVENTORY", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Inventory(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (!executor.IsPlayer && !executor.IsThing)
		{
			await NotifyService.Notify(executor, "You can't carry anything.", executor);
			return CallState.Empty;
		}

		var container = executor.AsContainer;
		var perceive = await ObserveRealityAsync(parser, executor);
		var contents = container.Content(Mediator).Where((item, ct) => perceive(item.Object().DBRef, ct));

		// PennMUSH: own inventory always shows Name(#dbrefFlags)
		var items = await contents
			.Select((AnySharpContent item, CancellationToken _) => MessageFormatting.FormatObjectWithDbref(item.Object()))
			.ToListAsync(ExecutionBudget.CurrentToken);

		if (items.Count == 0)
		{
			await NotifyService.Notify(executor, "You aren't carrying anything.", executor);
		}
		else
		{
			await NotifyService.Notify(executor, "You are carrying:", executor);
			foreach (var itemName in items)
			{
				await NotifyService.Notify(executor, itemName, executor);
			}
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "SCORE", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Score(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		await NotifyService.Notify(executor, "The SCORE command is not supported.", executor);
		await NotifyService.Notify(executor, "SharpMUSH does not track money or pennies.", executor);

		return CallState.Empty;
	}

	[SharpCommand(Name = "TEACH", Switches = ["LIST"], Behavior = CB.Default | CB.NoParse, MinArgs = 1, MaxArgs = 1, ParameterNames = ["player", "attribute"])]
	public async ValueTask<Option<CallState>> Teach(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
		var args = parser.CurrentState.Arguments;

		if (switches.Contains("LIST"))
		{
			if (!args.ContainsKey("0"))
			{
				await NotifyService.Notify(executor, "Teach what action list?", executor);
				return CallState.Empty;
			}

			var actionList = args["0"].Message!.ToPlainText();

			var executorLocation = await executor.Where();
			await CommunicationService.SendToRoomAsync(executor, executorLocation,
				_ => MarkupText.Plain($"{executor.Object().Name} types --> {actionList}"),
				INotifyService.NotificationType.Emit);

			var result = await parser.CommandListParse(MarkupText.Plain(actionList));

			return CallState.Empty with { HadErrors = result?.HadErrors == true };
		}

		if (!args.ContainsKey("0"))
		{
			await NotifyService.Notify(executor, "Teach what?", executor);
			return CallState.Empty;
		}

		var command = args["0"].Message!.ToPlainText();

		var location = await executor.Where();
		await CommunicationService.SendToRoomAsync(executor, location,
			_ => MarkupText.Plain($"{executor.Object().Name} types --> {command}"),
			INotifyService.NotificationType.Emit);

		var commandResult = await parser.CommandParse(MarkupText.Plain(command));

		return CallState.Empty with { HadErrors = commandResult.HadErrors };
	}

	[SharpCommand(Name = "USE", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Use(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		if (!args.ContainsKey("0") || string.IsNullOrWhiteSpace(args["0"].Message?.ToPlainText()))
		{
			await NotifyService.Notify(executor, "Use what?", executor);
			return CallState.Empty;
		}

		var objectName = args["0"].Message!.ToPlainText();

		var locateResult = await LocateService.LocateAndNotifyIfInvalid(
			parser, executor, executor, objectName, LocateFlags.All);

		if (locateResult is not AnySharpObject objectToUse)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontSeeThatHere), executor);
			return CallState.Empty;
		}

		// fail_lock(player, thing, Use_Lock, T("Permission denied."), NOTHING) (set.c:1413): the use
		// lock fails on the thing being used, and its failure attributes are UFAIL/OUFAIL/AUFAIL
		// through lock_msgs (lock.c:102).
		if (!await LockService.Evaluate(LockType.Use, objectToUse, executor))
		{
			await DidItService.FailLock(parser, executor, objectToUse, LockType.Use,
				MarkupText.Plain(ErrorMessages.Notifications.PermissionDenied));
			return CallState.Empty;
		}

		// did_it(player, thing, "USE", T("Used."), "OUSE", NULL, "AUSE", NOTHING, AN_SYS)
		// (set.c:1416-1417). PennMUSH picks AUSE or RUNOUT there by charge_action (predicat.c:88),
		// which decrements a CHARGES attribute; SharpMUSH has no CHARGES at all, so there is nothing
		// yet to switch on and AUSE always runs.
		await DidItService.DidIt(parser, new DidItRequest(
			Player: executor, Thing: objectToUse,
			What: "USE",
			Def: MarkupText.Plain(ErrorMessages.Notifications.Used),
			OWhat: "OUSE",
			AWhat: "AUSE"));

		return CallState.Empty;
	}

	[SharpCommand(Name = "WITH", Switches = ["NOEVAL", "ROOM"], Behavior = CB.Player | CB.Thing | CB.EqSplit, MinArgs = 0,
		MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> With(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;

		if (!args.ContainsKey("0") || string.IsNullOrWhiteSpace(args["0"].Message?.ToPlainText()))
		{
			await NotifyService.Notify(executor, "With whom?", executor);
			return CallState.Empty;
		}

		if (!args.TryGetValue("1", out var arg1) || string.IsNullOrWhiteSpace(arg1.Message?.ToPlainText()))
		{
			await NotifyService.Notify(executor, "Do what with them?", executor);
			return CallState.Empty;
		}

		var targetName = args["0"].Message!.ToPlainText();
		var command = arg1.Message!;

		AnySharpObject searchLocation = switches.Contains("ROOM")
			? (await executor.Where()).WithExitOption()
			: executor;

		var targetResult = await LocateService.LocateAndNotifyIfInvalid(
			parser, executor, searchLocation, targetName, LocateFlags.All);

		if (targetResult is not AnySharpObject target)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontSeeThatHere), executor);
			return CallState.Empty;
		}

		if (!target.IsPlayer && !target.IsThing)
		{
			await NotifyService.Notify(executor, "You can't do that with that.", executor);
			return CallState.Empty;
		}

		if (!await PermissionService.Controls(executor, target))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return CallState.Empty;
		}

		// Switch context: target becomes executor, original executor becomes enactor
		await parser.With(s => s with
		{
			Executor = target.Object().DBRef,
			Enactor = executor.Object().DBRef
		},
		async np => await np.CommandParse(command));

		return CallState.Empty;
	}
}
