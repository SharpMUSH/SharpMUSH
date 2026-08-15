using Mediator;
using OneOf.Types;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Services;

public partial class LocateService(
	IMediator mediator,
	INotifyService notifyService,
	IPermissionService permissionService,
	IOptionsWrapper<SharpMUSHOptions> configuration) : ILocateService
{
	private static readonly Regex NthRegex = Nth();

	public enum ControlFlow
	{
		Break,
		Continue,
		Return,
		None
	};

	private static string LocateNotifyMessage(AnyOptionalSharpObjectOrError loc)
		=> loc.IsNone
			? ErrorMessages.Notifications.NoMatch
			: loc.AsError.Value == ErrorMessages.Returns.CantSeeThat
				? ErrorMessages.Notifications.CantSeeThat
				: loc.AsError.Value;

	public async ValueTask<AnyOptionalSharpObjectOrError> LocateAndNotifyIfInvalid(IMUSHCodeParser parser,
		AnySharpObject looker, AnySharpObject executor, string name, LocateFlags flags)
	{
		var loc = await Locate(parser, looker, executor, name, flags);
		var caller = await parser.CurrentState.CallerObject(mediator);
		if (!loc.IsValid())
		{
			await notifyService.Notify(executor, LocateNotifyMessage(loc), caller.WithoutNone());
		}

		return loc;
	}

	public async ValueTask<AnySharpObjectOrErrorCallState> LocateAndNotifyIfInvalidWithCallState(IMUSHCodeParser parser,
		AnySharpObject looker, AnySharpObject executor,
		string name, LocateFlags flags)
	{
		var loc = await Locate(parser, looker, executor, name, flags);
		var caller = await parser.CurrentState.CallerObject(mediator);
		if (loc.IsValid())
		{
			return loc.AsAnyObject;
		}

		await notifyService.Notify(executor, LocateNotifyMessage(loc), caller.WithoutNone());
		var callStateMessage = loc.IsError ? loc.AsError.Value : ErrorMessages.Returns.NoMatch;

		return new Error<CallState>(new CallState(callStateMessage));
	}

	public async ValueTask<CallState> LocateAndNotifyIfInvalidWithCallStateFunction(IMUSHCodeParser parser,
		AnySharpObject looker,
		AnySharpObject executor, string name, LocateFlags flags, Func<AnySharpObject, ValueTask<CallState>> foundFunc)
		=> await LocateAndNotifyIfInvalidWithCallState(parser, looker, executor, name, flags) switch
		{
			{ IsError: true, AsError: var error } => error,
			{ IsT0: true, AsSharpObject: var obj } => await foundFunc(obj),
			_ => throw new InvalidOperationException("Unexpected state in LocateAndNotifyIfInvalidWithCallStateFunction")
		};


	public async ValueTask<CallState> LocateAndNotifyIfInvalidWithCallStateFunction(IMUSHCodeParser parser,
		AnySharpObject looker,
		AnySharpObject executor, string name, LocateFlags flags, Func<AnySharpObject, CallState> foundFunc)
		=> await LocateAndNotifyIfInvalidWithCallState(parser, looker, executor, name, flags) switch
		{
			{ IsError: true, AsError: var error } => error,
			{ IsT0: true, AsSharpObject: var obj } => foundFunc(obj),
			_ => throw new InvalidOperationException("Unexpected state in LocateAndNotifyIfInvalidWithCallStateFunction")
		};

	public async ValueTask<AnyOptionalSharpObjectOrError> Locate(
		IMUSHCodeParser parser,
		AnySharpObject looker,
		AnySharpObject executor,
		string name,
		LocateFlags flags)
	{
		if (!flags.HasFlag(LocateFlags.PreferLockPass)
				&& !flags.HasFlag(LocateFlags.FailIfNotPreferred)
				&& !flags.HasFlag(LocateFlags.NoPartialMatches)
				&& !flags.HasFlag(LocateFlags.OnlyMatchObjectsInLookerLocation))
		{
			flags |= LocateFlags.All | LocateFlags.MatchAgainstLookerLocationName | LocateFlags.ExitsInsideOfLooker;
		}

		if ((flags.HasFlag(LocateFlags.MatchObjectsInLookerLocation)
				 || flags.HasFlag(LocateFlags.MatchObjectsInLookerInventory)
				 || flags.HasFlag(LocateFlags.MatchHereForLookerLocation)
				 || flags.HasFlag(LocateFlags.ExitsPreference)
				 || flags.HasFlag(LocateFlags.ExitsInsideOfLooker)) &&
				!await Nearby(executor, looker) && !await executor.IsSee_All() &&
				!await permissionService.Controls(executor, looker))
		{
			return new Error<string>("#-1 NOT PERMITTED TO EVALUATE ON LOOKER");
		}

		var match = await LocateMatch(executor, looker, flags, name);
		if (match.IsError) return match.AsError;
		if (match.IsNone) return match.AsNone;

		var result = match.WithoutError().WithoutNone();

		// PennMUSH: absolute dbref matches (#N) always bypass visibility checks.
		if (flags.HasFlag(LocateFlags.NoVisibilityCheck) || HelperFunctions.ParseDbRef(name).IsSome())
		{
			return result.WithNoneOption().WithErrorOption();
		}

		var location = await FriendlyWhereIs(result);

		if (await permissionService.CanExamine(executor, location.WithExitOption()) ||
				((!await result.IsDarkLegal() || await location.WithExitOption().IsLight() || await result.IsLight()) &&
				 await permissionService.CanInteract(executor, result, IPermissionService.InteractType.See)))
		{
			return result.WithNoneOption().WithErrorOption();
		}

		return new Error<string>(ErrorMessages.Returns.CantSeeThat);
	}

	// A player-name match is GLOBAL in PennMUSH: pmatch()/player lookup resolves any player by name
	// regardless of where they stand or whether the looker can "see" them. NoVisibilityCheck keeps
	// Locate()'s post-match dark/can-examine gate from rejecting a perfectly valid player just because
	// an unprivileged looker (e.g. the profile http_handler #4) is neither near nor a controller —
	// which 404'd GET /api/profile/<name> for every character.
	private const LocateFlags PlayerMatchFlags =
		LocateFlags.PlayersPreference | LocateFlags.OnlyMatchTypePreference | LocateFlags.EnglishStyleMatching |
		LocateFlags.MatchOptionalWildCardForPlayerName | LocateFlags.NoVisibilityCheck;

	public ValueTask<AnyOptionalSharpObjectOrError> LocatePlayerAndNotifyIfInvalid(IMUSHCodeParser parser,
		AnySharpObject looker, AnySharpObject executor,
		string name) =>
		LocateAndNotifyIfInvalid(parser, looker, executor, name, PlayerMatchFlags);

	public ValueTask<AnySharpObjectOrErrorCallState> LocatePlayerAndNotifyIfInvalidWithCallState(IMUSHCodeParser parser,
		AnySharpObject looker, AnySharpObject executor,
		string name) =>
		LocateAndNotifyIfInvalidWithCallState(parser, looker, executor, name, PlayerMatchFlags);

	public async ValueTask<CallState> LocatePlayerAndNotifyIfInvalidWithCallStateFunction(IMUSHCodeParser parser,
		AnySharpObject looker,
		AnySharpObject executor, string name, Func<SharpPlayer, ValueTask<CallState>> foundFunc)
		=> await LocatePlayerAndNotifyIfInvalidWithCallState(parser, looker, executor, name) switch
		{
			{ IsError: true, AsError: var error } => error,
			{ IsT0: true, AsSharpObject: var obj } => await foundFunc(obj.AsPlayer),
			_ => throw new InvalidOperationException("Unexpected state in LocateAndNotifyIfInvalidWithCallStateFunction")
		};

	public ValueTask<AnyOptionalSharpObjectOrError> LocatePlayer(IMUSHCodeParser parser, AnySharpObject looker,
		AnySharpObject executor, string name)
		=>
			Locate(parser, looker, executor, name, PlayerMatchFlags);

	// PennMUSH's match_result has one object; SharpMUSH splits it. `executor` is the permission
	// subject — every Controls/CanInteract/HasLongFingers question is asked of it. `looker` is the
	// search origin: whose surroundings are walked, and whose "me" and "here" these are.
	private async ValueTask<AnyOptionalSharpObjectOrError> LocateMatch(
		AnySharpObject executor,
		AnySharpObject looker,
		LocateFlags flags,
		string name)
	{
		AnyOptionalSharpObjectOrError match;
		AnyOptionalSharpObjectOrError bestMatch = new None();
		AnySharpContainer location;
		ControlFlow c;
		var final = 0;
		var curr = 0;
		var exact = false;
		var rightType = 0;

		if (looker.IsRoom)
		{
			location = looker.MinusExit();
		}
		else if (looker.IsExit)
		{
			// Search the exit's destination; an unlinked exit has none, so fall back to the room it sits in.
			var destination = await looker.MinusRoom().Home();
			location = destination.IsNone ? await FriendlyWhereIs(looker) : destination.WithoutNone();
		}
		else
		{
			location = await FriendlyWhereIs(looker);
		}

		if (!flags.HasFlag(LocateFlags.NoTypePreference)
				&& flags.HasFlag(LocateFlags.MatchMeForLooker)
				&& !flags.HasFlag(LocateFlags.OnlyMatchObjectsInLookerInventory)
				&& name.Equals("me", StringComparison.InvariantCultureIgnoreCase))
		{
			if (!flags.HasFlag(LocateFlags.OnlyMatchLookerControlledObjects)
					|| await permissionService.Controls(executor, looker))
			{
				return looker.WithNoneOption().WithErrorOption();
			}

			return new Error<string>(ErrorMessages.Returns.PermissionDenied);
		}

		if (flags.HasFlag(LocateFlags.MatchHereForLookerLocation)
				&& !flags.HasFlag(LocateFlags.OnlyMatchObjectsInLookerInventory)
				&& name.Equals("here", StringComparison.InvariantCultureIgnoreCase))
		{
			if (!flags.HasFlag(LocateFlags.OnlyMatchLookerControlledObjects)
					|| await permissionService.Controls(executor, looker))
			{
				return (await FriendlyWhereIs(looker)).WithExitOption().WithNoneOption().WithErrorOption();
			}

			return new Error<string>(ErrorMessages.Returns.PermissionDenied);
		}

		if ((flags.HasFlag(LocateFlags.MatchOptionalWildCardForPlayerName)
				 || (flags.HasFlag(LocateFlags.PlayersPreference) && name.StartsWith('*')))
				&& (flags.HasFlag(LocateFlags.PlayersPreference) || flags.HasFlag(LocateFlags.NoTypePreference)))
		{
			// In PennMUSH, a name starting with '*' in locate() is a player-name prefix indicator, not a regex
			// wildcard. Strip the leading '*' before doing the global player lookup so that
			// locate(%#, "*God", "p") correctly finds the player named "God".
			var playerName = name.StartsWith('*') ? name[1..] : name;
			var maybeMatch = await mediator
				.CreateStream(new GetPlayerQuery(playerName))
				.FirstOrDefaultAsync();

			match = maybeMatch is null
				? new None()
				: maybeMatch;
			// A player-name match is GLOBAL — pmatch(name) resolves any player by name, regardless of
			// whether they are near the looker. Returning the found player must NOT require
			// MatchObjectsInLookerInventory (which pmatch does not set); gating on it dropped the match
			// and fell through to location matching, so pmatch failed for any player not co-located with
			// the looker (e.g. the profile http_handler #4 matching God).
			if (maybeMatch is not null)
			{
				var found = match.WithoutError().WithoutNone();
				if (!flags.HasFlag(LocateFlags.OnlyMatchObjectsInLookerLocation)
						|| await executor.HasLongFingers()
						|| await Nearby(looker, found)
						|| await permissionService.Controls(executor, found))
				{
					if (!flags.HasFlag(LocateFlags.OnlyMatchLookerControlledObjects)
							|| await permissionService.Controls(executor, found))
					{
						return match;
					}

					return new Error<string>(ErrorMessages.Returns.PermissionDenied);
				}
			}
		}

		var abs = HelperFunctions.ParseDbRef(name);
		if (abs.IsSome())
		{
			var absObject = await mediator.Send(new GetObjectNodeQuery(abs.AsValue()));
			match = absObject.WithErrorOption();
			if (!match.IsNone && (flags & LocateFlags.AbsoluteMatch) != 0)
			{
				var found = match.WithoutError().WithoutNone();
				if (!flags.HasFlag(LocateFlags.OnlyMatchObjectsInLookerLocation)
						|| await executor.HasLongFingers()
						|| await Nearby(looker, found)
						|| await permissionService.Controls(executor, found))
				{
					// MATCH_CONTROLS is per candidate — the object that matched, never the search origin.
					if (!flags.HasFlag(LocateFlags.OnlyMatchLookerControlledObjects)
							|| await permissionService.Controls(executor, found))
					{
						return match;
					}

					return new Error<string>(ErrorMessages.Returns.PermissionDenied);
				}
			}
		}

		if (flags.HasFlag(LocateFlags.EnglishStyleMatching))
		{
			(name, flags, final) = ParseEnglish(name, flags);
		}

		// The scopes, in PennMUSH's order. Each is gated on the flag that says *where to look*; a type
		// preference says what to prefer once looked, and is no longer spelt with the same bits.
		while (true)
		{
			if (flags.HasFlag(LocateFlags.MatchObjectsInLookerInventory) && looker.IsContainer)
			{
				(bestMatch, final, curr, rightType, exact, c) =
					await Match_List(ContentsOf(looker.AsContainer), executor, looker, bestMatch, exact, final, curr,
						rightType, flags, name);

				if (c == ControlFlow.Break) break;
			}

			if (flags.HasFlag(LocateFlags.MatchAgainstLookerLocationName)
					&& location.Object().DBRef != looker.Object().DBRef)
			{
				(bestMatch, final, curr, rightType, exact, c) =
					await Match_List(ContentsOf(location), executor, looker, bestMatch, exact, final, curr, rightType,
						flags, name);

				if (c == ControlFlow.Break) break;
			}

			if (flags.HasFlag(LocateFlags.ExitsInTheRoomOfLooker) && location.IsRoom)
			{
				// Exits in the current location.
				(bestMatch, final, curr, rightType, exact, c) =
					await Match_List(ExitsIn(location), executor, looker, bestMatch, exact, final, curr, rightType,
						flags, name);

				if (c == ControlFlow.Break) break;

				var searchesRemoteExits = !flags.HasFlag(LocateFlags.OnlyMatchObjectsInLookerLocation |
																								 LocateFlags.OnlyMatchObjectsInLookerInventory);

				// Exits in the Zone Master Room.
				if (flags.HasFlag(LocateFlags.MatchRemoteContents) && searchesRemoteExits)
				{
					var locationZone = await location.WithExitOption().Object().Zone.WithCancellation(CancellationToken.None);
					if (!locationZone.IsNone && locationZone.Known.IsRoom)
					{
						(bestMatch, final, curr, rightType, exact, c) =
							await Match_List(ExitsIn(locationZone.Known.AsRoom), executor, looker, bestMatch, exact, final,
								curr, rightType, flags, name);

						if (c == ControlFlow.Break) break;
					}
				}

				// Global exits in the Master Room.
				if (flags.HasFlag(LocateFlags.All) && searchesRemoteExits)
				{
					var masterRoom = new DBRef(Convert.ToInt32(configuration.CurrentValue.Database.MasterRoom));

					(bestMatch, final, curr, rightType, exact, c) =
						await Match_List(ExitsIn(masterRoom), executor, looker, bestMatch, exact, final, curr, rightType,
							flags, name);

					if (c == ControlFlow.Break) break;
				}
			}

			// Exits carried by a looker that is itself a room.
			if (flags.HasFlag(LocateFlags.ExitsInsideOfLooker)
					&& looker.IsRoom
					&& (location.Object().DBRef != looker.Object().DBRef
							|| !flags.HasFlag(LocateFlags.ExitsInTheRoomOfLooker)))
			{
				(bestMatch, final, curr, rightType, exact, c) =
					await Match_List(ExitsIn(looker.AsContainer), executor, looker, bestMatch, exact, final, curr,
						rightType, flags, name);

				if (c == ControlFlow.Break) break;
			}

			break;
		}

		// match.c: a `final` search that never reached the Nth item leaves bestmatch NOTHING, and
		// ambiguity is only ever considered for a non-ordinal search that matched more than once.
		if (bestMatch.IsNone) return new None();

		if (final == 0
				&& curr > 1
				&& rightType != 1
				&& !flags.HasFlag(LocateFlags.UseLastIfAmbiguous))
		{
			return new Error<string>(ErrorMessages.Returns.AmbiguousMatch);
		}

		return bestMatch;
	}

	// PennMUSH keeps exits on their own chain, so Contents() never yields one and the neighbour scope
	// and the exit scope cannot overlap. GetContentsQuery returns both, so the exclusion is ours: without
	// it an exit in the room is matched twice — once here, once in ExitsIn — and reads as ambiguous.
	private IAsyncEnumerable<AnySharpObject> ContentsOf(AnySharpContainer container)
		=> mediator.CreateStream(new GetContentsQuery(container))?.Where(x => !x.IsExit).Select(x => x.WithRoomOption())
			 ?? AsyncEnumerable.Empty<AnySharpObject>();

	private IAsyncEnumerable<AnySharpObject> ExitsIn(AnySharpContainer container)
		=> ExitsIn(new GetContentsQuery(container));

	private IAsyncEnumerable<AnySharpObject> ExitsIn(DBRef container)
		=> ExitsIn(new GetContentsQuery(container));

	private IAsyncEnumerable<AnySharpObject> ExitsIn(GetContentsQuery query)
		=> mediator.CreateStream(query)?.Where(x => x.IsExit).Select(x => new AnySharpObject(x.AsExit))
			 ?? AsyncEnumerable.Empty<AnySharpObject>();

	public async ValueTask<(AnyOptionalSharpObjectOrError BestMatch, int Final, int Curr, int RightType, bool Exact
			, ControlFlow c)>
		Match_List(
			IAsyncEnumerable<AnySharpObject> list,
			AnySharpObject looker,
			AnySharpObject where,
			AnyOptionalSharpObjectOrError bestMatch,
			bool exact,
			int final,
			int curr,
			int rightType,
			LocateFlags flags,
			string name)
	{
		ControlFlow flow = ControlFlow.Continue;
		var preferred = PreferredTypes(flags);
		var abs = HelperFunctions.ParseDbRef(name);

		await foreach (var item in list)
		{
			var cur = item;
			// match.c MATCH_TYPE: a wrong-type object is only *skipped* under MAT_TYPE. Otherwise it
			// stays a candidate and merely loses to a preferred-type one in ChooseThing.
			if (preferred != SharpObjectTypes.None
					&& (preferred & TypeOf(cur)) == SharpObjectTypes.None
					&& flags.HasFlag(LocateFlags.OnlyMatchTypePreference))
			{
				continue;
			}

			if (abs.IsSome() && cur.Object().DBRef.Matches(abs.AsValue()))
			{
				(bestMatch, final, curr, rightType, exact, flow) =
					await Matched(true, exact, final, curr, rightType, looker, where, cur, bestMatch, flags);

				if (flow == ControlFlow.Break) break;
				if (flow == ControlFlow.Continue) continue;
				if (flow == ControlFlow.Return) return (bestMatch, final, curr, rightType, exact, ControlFlow.Return);
			}
			else if (!await permissionService.CanInteract(looker, cur, IPermissionService.InteractType.Match))
			{
				continue;
			}
			// Exact name/alias match (full == true → 'exact' match in PennMUSH terms)
			else if (
				(cur.IsPlayer && cur.Aliases.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase)))
				|| (cur.IsExit && (cur.Aliases.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase)) ||
													 cur.Object().Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
				|| (!cur.IsExit && string.Equals(cur.Object().Name, name, StringComparison.OrdinalIgnoreCase)))
			{
				(bestMatch, final, curr, rightType, exact, flow) =
					await Matched(true, exact, final, curr, rightType, looker, where, cur, bestMatch, flags);

				if (flow == ControlFlow.Break) break;
				if (flow == ControlFlow.Continue) continue;
				if (flow == ControlFlow.Return) return (bestMatch, final, curr, rightType, exact, ControlFlow.Return);
			}
			// Partial (prefix) match for non-exit objects and player aliases, matching PennMUSH string_match().
			// Guarded by (!exact || !GoodObject(bestmatch)): once an exact match exists, partial matches are
			// not candidates at all — without this they still run ChooseThing and still bump curr, so an
			// exact match reads as ambiguous.
			else if (!flags.HasFlag(LocateFlags.NoPartialMatches)
							 && (!exact || !bestMatch.IsValid())
							 && ((cur.IsPlayer && cur.Aliases.Any(a => a.StartsWith(name, StringComparison.OrdinalIgnoreCase)))
									 || (!cur.IsExit && cur.Object().Name.StartsWith(name, StringComparison.OrdinalIgnoreCase))))
			{
				(bestMatch, final, curr, rightType, exact, flow) =
					await Matched(false, exact, final, curr, rightType, looker, where, cur, bestMatch, flags);

				if (flow == ControlFlow.Break) break;
				if (flow == ControlFlow.Continue) continue;
				if (flow == ControlFlow.Return) return (bestMatch, final, curr, rightType, exact, ControlFlow.Return);
			}
		}

		return (bestMatch, final, curr, rightType, exact,
			flow == ControlFlow.Break ? ControlFlow.Break : ControlFlow.Continue);
	}

	public async ValueTask<AnyOptionalSharpObject> ChooseThing(AnySharpObject who,
		LocateFlags flags,
		AnyOptionalSharpObject thing1, AnyOptionalSharpObject thing2)
	{
		switch (thing1, thing2)
		{
			case ({ IsNone: true }, { IsNone: true }): return new None();
			case ({ IsNone: true }, _): return thing2;
			case (_, { IsNone: true }): return thing1;
		}

		var preferred = PreferredTypes(flags);
		if (preferred != SharpObjectTypes.None)
		{
			var first = (preferred & TypeOf(thing1.Known)) != SharpObjectTypes.None;
			var second = (preferred & TypeOf(thing2.Known)) != SharpObjectTypes.None;

			// Only decisive when exactly one is preferred; two of the preferred type fall through to
			// the lock check, which is what match.c's nested if/else-if does.
			if (first && !second) return thing1;
			if (!first && second) return thing2;
		}

		if (flags.HasFlag(LocateFlags.PreferLockPass))
		{
			var first = await permissionService.CouldDoIt(who, thing1);
			var second = await permissionService.CouldDoIt(who, thing2);

			if (first && !second) return thing1;
			if (!first && second) return thing2;
		}

		// No luck. Return last match.
		return thing2;
	}

	public async ValueTask<(AnyOptionalSharpObjectOrError BestMatch, int Final, int Curr, int RightType, bool Exact
			, ControlFlow c)>
		Matched(
			bool full,
			bool exact,
			int final,
			int curr,
			int right_type,
			AnySharpObject looker,
			AnySharpObject where,
			AnySharpObject cur,
			AnyOptionalSharpObjectOrError bestMatch,
			LocateFlags flags)
	{
		if (flags.HasFlag(LocateFlags.OnlyMatchLookerControlledObjects)
				&& !await permissionService.Controls(looker, cur))
		{
			// PennMUSH match_list: uncontrolled objects are silently skipped (continue),
			// preserving any previously found controlled match.
			return (bestMatch, final, curr, right_type, exact, ControlFlow.Continue);
		}

		if (final != 0) // An English ordinal match: count, and take only the Nth.
		{
			curr++;
			return curr == final
				? (cur.WithNoneOption().WithErrorOption(), final, curr, right_type, exact, ControlFlow.Break)
				// match.c assigns bestmatch only on the Nth item — anything else leaves it alone, or a
				// search that never reaches N returns whatever it happened to walk past last.
				: (bestMatch, final, curr, right_type, exact, ControlFlow.Continue);
		}

		bestMatch = (await ChooseThing(looker, flags, bestMatch.WithoutError(), cur.WithNoneOption()))
			.WithErrorOption();
		if (bestMatch.IsValid() && bestMatch.WithoutError().WithoutNone().Object().DBRef != cur.Object().DBRef)
		{
			return (bestMatch, final, curr, right_type, exact, ControlFlow.Continue);
		}

		if (full)
		{
			if (exact)
			{
				curr++;
			}
			else
			{
				//  Ignore any previous partial matches now we have an exact match
				exact = true;
				curr = 1;
				right_type = 0;
			}
		}
		else
		{
			curr++;
		}

		// match.c: `if (type != NOTYPE && (Typeof(bestmatch) & type)) right_type++` — whether the winner
		// is *of the preferred type*. Comparing it to cur's type is a tautology here: bestMatch is cur.
		var preferred = PreferredTypes(flags);
		if (preferred != SharpObjectTypes.None
				&& bestMatch.IsValid()
				&& (preferred & TypeOf(bestMatch.WithoutError().WithoutNone())) != SharpObjectTypes.None)
		{
			right_type++;
		}

		return (bestMatch, final, curr, right_type, exact, ControlFlow.Continue);
	}


	public static async ValueTask<DBRef?> WhereIs(AnySharpObject thing)
	{
		if (thing.IsRoom) return null;
		var minusRoom = thing.MinusRoom();
		if (!thing.IsExit)
		{
			return (await minusRoom.Location()).Object().DBRef;
		}

		var destination = await minusRoom.Home();
		return destination.IsNone ? null : destination.WithoutNone().Object().DBRef;
	}

	public async ValueTask<AnySharpContainer> Room(AnySharpObject content)
	{
		var currentLocation = await FriendlyWhereIs(content);

		// Depth limit to prevent infinite loops from corrupted containment chains.
		// PennMUSH uses MAX_PARENTS (10) as a depth limit for similar traversals.
		const int maxDepth = 50;
		var depth = 0;
		var visited = new HashSet<string> { currentLocation.Object().DBRef.ToString() };

		while (currentLocation.Id != (await currentLocation.Location()).Id)
		{
			depth++;
			if (depth > maxDepth)
			{
				break;
			}

			currentLocation = await currentLocation.Location();

			var dbRefStr = currentLocation.Object().DBRef.ToString();
			if (!visited.Add(dbRefStr))
			{
				break;
			}
		}

		return currentLocation;
	}

	public static async ValueTask<AnySharpContainer> FriendlyWhereIs(AnySharpObject obj) => await obj.Match(
		async player => await player.Location.WithCancellation(CancellationToken.None),
		async room => await ValueTask.FromResult<AnySharpContainer>(room),
		async exit => await exit.Location.WithCancellation(CancellationToken.None),
		async thing => await thing.Location.WithCancellation(CancellationToken.None)
	);

	public static async ValueTask<bool> Nearby(
		AnySharpObject obj1,
		AnySharpObject obj2)
	{
		if (obj1.IsRoom && obj2.IsRoom) return false;

		var loc1 = (await FriendlyWhereIs(obj1)).Object().DBRef;

		if (loc1 == obj2.Object().DBRef) return true;

		var loc2 = (await FriendlyWhereIs(obj2)).Object().DBRef;

		return loc2 == obj1.Object().DBRef || loc2 == loc1;
	}

	/// <summary>PennMUSH's <c>type</c> mask — <c>SharpObjectTypes.None</c> is its <c>NOTYPE</c>.</summary>
	public static SharpObjectTypes PreferredTypes(LocateFlags flags)
		=> (flags.HasFlag(LocateFlags.PlayersPreference) ? SharpObjectTypes.Player : SharpObjectTypes.None)
			 | (flags.HasFlag(LocateFlags.ThingsPreference) ? SharpObjectTypes.Thing : SharpObjectTypes.None)
			 | (flags.HasFlag(LocateFlags.RoomsPreference) ? SharpObjectTypes.Room : SharpObjectTypes.None)
			 | (flags.HasFlag(LocateFlags.ExitsPreference) ? SharpObjectTypes.Exit : SharpObjectTypes.None);

	public static SharpObjectTypes TypeOf(AnySharpObject obj)
		=> obj.IsPlayer ? SharpObjectTypes.Player
			: obj.IsRoom ? SharpObjectTypes.Room
			: obj.IsExit ? SharpObjectTypes.Exit
			: SharpObjectTypes.Thing;

	private static (string RemainingString, LocateFlags NewFlags, int Count) ParseEnglish(
		string oldName,
		LocateFlags oldFlags)
	{
		var flags = oldFlags;
		var saveFlags = flags;
		var name = oldName;
		var saveName = name;
		var count = 0;

		if ((flags & LocateFlags.MatchObjectsInLookerLocation) != 0)
		{
			if (name.StartsWith("this here ", StringComparison.OrdinalIgnoreCase))
			{
				name = name[10..];
				flags &= ~(LocateFlags.MatchObjectsInLookerInventory | LocateFlags.ExitsInTheRoomOfLooker);
			}
			else if (name.StartsWith("here ", StringComparison.OrdinalIgnoreCase) ||
							 name.StartsWith("this ", StringComparison.OrdinalIgnoreCase))
			{
				name = name[5..];
				flags &= ~(LocateFlags.MatchObjectsInLookerInventory | LocateFlags.ExitsInTheRoomOfLooker |
									 LocateFlags.MatchAgainstLookerLocationName);
			}
		}

		if (((flags & LocateFlags.MatchObjectsInLookerInventory) != 0) &&
				(name.StartsWith("my ", StringComparison.OrdinalIgnoreCase) ||
				 name.StartsWith("me ", StringComparison.OrdinalIgnoreCase)))
		{
			name = name[3..];
			flags &= ~(LocateFlags.ExitsInTheRoomOfLooker | LocateFlags.ExitsInTheRoomOfLooker |
								 LocateFlags.MatchAgainstLookerLocationName);
		}

		if (((flags & (LocateFlags.ExitsInTheRoomOfLooker | LocateFlags.ExitsInsideOfLooker)) != 0) &&
				(name.StartsWith("toward ", StringComparison.OrdinalIgnoreCase)))
		{
			name = name[7..];
			flags &= ~(LocateFlags.ExitsInTheRoomOfLooker | LocateFlags.MatchObjectsInLookerInventory |
								 LocateFlags.MatchAgainstLookerLocationName);
		}

		name = name.TrimStart();

		if (string.IsNullOrWhiteSpace(name))
		{
			return (saveName, saveFlags, 0);
		}

		if (!char.IsDigit(name[0]))
		{
			return (name, flags, 0);
		}

		var mName = name.Split(' ').FirstOrDefault();
		if (string.IsNullOrWhiteSpace(mName))
		{
			return (name, flags, 0);
		}

		var ordinalMatch = NthRegex.Match(mName);

		if (ordinalMatch.Success)
		{
			count = int.Parse(ordinalMatch.Groups["Number"].Value);
			var ordinal = ordinalMatch.Groups["Ordinal"].Value;

			// Validate the ordinal suffix, following PennMUSH parse_english() rules:
			//   11th, 12th, 13th  → always "th"  (teen exception – not st/nd/rd)
			//   *1  (excl. 11)    → "st"
			//   *2  (excl. 12)    → "nd"
			//   *3  (excl. 13)    → "rd"
			//   everything else   → "th"
			var mod100 = count % 100;
			var isTeen = mod100 >= 11 && mod100 <= 13;
			var mod10 = count % 10;

			string expectedSuffix = (isTeen || mod10 == 0 || mod10 > 3) ? "th"
				: mod10 == 1 ? "st"
				: mod10 == 2 ? "nd"
				: "rd";

			if (count < 1 || !ordinal.Equals(expectedSuffix, StringComparison.CurrentCultureIgnoreCase))
			{
				return (name, flags, 0);
			}
		}

		return (name[mName.Length..].TrimStart(), flags, count);
	}

	/// <summary>
	/// A regular expression that checks if a string is a number followed by an ordinal indicator.
	/// </summary>
	/// <returns>A regex that has a Named Group for Number and Ordinal.</returns>
	[GeneratedRegex(@"^(?<Number>\d+)(?<Ordinal>rd|th|nd|st)$")]
	private static partial Regex Nth();
}