using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
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
using CB = SharpMUSH.Library.Definitions.CommandBehavior;
using System.Collections.Immutable;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	private const string AttrLinkType = "_LINKTYPE";

	private const string LinkTypeVariable = "variable";

	private const string LinkTypeHome = "home";

	private const string AttrFollowing = "FOLLOWING";

	private const string AttrFollowers = "FOLLOWERS";

	/// <summary>
	/// Clears a follower's FOLLOWING attribute on the engine's own authority.
	/// </summary>
	/// <remarks>
	/// FOLLOWING is seeded with the <c>wizard</c> attribute flag, so no mortal can write it
	/// under their own authority - which is why PennMUSH performs every FOLLOWING write as
	/// GOD: <c>atr_clr(follower, "FOLLOWING", GOD)</c> (src/move.c:1451) and
	/// <c>atr_add(follower, "FOLLOWING", ..., GOD, 0)</c> (src/move.c:1236, 1243, 1292).
	/// This is engine bookkeeping, not a player write; routing it through the executor would
	/// deny every mortal FOLLOW, DESERT, DISMISS and UNFOLLOW.
	/// </remarks>
	private async ValueTask<Result<Success>> ClearFollowingAsync(
		AnySharpObject follower)
		=> await AttributeService.ClearAttributeAsync(await HelperFunctions.GetGod(Mediator), follower,
			AttrFollowing, IAttributeService.AttributePatternMode.Exact);

	/// <inheritdoc cref="ClearFollowingAsync"/>
	private async ValueTask<Result<Success>> SetFollowingAsync(
		AnySharpObject follower, AnySharpObject leader)
		=> await AttributeService.SetAttributeAsync(await HelperFunctions.GetGod(Mediator), follower,
			AttrFollowing, MarkupText.Plain(leader.Object().DBRef.ToString()));

	/// <summary>
	/// The dbrefs on <paramref name="leader"/>'s <c>FOLLOWERS</c> list, in order.
	/// PennMUSH keeps this list beside each follower's <c>FOLLOWING</c> so that
	/// <c>follower_command</c> (<c>src/move.c:1458</c>) can read the leader's followers directly
	/// instead of scanning every object's <c>FOLLOWING</c> after every successful move.
	/// </summary>
	/// <remarks>
	/// Read as GOD for the same reason the writes are: <c>FOLLOWERS</c> carries the <c>wizard</c>
	/// attribute flag (<c>AttributeEntrySeed.cs:68</c>), and a mortal following someone else has to
	/// be able to reach the leader's copy.
	/// </remarks>
	private async ValueTask<string[]> FollowersOfAsync(AnySharpObject leader)
	{
		var followers = await AttributeService.GetAttributeAsync(
			await HelperFunctions.GetGod(Mediator), leader, AttrFollowers,
			IAttributeService.AttributeMode.Read, parent: false);

		return followers is SharpAttribute[] chain
			? [.. chain.Last().Value.ToPlainText()
				.Split(' ', StringSplitOptions.RemoveEmptyEntries)]
			: [];
	}

	/// <inheritdoc cref="FollowersOfAsync"/>
	private async ValueTask<Result<Success>> WriteFollowersAsync(
		AnySharpObject leader, IEnumerable<string> followers)
	{
		var god = await HelperFunctions.GetGod(Mediator);
		var value = string.Join(' ', followers);

		// PennMUSH's atr_add with an empty value removes the attribute (src/atr.c), which is what
		// del_follower relies on to leave no empty FOLLOWERS behind.
		return value.Length == 0
			? await AttributeService.ClearAttributeAsync(god, leader, AttrFollowers,
				IAttributeService.AttributePatternMode.Exact)
			: await AttributeService.SetAttributeAsync(god, leader, AttrFollowers, MarkupText.Plain(value));
	}

	/// <summary>PennMUSH <c>add_follower</c> (<c>src/move.c:1208</c>).</summary>
	private async ValueTask AddFollowerAsync(AnySharpObject leader, AnySharpObject follower)
	{
		var followerRef = follower.Object().DBRef.ToString();
		var current = await FollowersOfAsync(leader);

		if (current.Contains(followerRef))
		{
			return;
		}

		await WriteFollowersAsync(leader, [.. current, followerRef]);
	}

	/// <summary>PennMUSH <c>del_follower</c> (<c>src/move.c:1262</c>).</summary>
	private async ValueTask RemoveFollowerAsync(AnySharpObject leader, AnySharpObject follower)
	{
		var followerRef = follower.Object().DBRef.ToString();
		var current = await FollowersOfAsync(leader);

		if (!current.Contains(followerRef))
		{
			return;
		}

		await WriteFollowersAsync(leader, current.Where(x => x != followerRef));
	}

	/// <summary>
	/// Whoever <paramref name="follower"/> currently follows, or none. SharpMUSH's <c>FOLLOWING</c>
	/// holds one leader rather than PennMUSH's list, so a new FOLLOW replaces the old one — and has
	/// to take the follower off the previous leader's <c>FOLLOWERS</c> as it does.
	/// </summary>
	private async ValueTask<AnySharpObject?> LeaderOfAsync(AnySharpObject follower)
	{
		var following = await AttributeService.GetAttributeAsync(
			await HelperFunctions.GetGod(Mediator), follower, AttrFollowing,
			IAttributeService.AttributeMode.Read, parent: false);

		if (following is not SharpAttribute[] chain
				|| !DBRef.TryParse(chain.Last().Value.ToPlainText().Trim(), out var leaderRef))
		{
			return null;
		}

		return await Mediator.Send(new GetObjectNodeQuery(leaderRef!.Value)) switch
		{
			AnySharpObject leader => leader,
			None => null
		};
	}

	/// <summary>
	/// Stops <paramref name="follower"/> following anyone, taking them off their leader's
	/// <c>FOLLOWERS</c> too. PennMUSH <c>clear_following</c> (<c>src/move.c:1425</c>).
	/// </summary>
	private async ValueTask<Result<Success>> StopFollowingAsync(AnySharpObject follower)
	{
		var leader = await LeaderOfAsync(follower);

		if (leader is not null)
		{
			await RemoveFollowerAsync(leader, follower);
		}

		return await ClearFollowingAsync(follower);
	}

	/// <summary>
	/// Stops everyone following <paramref name="leader"/>, clearing each follower's
	/// <c>FOLLOWING</c> and then the leader's own list. PennMUSH <c>clear_followers</c>
	/// (<c>src/move.c:1400</c>). Answers with the followers that were actually cleared, so the
	/// caller can report them.
	/// </summary>
	private async ValueTask<AnySharpObject[]> ClearFollowersAsync(AnySharpObject leader)
	{
		var followers = await FollowersOfAsync(leader);
		var cleared = new List<AnySharpObject>(followers.Length);

		foreach (var token in followers)
		{
			if (!DBRef.TryParse(token, out var followerRef))
			{
				continue;
			}

			if (await Mediator.Send(new GetObjectNodeQuery(followerRef!.Value)) is not AnySharpObject follower
					|| await ClearFollowingAsync(follower) is Error<string>)
			{
				continue;
			}

			cleared.Add(follower);
		}

		await WriteFollowersAsync(leader, []);

		return [.. cleared];
	}

	/// <summary>
	/// Re-issues <paramref name="command"/> for every object following <paramref name="leader"/>
	/// that was standing with them. PennMUSH <c>follower_command</c> (<c>src/move.c:1458</c>).
	/// </summary>
	/// <remarks>
	/// Each follower's command is queued as that follower with the leader as enactor, not run
	/// inline: a chain of followers would otherwise recurse on the stack.
	/// </remarks>
	private async ValueTask FollowerCommand(
		IMUSHCodeParser parser,
		AnySharpObject leader,
		AnySharpContainer from,
		string command,
		DBRef? toward)
	{
		var followers = await FollowersOfAsync(leader);

		if (followers.Length == 0)
		{
			return;
		}

		var line = toward is null ? command : $"{command} {toward}";

		// Every follower that gets this far was standing in `from`, so Penn's Dark(Location(follower))
		// is one read rather than one per follower.
		var leaderIsHidden = await leader.IsDarkLegal();
		var roomIsDark = await from.WithExitOption().HasFlag("DARK");
		var leaderIsLight = await leader.HasFlag("LIGHT");
		var leaderIsUnseen = leaderIsHidden || (roomIsDark && !leaderIsLight);

		foreach (var token in followers)
		{
			if (!DBRef.TryParse(token, out var followerRef))
			{
				continue;
			}

			var node = await Mediator.Send(new GetObjectNodeQuery(followerRef!.Value));

			if (node is not AnySharpObject follower)
			{
				continue;
			}

			if (!follower.IsContent)
			{
				continue;
			}

			var followerLocation = await follower.AsContent.Location();

			if (!followerLocation.Object().DBRef.Equals(from.Object().DBRef))
			{
				continue;
			}

			// Connected(follower) || IsThing(follower) (move.c:1481). A logged-out player stays where
			// they left off rather than being walked around the game by whoever they last followed.
			if (!follower.IsThing && !await ConnectionService.IsOnline(follower))
			{
				continue;
			}

			// !(DarkLegal(leader) || (Dark(Location(follower)) && !Light(leader))) || See_All(follower)
			// (move.c:1482): a departure the follower could not have seen is not one they can follow.
			if (leaderIsUnseen && !await follower.HasPower("See_All"))
			{
				continue;
			}

			await NotifyService.NotifyLocalized(follower.Object().DBRef,
				nameof(ErrorMessages.Notifications.YouFollowFormat), leader.Object().Name);

			// move.c:1484 queues with parse_que, which is PE_INFO_DEFAULT over a NULL parent queue
			// (hdrs/externs.h:178): the follower's command gets an entirely fresh pe_info, not a view
			// of the leader's and not a clone of it. Every collection and counter on ParserState is a
			// reference type, and the queue entry runs on the scheduler's thread while the leader's
			// command list is still going, so carrying the leader's over would be a data race as well
			// as a semantic leak — the leader pops its register frame and its SwitchStack entry long
			// before the consumer drains this, and a shared ExecutionStack would let the follower's
			// @break stop the leader's list. It is not direct input either, so it carries no handle.
			await Mediator.Send(new AdmitCommandListRequest(
				MarkupText.Plain(line),
				parser.CurrentState with
				{
					Executor = follower.Object().DBRef,
					Enactor = leader.Object().DBRef,
					Caller = leader.Object().DBRef,
					Handle = null,
					// parse_que passes no pe_regs, so %0-%9 and the q-registers start empty.
					Arguments = new Dictionary<string, CallState>(),
					EnvironmentRegisters = new Dictionary<string, CallState>(),
					CallerArguments = null,
					Registers = new([[]]),
					IterationRegisters = [],
					RegexRegisters = [],
					SwitchStack = [],
					ExecutionStack = [],
					CallDepth = new InvocationCounter(),
					FunctionRecursionDepths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
					TotalInvocations = new InvocationCounter(),
					LimitExceeded = new LimitExceededFlag(),
					MoveDepth = new InvocationCounter(),
					CommandHistory = null,
					BreakPropagation = null,
					HttpResponse = null
				},
				new DbRefAttribute(follower.Object().DBRef, DefaultSemaphoreAttributeArray),
				-1), ExecutionBudget.CurrentToken);
		}
	}

	/// <summary>
	/// How the exit was linked. PennMUSH stores HOME and AMBIGUOUS directly in Destination(); SharpMUSH
	/// records them in a <c>_LINKTYPE</c> attribute instead, which is the convention <c>loc()</c> already
	/// reads to answer <c>#-3</c> and <c>#-2</c>.
	/// </summary>
	private async ValueTask<string?> LinkTypeOf(AnySharpObject executor, AnySharpObject exitObject)
	{
		var linkTypeAttr = await AttributeService.GetAttributeAsync(
			executor, exitObject, AttrLinkType, IAttributeService.AttributeMode.Read, false);

		if (linkTypeAttr is not SharpAttribute[] { Length: > 0 } linkTypeChain)
		{
			return null;
		}

		var linkType = linkTypeChain[0].Value.ToPlainText().Trim();

		return string.IsNullOrEmpty(linkType) ? null : linkType.ToLowerInvariant();
	}

	/// <summary>
	/// Why an exit could not say where it leads.
	/// </summary>
	private enum ExitDestinationFailure
	{
		/// <summary>No destination at all — never linked, or <c>@unlink</c>ed.</summary>
		Unlinked,

		/// <summary>A variable exit failed to resolve one, and has already told the executor why.</summary>
		AlreadyReported
	}

	/// <summary>
	/// Where an exit leads for a particular mover. <c>goto</c> and <c>@teleport</c> both need this and
	/// must not drift apart: a variable exit computes its destination from <c>@DESTINATION</c>, a
	/// home-linked exit sends the mover to <em>their own</em> home — which is why the mover is a separate
	/// parameter from the executor — and otherwise it is the stored destination edge.
	/// </summary>
	private async ValueTask<ExitDestination> ResolveExitDestination(
		IMUSHCodeParser parser, AnySharpObject executor, AnySharpObject mover, SharpExit exitObj, string typedName)
	{
		var exitObject = new AnySharpObject(exitObj);
		var linkType = await LinkTypeOf(executor, exitObject);

		if (linkType == LinkTypeVariable)
		{
			var variableDestination = await FindVariableDestination(parser, executor, exitObject, typedName);

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
	/// PennMUSH <c>find_var_dest</c> (<c>move.c:360</c>): a variable exit works out where it leads at move
	/// time by evaluating its <c>DESTINATION</c> attribute — with <c>%0</c> set to the exit name or alias
	/// the mover typed — falling back to <c>EXITTO</c>. The result is parsed as an objid, so it must name
	/// an object rather than merely matching something nearby.
	/// <para>Returns <c>null</c> after notifying the mover when no usable destination comes back.</para>
	/// </summary>
	private async ValueTask<AnySharpContainer?> FindVariableDestination(
		IMUSHCodeParser parser, AnySharpObject executor, AnySharpObject exitObject, string typedName)
	{
		var attributeArgs = new Dictionary<string, CallState> { { "0", new CallState(typedName) } };

		var resolved = await AttributeHelpers.EvaluateFormatAttribute(
			AttributeService, parser, executor, exitObject, "DESTINATION", attributeArgs, MarkupText.Empty);

		if (resolved.Length == 0)
		{
			resolved = await AttributeHelpers.EvaluateFormatAttribute(
				AttributeService, parser, executor, exitObject, "EXITTO", attributeArgs, MarkupText.Empty);
		}

		var destinationText = resolved.ToPlainText().Trim();
		var located = DBRef.TryParse(destinationText, out var destinationRef)
			? await Mediator.Send(new GetObjectNodeQuery(destinationRef!.Value))
			: new AnyOptionalSharpObject(new None());

		// PennMUSH only permits a variable destination the exit itself could have been linked to
		// (move.c:457), and an exit is not somewhere you can end up.
		if (located is not AnySharpObject destination || !destination.IsContainer
				|| !await ExitCanLinkTo(exitObject, destination))
		{
			await NotifyService.NotifyLocalized(executor,
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
	private async ValueTask<bool> ExitCanLinkTo(AnySharpObject exitObject, AnySharpObject destination)
	{
		if (await PermissionService.Controls(exitObject, destination))
		{
			return true;
		}

		if (await exitObject.HasPower("Link_Anywhere"))
		{
			return true;
		}

		return await destination.HasFlag("LINK_OK")
					 && await LockService.Evaluate(LockType.Link, destination, exitObject);
	}

	[SharpCommand(Name = "GOTO", Behavior = CB.Default, MinArgs = 1, MaxArgs = 1, ParameterNames = ["destination"])]
	public async ValueTask<Option<CallState>> GoTo(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (parser.CurrentState.Arguments.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CantGoThatWay), executor);
			return CallState.Empty;
		}

		var exit = await LocateService.Locate(
			parser,
			executor,
			executor,
			args["0"].Message!.ToPlainText(),
			LocateFlags.ExitsInTheRoomOfLooker
			| LocateFlags.EnglishStyleMatching
			| LocateFlags.ExitsPreference
			| LocateFlags.OnlyMatchTypePreference);

		// PennMUSH do_move (move.c:432) answers a failed exit match with "You can't go that way.",
		// not with the generic locate failure.
		if (exit is not (AnySharpObject and SharpExit exitObj))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CantGoThatWay), executor);
			return CallState.Empty;
		}

		// enter_room only moves a Mobile (move.c:243). A room cannot be content, and asking it where
		// it is would throw rather than refuse.
		if (!executor.IsContent)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CantGoThatWay), executor);
			return CallState.Empty;
		}

		var exitObject = new AnySharpObject(exitObj);

		// The leave lock on the room the mover is standing in is evaluated before the exit's own
		// lock (move.c:441).
		var currentLocation = await executor.Where();

		if (!await PermissionService.PassesLock(executor, currentLocation.WithExitOption(), LockType.Leave))
		{
			await DidItService.FailLock(parser, executor, currentLocation.WithExitOption(), LockType.Leave,
				MarkupText.Plain(ErrorMessages.Notifications.CantGoThatWay));
			return CallState.Empty;
		}

		// The exit name or alias actually typed: args["1"] when the visitor routed a bare exit command
		// here, otherwise the argument to an explicit `goto`.
		var typedName = args.TryGetValue("1", out var typedArg)
			? typedArg.Message!.ToPlainText()
			: args["0"].Message!.ToPlainText();

		var resolved = await ResolveExitDestination(parser, executor, executor, exitObj, typedName);

		if (resolved is not AnySharpContainer destination)
		{
			// PennMUSH could_doit() (predicat.c:77) refuses an exit with no destination before the basic
			// lock is even evaluated, so do_move falls through to fail_lock. A variable exit that could
			// not work out where it leads has already reported that itself.
			return resolved is ExitDestinationFailure.Unlinked
				? await FailBasicLock(parser, executor, exitObject)
				: CallState.Empty;
		}

		if (!await PermissionService.CanGoto(executor, exitObj, destination))
		{
			return await FailBasicLock(parser, executor, exitObject);
		}

		if (await MoveService.WouldCreateLoop(executor.AsContent, destination))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CantGoThatWayContainmentLoop), executor);
			return CallState.Empty;
		}

		// did_it_with(..., NOTHING, Location(player), NOTHING, …) (move.c:480-482): the success triad
		// runs with the room being LEFT in %0. did_it's loc of NOTHING resolves to the mover's
		// location, which is still that room (predicat.c:230).
		await DidItService.DidIt(parser, new DidItRequest(
			Player: executor, Thing: exitObject,
			What: "SUCCESS", OWhat: "OSUCCESS", AWhat: "ASUCCESS",
			Loc: currentLocation, Env0: currentLocation.Object().DBRef.ToString()));

		// @drop / @odrop / @adrop on an exit are shown where the mover ARRIVES: did_it's loc argument
		// is var_dest, not the room being left (move.c:483).
		await DidItService.DidIt(parser, new DidItRequest(
			Player: executor, Thing: exitObject,
			What: "DROP", OWhat: "ODROP", AWhat: "ADROP",
			Loc: destination));

		// A room destination goes through enter_room, anything else through safe_tel (move.c:486-508).
		var result = destination.WithExitOption().IsRoom
			? await MoveService.EnterRoom(parser, executor.AsContent, destination,
				noMoveMsgs: false, executor.Object().DBRef, "move")
			: await MoveService.SafeTel(parser, executor.AsContent, destination,
				noMoveMsgs: false, executor.Object().DBRef, "move");

		if (result is Error<string> error)
		{
			await NotifyService.Notify(executor, error.Value, executor);
			return CallState.Empty;
		}

		// Followers trail the leader only if the leader actually went somewhere (move.c:493).
		var newLocation = await executor.Where();

		if (!newLocation.Object().DBRef.Equals(currentLocation.Object().DBRef))
		{
			await FollowerCommand(parser, executor, currentLocation, "GOTO", exitObj.Object.DBRef);
		}

		return new CallState(destination.Object().DBRef.ToString());
	}

	/// <summary>
	/// PennMUSH <c>fail_lock(player, exit, Basic_Lock, "You can't go that way.", NOTHING)</c>
	/// (<c>src/move.c:516</c>).
	/// </summary>
	private async ValueTask<Option<CallState>> FailBasicLock(
		IMUSHCodeParser parser, AnySharpObject executor, AnySharpObject exitObject)
	{
		await DidItService.FailLock(parser, executor, exitObject, LockType.Basic,
			MarkupText.Plain(ErrorMessages.Notifications.CantGoThatWay));

		return CallState.Empty;
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
	/// PennMUSH <c>tport_control_ok</c> (<c>src/wiz.c:331</c>): may <paramref name="player"/> move
	/// <paramref name="victim"/> out of <paramref name="loc"/> at all. Owning the room something is
	/// standing in is authority enough to evict it — that is how a room owner clears their own room —
	/// except for a HEAVY object belonging to someone else.
	/// </summary>
	private async ValueTask<bool> TportControlOk(
		AnySharpObject player, AnySharpObject victim, AnySharpContainer loc, bool telAnything)
	{
		// wiz.c:334: nobody but God moves God.
		if (victim.IsGod() && !player.IsGod())
		{
			return false;
		}

		if (telAnything || await PermissionService.Controls(player, victim))
		{
			return true;
		}

		if (!await PermissionService.Controls(player, loc.WithExitOption()))
		{
			return false;
		}

		// wiz.c:347: "mortals can't @tel HEAVY players just on basis of location ownership".
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
	private async ValueTask<bool> TportDestOk(
		AnySharpObject player, AnySharpObject victim, AnySharpContainer destination, bool telAnywhere)
	{
		if (telAnywhere)
		{
			return true;
		}

		var destinationObject = destination.WithExitOption();

		if (await PermissionService.Controls(player, destinationObject))
		{
			return true;
		}

		// wiz.c:312: past here, something you do not control and that is not a room is hopeless.
		if (!destination.IsRoom)
		{
			return false;
		}

		// wiz.c:319: the unlocker is the VICTIM, not the teleporter — the room says who may arrive,
		// not who may send.
		if (!await LockService.Evaluate(LockType.Teleport, destinationObject, victim))
		{
			return false;
		}

		return await destinationObject.HasFlag("JUMP_OK");
	}

	/// <summary>
	/// PennMUSH <c>wiz.c:570</c>. A FIXED player is pinned: nothing they own is teleported, and they
	/// teleport nothing — unless the teleporter has Tel_Anything, or is Tel_Anywhere and is moving
	/// only themselves, or the destination is the victim's own owner.
	/// </summary>
	private async ValueTask<bool> FixedAllows(
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
	private async ValueTask<bool> CanOpenFrom(AnySharpObject player, AnySharpContainer room)
	{
		if (!room.IsRoom || await player.IsGuest())
		{
			return false;
		}

		var roomObject = room.WithExitOption();

		if (await PermissionService.Controls(player, roomObject)
				|| await player.IsWizard()
				|| await player.IsRoyalty()
				|| await player.HasPower("Open_Anywhere"))
		{
			return true;
		}

		return await roomObject.HasFlag("OPEN_OK")
			&& await LockService.Evaluate(LockType.Open, roomObject, player);
	}

	[SharpCommand(Name = "@TELEPORT", Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 2,
		Switches = ["LIST", "INSIDE", "SILENT"], ParameterNames = ["object", "destination"])]
	public async ValueTask<Option<CallState>> Teleport(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var destinationString = (args.Count == 1 ? args["0"].Message : args["1"].Message)?.ToPlainText() ?? string.Empty;
		var toTeleport = (args.Count == 1 ? MarkupText.Plain(executor.Object().DBRef.ToString()) : args["0"].Message)?.ToPlainText() ?? string.Empty;

		var isList = parser.CurrentState.Switches.Contains("LIST");

		IEnumerable<DbRefOrName> toTeleportList;
		if (isList)
		{
			toTeleportList = ArgHelpers.NameList(toTeleport);
		}
		else
		{
			var isDbRef = DBRef.TryParse(toTeleport, out var objToTeleport);
			toTeleportList = [isDbRef ? objToTeleport!.Value : toTeleport];
		}

		var toTeleportStringList = toTeleportList.Select(x => x switch
		{
			DBRef dbref => dbref.ToString(),
			string str => str
		});

		var destination = await LocateService.LocateAndNotifyIfInvalid(parser,
			executor,
			executor,
			destinationString,
			LocateFlags.All);

		if (destination is not AnySharpObject validDestination)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CantGoThatWay), executor);
			return CallState.Empty;
		}

		// Teleporting to an exit means going where it leads. That is resolved per target inside the loop,
		// because a home-linked exit leads somewhere different for each mover.
		var destinationExit = validDestination is SharpExit exitDestination ? exitDestination : null;
		var fixedDestination = destinationExit is null ? validDestination.AsContainer : null;

		foreach (var obj in toTeleportStringList)
		{
			var locateTarget = await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, obj,
				LocateFlags.All);
			if (locateTarget is not AnySharpObject target || target.IsRoom)
			{
				// Rooms cannot be teleported (PennMUSH src/wiz.c).
				if (locateTarget.IsRoom)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CantTeleportRooms), executor);
				}
				else
				{
					await NotifyService.Notify(executor, ErrorMessages.Returns.NotVisible, executor);
				}
				continue;
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
					parser, executor, target, destinationExit, destinationString);

				if (resolvedExit is not AnySharpContainer resolvedContainer)
				{
					if (resolvedExit is ExitDestinationFailure.Unlinked)
					{
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ExitGoesNowhere), executor);
					}

					continue;
				}

				destinationContainer = resolvedContainer;
			}

			// recursive_member(destination, victim, 0) || victim == destination (wiz.c:440). This is a
			// refusal, so it has to be decided before anything announces the departure — safe_tel
			// declining the move afterwards would leave the room told about a move that never happened.
			if (targetContent.Object().DBRef.Equals(destinationContainer.Object().DBRef)
					|| await MoveService.WouldCreateLoop(targetContent, destinationContainer))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.BadDestination), executor);
				continue;
			}

			// Two privilege sets run through the rest of the command, and they are not the same one.
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
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.BadDestination), executor);
				continue;
			}

			// wiz.c:450-479: an exit is not carried to the destination. Its SOURCE is rewritten, so it
			// now leads out of the destination room instead, keeping where it leads. Penn returns here
			// and never reaches safe_tel, which is why safe_tel has nothing to say about exits.
			if (target.IsExit)
			{
				if (!destinationContainer.IsRoom)
				{
					await NotifyService.NotifyLocalized(executor,
						nameof(ErrorMessages.Notifications.ExitsOnlyTeleportToRooms), executor);
					continue;
				}

				if (await destinationContainer.WithExitOption().HasFlag("GOING"))
				{
					await NotifyService.NotifyLocalized(executor,
						nameof(ErrorMessages.Notifications.ExitDestinationCrumbling), executor);
					continue;
				}

				var oldSource = await targetContent.Location();

				// wiz.c:468: the room the exit sits in decides the eviction, and the new room has to be
				// one the teleporter could have opened an exit in to begin with.
				if (!await TportControlOk(executor, target, oldSource, telAnything)
						|| !await CanOpenFrom(executor, destinationContainer))
				{
					await NotifyService.NotifyLocalized(executor,
						nameof(ErrorMessages.Notifications.PermissionDenied), executor);
					continue;
				}

				await Mediator.Send(new MoveObjectCommand(
					targetContent, destinationContainer, oldSource.Object().DBRef,
					executor.Object().DBRef, IsSilent: true, Cause: "teleport"));

				// wiz.c:476: the exit branch has no victim==player case to exclude, so AreQuiet is the
				// whole of it.
				if (!await target.Object().AreQuietAsync(executor))
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.Teleported), executor);
				}

				continue;
			}

			// wiz.c:487: a Tel_Anywhere teleporter sending a player TO a player lands them beside that
			// player rather than inside them. /INSIDE is what asks for the containment instead.
			// DEVIATION: Penn's branch (wiz.c:487-497) does its own OXTPORT/safe_tel/TPORT and returns
			// before wiz.c:585, so it never prints "Teleported." here. This falls through to the shared
			// path below instead, which does print it.
			if (telAnywhere
					&& target.IsPlayer
					&& destinationContainer.IsPlayer
					&& !parser.CurrentState.Switches.Contains("INSIDE"))
			{
				destinationContainer = await destinationContainer.Location();
			}

			// wiz.c:519-566. Every restriction here reads the victim's ABSOLUTE room — the room at the
			// end of the containment walk, not the immediate container — so nesting inside a vehicle is
			// no way around the room's policy. Penn checks the VICTIM's room rather than the
			// teleporter's, which is what stops someone in a NO_TEL room having one of their objects
			// @tel them out; the exemption, though, is the command-giving player's, and it is waived
			// for a teleporter who controls that room or holds Tel_Anywhere.
			var absoluteRoom = await MoveService.AbsoluteRoom(target);

			if (absoluteRoom is not null)
			{
				var absoluteRoomObject = absoluteRoom.WithExitOption();
				var sourceExempt = telAnywhere || await PermissionService.Controls(executor, absoluteRoomObject);

				// wiz.c:519.
				if (!sourceExempt && await absoluteRoomObject.HasFlag("NO_TEL"))
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.TeleportsNotAllowed), executor);
					continue;
				}

				// wiz.c:543: the room's LEAVE lock, evaluated against the teleporter, with its failure
				// triad run once.
				if (!sourceExempt
						&& !await PermissionService.PassesLock(executor, absoluteRoomObject, LockType.Leave))
				{
					await DidItService.FailLock(parser, executor, absoluteRoomObject, LockType.Leave,
						MarkupText.Plain(ErrorMessages.Notifications.TeleportsNotAllowed));
					continue;
				}

				// wiz.c:561: Z_TEL on the room, or on the room's zone object, pins the victim inside
				// that zone. The Zone lock has no part in this — where it matters is `controls`, which
				// `sourceExempt` already went through.
				if (!sourceExempt
						&& await absoluteRoomObject.Object().Zone.WithCancellation(CancellationToken.None) is AnySharpObject sourceZone
						&& (await absoluteRoomObject.HasFlag("Z_TEL") || await sourceZone.HasFlag("Z_TEL")))
				{
					var destinationZone = await destinationContainer.WithExitOption().Object().Zone
						.WithCancellation(CancellationToken.None);

					var sameZone = destinationZone is AnySharpObject destinationZoneObject
						&& sourceZone.Object().DBRef.Equals(destinationZoneObject.Object().DBRef);

					if (!sameZone)
					{
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NoZoneTeleport), executor);
						continue;
					}
				}
			}

			// PennMUSH do_teleport_one (wiz.c:568-579). /SILENT suppresses the OXTPORT and TPORT
			// triads and, through safe_tel's nomovemsgs, the MOVE triad. It does not reach ENTER or
			// LEAVE, and it does not reach the automatic look enter_room ends with.
			var isSilent = parser.CurrentState.Switches.Contains("SILENT");
			var currentLocation = await targetContent.Location();
			var changesRoom = !currentLocation.Object().DBRef.Equals(destinationContainer.Object().DBRef);

			// wiz.c:568-571. One conjunction authorises the whole move: authority over the victim where
			// it stands, authority over where it is going, and the FIXED rule. Any of the three failing
			// is the same refusal, and it is the destination's ENTER lock failure triad (wiz.c:588) —
			// not a bare notification — shown to the room the teleporter is standing in.
			if (!await TportControlOk(executor, target, currentLocation, telAnything)
					|| !await TportDestOk(executor, target, destinationContainer, telAnywhere)
					|| !await FixedAllows(executor, target, destinationContainer, telAnywhere, telAnything))
			{
				await DidItService.FailLock(parser, executor, destinationContainer.WithExitOption(), LockType.Enter,
					MarkupText.Plain(ErrorMessages.Notifications.PermissionDenied),
					await executor.Where());
				continue;
			}

			if (!isSilent && changesRoom)
			{
				await DidItService.DidIt(parser, new DidItRequest(
					Player: target, Thing: target, OWhat: "OXTPORT",
					Loc: currentLocation, Env0: executor.Object().DBRef.ToString()));
			}

			var moveResult = await MoveService.SafeTel(
				parser, targetContent, destinationContainer, isSilent, executor.Object().DBRef, "teleport");

			if (moveResult is Error<string> error)
			{
				await NotifyService.Notify(executor, error.Value, executor);
				continue;
			}

			if (!isSilent && changesRoom)
			{
				await DidItService.DidIt(parser, new DidItRequest(
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
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.Teleported), executor);
			}
		}

		return new CallState(validDestination.Object().DBRef.ToString());
	}

	[SharpCommand(Name = "ENTER", Switches = [], Behavior = CB.Default, MinArgs = 1, MaxArgs = 1, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Enter(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		// move.c:928: do_enter refuses a non-Mobile before it matches anything, silently. ENTER is
		// CB.Default, so @force and @trigger can run it with a room or an exit as executor; a room has
		// no location to read and neither can be moved by enter_room (move.c:243).
		if (!executor.IsPlayer && !executor.IsThing)
		{
			return CallState.Empty;
		}

		var args = parser.CurrentState.Arguments;
		var objectName = args["0"].Message!.ToPlainText();

		// move.c:930-931: MAT_ABSOLUTE is added to the match flags only for Hasprivs — God, Wizard or
		// Royalty, which is IsPriv here. Without it "#N" is not a name a mortal can enter by, so the
		// only things they can name are the ones the remaining scopes already reach.
		var hasPrivs = await executor.IsPriv();
		var matchFlags = hasPrivs ? LocateFlags.All : LocateFlags.All & ~LocateFlags.AbsoluteMatch;

		var locateResult = await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, objectName, matchFlags);

		if (locateResult is not AnySharpObject objectToEnter)
		{
			// LocateAndNotifyIfInvalid has already told the mover what went wrong.
			return CallState.Empty;
		}

		if (!objectToEnter.IsThing && !objectToEnter.IsPlayer)
		{
			await NotifyService.Notify(executor, "You can't enter that.", executor);
			return CallState.Empty;
		}

		var currentLocation = await executor.Where();

		// move.c:946-948: only privileged players may enter something remotely. Paired with the
		// absolute-match gate above, this is what keeps "enter #N" from being a free teleport for a
		// mortal who learned the dbref of an ENTER_OK thing on the other side of the game.
		var targetLocation = await objectToEnter.Where();

		if (!hasPrivs && !targetLocation.Object().DBRef.Equals(currentLocation.Object().DBRef))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontSeeThatHere), executor);
			return CallState.Empty;
		}

		// move.c:952-955: one condition, one failure. The container must be ENTER_OK or controlled
		// AND pass its enter lock; anything else is the same fail_lock, defaulting to
		// "Permission denied.".
		var mayEnter =
			(await objectToEnter.HasFlag("ENTER_OK") || await PermissionService.Controls(executor, objectToEnter))
			&& await PermissionService.PassesLock(executor, objectToEnter, LockType.Enter);

		if (!mayEnter)
		{
			await DidItService.FailLock(parser, executor, objectToEnter, LockType.Enter,
				MarkupText.Plain(ErrorMessages.Notifications.PermissionDenied));
			return CallState.Empty;
		}

		// move.c:957-959: entering yourself is its own refusal, after the lock, with its own wording.
		if (objectToEnter.Object().DBRef.Equals(executor.Object().DBRef))
		{
			await NotifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.MustRemainBesideYourself), executor);
			return CallState.Empty;
		}

		// move.c:962: do_enter teleports rather than plain enter_room, so a container owned by
		// someone else strips the STICKY possessions the mover does not control.
		var moveResult = await MoveService.SafeTel(parser, executor.AsContent, objectToEnter.AsContainer,
			noMoveMsgs: false, executor.Object().DBRef, "enter");

		if (moveResult is Error<string> error)
		{
			await NotifyService.Notify(executor, error.Value, executor);
			return CallState.Empty;
		}

		// move.c:964-965: followers trail the leader only if the leader actually went somewhere.
		var newLocation = await executor.Where();

		if (!newLocation.Object().DBRef.Equals(currentLocation.Object().DBRef))
		{
			await FollowerCommand(parser, executor, currentLocation, "ENTER", objectToEnter.Object().DBRef);
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "LEAVE", Switches = [], Behavior = CB.Player | CB.Thing, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Leave(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (!executor.IsPlayer && !executor.IsThing)
		{
			await NotifyService.Notify(executor, "Only players and things can leave.", executor);
			return CallState.Empty;
		}

		var currentLocation = await executor.Where();
		var container = currentLocation.WithExitOption();

		AnySharpContainer destinationLocation = currentLocation switch
		{
			SharpPlayer player => await player.Location.WithCancellation(CancellationToken.None),
			SharpRoom room => room,
			SharpThing thing => await thing.Location.WithCancellation(CancellationToken.None)
		};

		// move.c:981-983: standing in a room, a NO_LEAVE container, or one whose leave lock refuses,
		// are one and the same refusal — fail_lock on the container, defaulting to "You can't leave.".
		if (currentLocation.IsRoom
				|| await container.HasFlag("NO_LEAVE")
				|| !await PermissionService.PassesLock(executor, container, LockType.Leave))
		{
			await DidItService.FailLock(parser, executor, container, LockType.Leave,
				MarkupText.Plain(ErrorMessages.Notifications.CantLeave));
			return CallState.Empty;
		}

		// move.c:986. EnterRoom carries the automatic look, so the command adds none of its own.
		var moveResult = await MoveService.EnterRoom(parser, executor.AsContent, destinationLocation,
			noMoveMsgs: false, executor.Object().DBRef, "leave");

		if (moveResult is Error<string> error)
		{
			await NotifyService.Notify(executor, error.Value, executor);
			return CallState.Empty;
		}

		// move.c:987-988.
		var newLocation = await executor.Where();

		if (!newLocation.Object().DBRef.Equals(currentLocation.Object().DBRef))
		{
			await FollowerCommand(parser, executor, currentLocation, "leave", toward: null);
		}

		return new CallState(destinationLocation.Object().DBRef.ToString());
	}

	[SharpCommand(Name = "HOME", Switches = [], Behavior = CB.Player | CB.Thing, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Home(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		// move.c:402-404: !Mobile, no home, a home the mover is carrying, and being its own home are
		// one refusal — "Bad destination.".
		if (!executor.IsPlayer && !executor.IsThing)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.BadDestination), executor);
			return CallState.Empty;
		}

		// Guarded above: only players and things reach here, and both always have a home.
		if (await executor.MinusRoom().Home() is not AnySharpContainer homeLocation)
		{
			throw new InvalidOperationException("Players and things always have a home.");
		}

		var homeObj = homeLocation.Object();

		if (homeObj.DBRef.Number < 0
				|| homeObj.DBRef.Equals(executor.Object().DBRef)
				|| await MoveService.WouldCreateLoop(executor.AsContent, homeLocation))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.BadDestination), executor);
			return CallState.Empty;
		}

		var currentLocation = await executor.Where();

		// move.c:407-412: neither the mover nor the room it stands in may be Dark for the room to be
		// told.
		if (!await executor.IsDark() && !await currentLocation.WithExitOption().IsDark())
		{
			await CommunicationService.SendToRoomAsync(
				executor,
				currentLocation,
				_ => MarkupText.Plain(string.Format(ErrorMessages.Notifications.GoesHomeFormat, executor.Object().Name)),
				INotifyService.NotificationType.Emit,
				excludeObjects: [executor],
				interact: IPermissionService.InteractType.See);
		}

		// PennMUSH sends all three (move.c:415-417); that is not a transcription slip.
		for (var i = 0; i < 3; i++)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NoPlaceLikeHome), executor);
		}

		// move.c:418. safe_tel steals the possessions the mover does not control, and the automatic
		// look it reaches through enter_room is the only one the command needs.
		var moveResult = await MoveService.SafeTel(parser, executor.AsContent, homeLocation,
			noMoveMsgs: false, executor.Object().DBRef, "home");

		if (moveResult is Error<string> error)
		{
			await NotifyService.Notify(executor, error.Value, executor);
			return CallState.Empty;
		}

		return new CallState(homeObj.DBRef.ToString());
	}

	[SharpCommand(Name = "FOLLOW", Switches = [], Behavior = CB.Player | CB.Thing | CB.NoGagged, MinArgs = 0,
		MaxArgs = 0, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Follow(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		if (!args.ContainsKey("0") || string.IsNullOrWhiteSpace(args["0"].Message?.ToPlainText()))
		{
			await NotifyService.Notify(executor, "Follow whom?", executor);
			return CallState.Empty;
		}

		var targetName = args["0"].Message!.ToPlainText();

		var targetResult = await LocateService.LocateAndNotifyIfInvalid(
			parser, executor, executor, targetName, LocateFlags.All);

		if (targetResult is not AnySharpObject target)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontSeeThatHere), executor);
			return CallState.Empty;
		}

		if (target.Object().DBRef.Equals(executor.Object().DBRef))
		{
			await NotifyService.Notify(executor, "You can't follow yourself.", executor);
			return CallState.Empty;
		}

		if (!target.IsPlayer && !target.IsThing)
		{
			await NotifyService.Notify(executor, "You can't follow that.", executor);
			return CallState.Empty;
		}

		// PennMUSH add_follow (move.c:1246) writes both halves: FOLLOWING on the follower and
		// FOLLOWERS on the leader. SharpMUSH's FOLLOWING holds one leader, so switching leaders has
		// to come off the previous one's list first or the two lists drift apart.
		var previousLeader = await LeaderOfAsync(executor);

		var followSet = await SetFollowingAsync(executor, target);
		if (followSet is Error<string> error)
		{
			await NotifyService.Notify(executor, error.Value, executor);
			return CallState.Empty;
		}

		if (previousLeader is not null && !previousLeader.Object().DBRef.Equals(target.Object().DBRef))
		{
			await RemoveFollowerAsync(previousLeader, executor);
		}

		await AddFollowerAsync(target, executor);

		await NotifyService.Notify(executor, $"You are now following {target.Object().Name}.", executor);
		await NotifyService.Notify(target, $"{executor.Object().Name} is now following you.", executor);

		return CallState.Empty;
	}

	[SharpCommand(Name = "UNFOLLOW", Switches = [], Behavior = CB.Player | CB.Thing | CB.NoGagged, MinArgs = 0,
		MaxArgs = 0, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> UnFollow(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var followingAttr = await AttributeService.GetAttributeAsync(executor, executor, AttrFollowing,
			IAttributeService.AttributeMode.Read, false);

		if (followingAttr.IsNone || followingAttr.IsError)
		{
			await NotifyService.Notify(executor, "You aren't following anyone.", executor);
			return CallState.Empty;
		}

		// del_follow removes from both lists (move.c:1292).
		var unfollowed = await StopFollowingAsync(executor);
		if (unfollowed is Error<string> error)
		{
			await NotifyService.Notify(executor, error.Value, executor);
			return CallState.Empty;
		}

		await NotifyService.Notify(executor, "You stop following.", executor);
		return CallState.Empty;
	}

	[SharpCommand(Name = "DESERT", Switches = [], Behavior = CB.Player | CB.Thing, MinArgs = 0, MaxArgs = 1, ParameterNames = ["follower"])]
	public async ValueTask<Option<CallState>> Desert(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		if (!args.ContainsKey("0") || string.IsNullOrWhiteSpace(args["0"].Message?.ToPlainText()))
		{
			// clear_following then clear_followers (move.c:1201-1202), both of which keep the two
			// lists in step. The leader's FOLLOWERS is what names the followers, so no scan of every
			// object's FOLLOWING is needed to find them.
			var selfCleared = await StopFollowingAsync(executor);
			if (selfCleared is Error<string> error)
			{
				await NotifyService.Notify(executor, error.Value, executor);
				return CallState.Empty;
			}

			await ClearFollowersAsync(executor);

			await NotifyService.Notify(executor, "You stop following and dismiss all followers.", executor);
			return CallState.Empty;
		}

		var targetName = args["0"].Message!.ToPlainText();

		var targetResult = await LocateService.LocateAndNotifyIfInvalid(
			parser, executor, executor, targetName, LocateFlags.All);

		if (targetResult is not AnySharpObject target)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontSeeThatHere), executor);
			return CallState.Empty;
		}

		var followingAttr = await AttributeService.GetAttributeAsync(executor, executor, AttrFollowing,
			IAttributeService.AttributeMode.Read, false);

		if (followingAttr is SharpAttribute[] followingChain)
		{
			var followingDbref = followingChain.Last().Value.ToPlainText();
			if (followingDbref == target.Object().DBRef.ToString())
			{
				// del_follow(player, who) — both lists (move.c:1197).
				var cleared = await StopFollowingAsync(executor);
				if (cleared is Error<string> error)
				{
					await NotifyService.Notify(executor, error.Value, executor);
					return CallState.Empty;
				}

				await NotifyService.Notify(executor, $"You stop following {target.Object().Name}.", executor);
			}
		}

		var targetFollowingAttr = await AttributeService.GetAttributeAsync(executor, target, AttrFollowing,
			IAttributeService.AttributeMode.Read, false);

		if (targetFollowingAttr is SharpAttribute[] targetFollowingChain)
		{
			var targetFollowingDbref = targetFollowingChain.Last().Value.ToPlainText();
			if (targetFollowingDbref == executor.Object().DBRef.ToString())
			{
				// del_follow(who, player) — the other direction (move.c:1198).
				var dismissed = await StopFollowingAsync(target);
				if (dismissed is Error<string> error)
				{
					await NotifyService.Notify(executor, error.Value, executor);
					return CallState.Empty;
				}

				await NotifyService.Notify(executor, $"You dismiss {target.Object().Name}.", executor);
				await NotifyService.Notify(target, $"{executor.Object().Name} deserts you. You stop following.", executor);
			}
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "DISMISS", Switches = [], Behavior = CB.Player | CB.Thing, MinArgs = 0, MaxArgs = 1, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Dismiss(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		if (!args.ContainsKey("0") || string.IsNullOrWhiteSpace(args["0"].Message?.ToPlainText()))
		{
			// clear_followers (move.c:1163). The leader's own FOLLOWERS names them, so nothing has to
			// walk every object in the database to find out who was following.
			var dismissed = await ClearFollowersAsync(executor);

			foreach (var follower in dismissed)
			{
				await NotifyService.Notify(follower, $"{executor.Object().Name} dismisses you. You stop following.", executor);
			}

			await NotifyService.Notify(executor, $"You dismiss all your followers. ({dismissed.Length} dismissed)", executor);
			return CallState.Empty;
		}

		var targetName = args["0"].Message!.ToPlainText();

		var targetResult = await LocateService.LocateAndNotifyIfInvalid(
			parser, executor, executor, targetName, LocateFlags.All);

		if (targetResult is not AnySharpObject target)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontSeeThatHere), executor);
			return CallState.Empty;
		}

		var followingAttr = await AttributeService.GetAttributeAsync(executor, target, AttrFollowing,
			IAttributeService.AttributeMode.Read, false);

		if (followingAttr is not SharpAttribute[] followingChain)
		{
			await NotifyService.Notify(executor, $"{target.Object().Name} is not following you.", executor);
			return CallState.Empty;
		}

		var followingDbref = followingChain.Last().Value.ToPlainText();
		if (followingDbref != executor.Object().DBRef.ToString())
		{
			await NotifyService.Notify(executor, $"{target.Object().Name} is not following you.", executor);
			return CallState.Empty;
		}

		// del_follow(player, follower) — both lists (move.c:1162).
		var targetDismissed = await StopFollowingAsync(target);
		if (targetDismissed is Error<string> error)
		{
			await NotifyService.Notify(executor, error.Value, executor);
			return CallState.Empty;
		}

		await NotifyService.Notify(executor, $"You dismiss {target.Object().Name}.", executor);
		await NotifyService.Notify(target, $"{executor.Object().Name} dismisses you. You stop following.", executor);

		return CallState.Empty;
	}
}
