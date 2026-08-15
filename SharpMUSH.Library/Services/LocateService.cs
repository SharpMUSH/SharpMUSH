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

	/// <summary>
	/// What PennMUSH's <c>match_result_internal</c> keeps in locals and its macros mutate in place.
	/// The C macros are textually inlined, so their <c>continue</c>/<c>break</c> reach the caller's
	/// loop; carrying that across as a control-flow token and a six-tuple only ever encoded
	/// "keep going" and "stop", so <see cref="Done"/> is the whole of it.
	/// </summary>
	/// <param name="final">The N of an English ordinal match; 0 when the search is not one.</param>
	public sealed class MatchState(int final)
	{
		/// <summary>match.c's <c>bestmatch</c>.</summary>
		public AnyOptionalSharpObjectOrError Best { get; set; } = new None();

		public int Final { get; } = final;

		/// <summary>match.c's <c>curr</c> — how many candidates have matched.</summary>
		public int Count { get; set; }

		/// <summary>How many of those were of the preferred type. Exactly one breaks a tie.</summary>
		public int RightType { get; set; }

		/// <summary>Whether any match so far was exact, which retires the partial ones.</summary>
		public bool Exact { get; set; }

		/// <summary>The Nth item of an ordinal search has been found; every remaining scope is skipped.</summary>
		public bool Done { get; set; }

		/// <summary>
		/// match.c's <c>nocontrol</c>: something answered to the name and was dropped only because
		/// <c>MAT_CONTROL</c> was set and the executor does not control it. It does not change what is
		/// returned — the search still fails — it changes what the noisy path *says*, since "I can't see
		/// that here" is a lie when the object is right there and the answer is that it isn't yours.
		/// </summary>
		public bool NoControl { get; set; }
	}

	// Notify's sender is nullable and a locate need not have a caller — a system-originated one has
	// none. WithoutNone() threw ArgumentException there, so the notification the caller asked for became
	// an exception instead.
	private static AnySharpObject? Sender(AnyOptionalSharpObject caller)
		=> caller.IsNone ? null : caller.WithoutNone();

	/// <summary>What a failed noisy locate tells the executor. match.c picks between exactly these three.</summary>
	private static string LocateNotifyMessage(AnyOptionalSharpObjectOrError loc, bool noControl)
		=> loc switch
		{
			{ IsError: true, AsError.Value: var e } when e == ErrorMessages.Returns.AmbiguousMatch
				=> ErrorMessages.Notifications.AmbiguousMatch,
			_ when noControl => ErrorMessages.Notifications.PermissionDenied,
			{ IsNone: true } => ErrorMessages.Notifications.NoMatch,
			{ IsError: true, AsError.Value: var e } when e == ErrorMessages.Returns.CantSeeThat
				=> ErrorMessages.Notifications.CantSeeThat,
			{ IsError: true, AsError.Value: var e } => e,
			_ => ErrorMessages.Notifications.NoMatch
		};

	public async ValueTask<AnyOptionalSharpObjectOrError> LocateAndNotifyIfInvalid(IMUSHCodeParser parser,
		AnySharpObject looker, AnySharpObject executor, string name, LocateFlags flags)
	{
		var (loc, noControl) = await LocateWithDiagnosis(looker, executor, name, flags);
		var caller = await parser.CurrentState.CallerObject(mediator);
		if (!loc.IsValid())
		{
			await notifyService.Notify(executor, LocateNotifyMessage(loc, noControl), Sender(caller));
		}

		return loc;
	}

	public async ValueTask<AnySharpObjectOrErrorCallState> LocateAndNotifyIfInvalidWithCallState(IMUSHCodeParser parser,
		AnySharpObject looker, AnySharpObject executor,
		string name, LocateFlags flags)
	{
		var (loc, noControl) = await LocateWithDiagnosis(looker, executor, name, flags);
		var caller = await parser.CurrentState.CallerObject(mediator);
		if (loc.IsValid())
		{
			return loc.AsAnyObject;
		}

		await notifyService.Notify(executor, LocateNotifyMessage(loc, noControl), Sender(caller));
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
		=> (await LocateWithDiagnosis(looker, executor, name, flags)).Result;

	/// <summary>
	/// <see cref="Locate"/>, plus the one thing a caller cannot read off its answer: whether the search
	/// came up empty because a candidate was refused for control. Only the noisy entry points want it,
	/// and it stays off the public surface because it changes nothing about what is found.
	/// </summary>
	private async ValueTask<(AnyOptionalSharpObjectOrError Result, bool NoControl)> LocateWithDiagnosis(
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
				// Cheapest first: See_All is a flag read, Nearby resolves up to two locations.
				!await executor.IsSee_All() && !await Nearby(executor, looker) &&
				!await permissionService.Controls(executor, looker))
		{
			return (new Error<string>(ErrorMessages.Returns.CannotEvaluateOnLooker), false);
		}

		var (match, noControl) = await LocateMatch(executor, looker, flags, name);
		if (match.IsError) return (match.AsError, noControl);
		if (match.IsNone) return (match.AsNone, noControl);

		var result = match.WithoutError().WithoutNone();

		// PennMUSH: absolute dbref matches (#N) always bypass visibility checks.
		if (flags.HasFlag(LocateFlags.NoVisibilityCheck) || HelperFunctions.ParseDbRef(name).IsSome())
		{
			return (result.WithNoneOption().WithErrorOption(), noControl);
		}

		var location = await FriendlyWhereIs(result);

		if (await permissionService.CanExamine(executor, location.WithExitOption()) ||
				((!await result.IsDarkLegal() || await location.WithExitOption().IsLight() || await result.IsLight()) &&
				 await permissionService.CanInteract(executor, result, IPermissionService.InteractType.See)))
		{
			return (result.WithNoneOption().WithErrorOption(), noControl);
		}

		return (new Error<string>(ErrorMessages.Returns.CantSeeThat), noControl);
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
	private async ValueTask<(AnyOptionalSharpObjectOrError Match, bool NoControl)> LocateMatch(
		AnySharpObject executor,
		AnySharpObject looker,
		LocateFlags flags,
		string name)
	{
		AnyOptionalSharpObjectOrError match;
		AnySharpContainer location;
		var final = 0;

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
				return (looker.WithNoneOption().WithErrorOption(), false);
			}

			return (new Error<string>(ErrorMessages.Returns.PermissionDenied), true);
		}

		if (flags.HasFlag(LocateFlags.MatchHereForLookerLocation)
				&& !flags.HasFlag(LocateFlags.OnlyMatchObjectsInLookerInventory)
				&& name.Equals("here", StringComparison.InvariantCultureIgnoreCase))
		{
			if (!flags.HasFlag(LocateFlags.OnlyMatchLookerControlledObjects)
					|| await permissionService.Controls(executor, looker))
			{
				return ((await FriendlyWhereIs(looker)).WithExitOption().WithNoneOption().WithErrorOption(), false);
			}

			return (new Error<string>(ErrorMessages.Returns.PermissionDenied), true);
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
						return (match, false);
					}

					return (new Error<string>(ErrorMessages.Returns.PermissionDenied), true);
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
						return (match, false);
					}

					return (new Error<string>(ErrorMessages.Returns.PermissionDenied), true);
				}
			}
		}

		if (flags.HasFlag(LocateFlags.EnglishStyleMatching))
		{
			(name, flags, final) = ParseEnglish(name, flags);
		}

		var state = new MatchState(final);
		await MatchList(state, Candidates(looker, location, flags), executor, flags, name);

		// match.c: a `final` search that never reached the Nth item leaves bestmatch NOTHING, and
		// ambiguity is only ever considered for a non-ordinal search that matched more than once.
		if (state.Best.IsNone) return (new None(), state.NoControl);

		if (state.Final == 0
				&& state.Count > 1
				&& state.RightType != 1
				&& !flags.HasFlag(LocateFlags.UseLastIfAmbiguous))
		{
			return (new Error<string>(ErrorMessages.Returns.AmbiguousMatch), state.NoControl);
		}

		return (state.Best, state.NoControl);
	}

	/// <summary>
	/// Every place a name may be found, in PennMUSH's order, as one lazy stream. Each scope is gated on
	/// the flag that says *where to look*; a type preference says what to prefer once looked, and is no
	/// longer spelt with the same bits. Laziness is what MATCH_LIST's opening <c>if (done) break</c>
	/// buys: <see cref="MatchList"/> stops enumerating on the Nth ordinal match, so a scope past it is
	/// never queried.
	/// </summary>
	private async IAsyncEnumerable<AnySharpObject> Candidates(
		AnySharpObject looker,
		AnySharpContainer location,
		LocateFlags flags)
	{
		var reader = new ContentsReader(mediator);

		if (flags.HasFlag(LocateFlags.MatchObjectsInLookerInventory) && looker.IsContainer)
		{
			foreach (var candidate in ContentsOf(await reader.Of(looker.AsContainer))) yield return candidate;
		}

		if (flags.HasFlag(LocateFlags.MatchAgainstLookerLocationName)
				&& location.Object().DBRef != looker.Object().DBRef)
		{
			foreach (var candidate in ContentsOf(await reader.Of(location))) yield return candidate;
		}

		if (flags.HasFlag(LocateFlags.ExitsInTheRoomOfLooker) && location.IsRoom)
		{
			foreach (var candidate in ExitsIn(await reader.Of(location))) yield return candidate;

			var remote = !flags.HasFlag(LocateFlags.OnlyMatchObjectsInLookerLocation |
																	LocateFlags.OnlyMatchObjectsInLookerInventory);

			if (flags.HasFlag(LocateFlags.MatchRemoteContents) && remote)
			{
				var zone = await location.WithExitOption().Object().Zone.WithCancellation(CancellationToken.None);
				if (!zone.IsNone && zone.Known.IsRoom)
				{
					foreach (var candidate in ExitsIn(await reader.Of(zone.Known.AsRoom))) yield return candidate;
				}
			}

			if (flags.HasFlag(LocateFlags.All) && remote)
			{
				var masterRoom = new DBRef(Convert.ToInt32(configuration.CurrentValue.Database.MasterRoom));
				foreach (var candidate in ExitsIn(await reader.Of(masterRoom))) yield return candidate;
			}
		}

		// Exits carried by a looker that is itself a room.
		if (flags.HasFlag(LocateFlags.ExitsInsideOfLooker)
				&& looker.IsRoom
				&& (location.Object().DBRef != looker.Object().DBRef
						|| !flags.HasFlag(LocateFlags.ExitsInTheRoomOfLooker)))
		{
			foreach (var candidate in ExitsIn(await reader.Of(looker.AsContainer))) yield return candidate;
		}
	}

	/// <summary>
	/// One read of a container per locate. The neighbour scope and the room's exit scope draw from the
	/// same container, so a search read it twice — and <see cref="GetContentsQuery"/>'s only cache tag is
	/// <c>ObjectContents</c>, which any object moving anywhere in the game invalidates, so the second
	/// read is not reliably a cache hit. Holding the list costs nothing that was not already paid:
	/// <c>StreamQueryCachingBehavior</c> materialises the stream a layer down regardless.
	/// </summary>
	private sealed class ContentsReader(IMediator mediator)
	{
		private readonly Dictionary<int, IReadOnlyList<AnySharpContent>> _read = [];

		public ValueTask<IReadOnlyList<AnySharpContent>> Of(AnySharpContainer container)
			=> Of(container.Object().DBRef.Number, new GetContentsQuery(container));

		public ValueTask<IReadOnlyList<AnySharpContent>> Of(DBRef container)
			=> Of(container.Number, new GetContentsQuery(container));

		private async ValueTask<IReadOnlyList<AnySharpContent>> Of(int number, GetContentsQuery query)
		{
			if (_read.TryGetValue(number, out var already)) return already;

			var stream = mediator.CreateStream(query);
			IReadOnlyList<AnySharpContent> contents = stream is null ? [] : await stream.ToListAsync();

			_read[number] = contents;
			return contents;
		}
	}

	// PennMUSH keeps exits on their own chain, so Contents() never yields one and the neighbour scope
	// and the exit scope cannot overlap. GetContentsQuery returns both, so the exclusion is ours: without
	// it an exit in the room is matched twice — once here, once in ExitsIn — and reads as ambiguous.
	private static IEnumerable<AnySharpObject> ContentsOf(IReadOnlyList<AnySharpContent> contents)
		=> contents.Where(x => !x.IsExit).Select(x => x.WithRoomOption());

	private static IEnumerable<AnySharpObject> ExitsIn(IReadOnlyList<AnySharpContent> contents)
		=> contents.Where(x => x.IsExit).Select(x => new AnySharpObject(x.AsExit));

	/// <summary>PennMUSH's <c>MATCH_LIST</c> over one candidate list, accumulating into <paramref name="state"/>.</summary>
	public async ValueTask MatchList(
		MatchState state,
		IAsyncEnumerable<AnySharpObject> list,
		AnySharpObject executor,
		LocateFlags flags,
		string name)
	{
		var preferred = PreferredTypes(flags);
		var abs = HelperFunctions.ParseDbRef(name);
		var filterByType = preferred != SharpObjectTypes.None && flags.HasFlag(LocateFlags.OnlyMatchTypePreference);
		var allowPartial = !flags.HasFlag(LocateFlags.NoPartialMatches);

		await foreach (var cur in list)
		{
			// match.c MATCH_TYPE: a wrong-type object is only *skipped* under MAT_TYPE. Otherwise it
			// stays a candidate and merely loses to a preferred-type one in ChooseThing.
			if (filterByType && (preferred & TypeOf(cur)) == SharpObjectTypes.None) continue;

			// An absolute dbref match is taken ahead of can_interact, as match.c does.
			var absolute = abs.IsSome() && cur.Object().DBRef.Matches(abs.AsValue());
			var kind = absolute
				? MatchKind.Exact
				// Once an exact match exists, a partial one is not a candidate at all: without that guard
				// it still runs ChooseThing and still bumps Count, so an exact match reads as ambiguous.
				: Classify(cur, name, allowPartial && (!state.Exact || !state.Best.IsValid()));

			if (kind == MatchKind.None) continue;

			// match.c asks can_interact before comparing names. A candidate whose name does not match is
			// skipped either way, so asking only about the ones that matched is the same answer for
			// fewer questions — and this is a per-candidate permission call.
			if (!absolute
					&& !await permissionService.CanInteract(executor, cur, IPermissionService.InteractType.Match))
			{
				continue;
			}

			await Matched(state, cur, kind == MatchKind.Exact, executor, flags);
			if (state.Done) return;
		}
	}

	private enum MatchKind
	{
		None,
		Partial,
		Exact
	}

	/// <summary>
	/// How <paramref name="cur"/> answers to <paramref name="name"/>. Aliases are a player's and an
	/// exit's only (match.c's <c>match_aliases</c>), and a partial match is <c>string_match</c> —
	/// a prefix, and never against an exit.
	/// </summary>
	private static MatchKind Classify(AnySharpObject cur, string name, bool allowPartial)
	{
		if (((cur.IsPlayer || cur.IsExit)
				 && cur.Aliases.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase)))
				|| cur.Object().Name.Equals(name, StringComparison.OrdinalIgnoreCase))
		{
			return MatchKind.Exact;
		}

		if (!allowPartial) return MatchKind.None;

		return (cur.IsPlayer && cur.Aliases.Any(a => a.StartsWith(name, StringComparison.OrdinalIgnoreCase)))
					 || (!cur.IsExit && cur.Object().Name.StartsWith(name, StringComparison.OrdinalIgnoreCase))
			? MatchKind.Partial
			: MatchKind.None;
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

	/// <summary>PennMUSH's <c>MATCHED</c> macro: fold one matched candidate into the running state.</summary>
	public async ValueTask Matched(
		MatchState state,
		AnySharpObject cur,
		bool full,
		AnySharpObject executor,
		LocateFlags flags)
	{
		if (flags.HasFlag(LocateFlags.OnlyMatchLookerControlledObjects)
				&& !await permissionService.Controls(executor, cur))
		{
			// match.c sets nocontrol and continues: an uncontrolled candidate is skipped, and any
			// previously found controlled match survives. The flag is what makes the failure say
			// "Permission denied." rather than "I don't see that here."
			state.NoControl = true;
			return;
		}

		if (state.Final != 0) // An English ordinal match: count, and take only the Nth.
		{
			state.Count++;
			// match.c assigns bestmatch only on the Nth item; anything else leaves it alone, or a search
			// that never reaches N returns whatever it happened to walk past last.
			if (state.Count != state.Final) return;

			state.Best = cur.WithNoneOption().WithErrorOption();
			state.Done = true;
			return;
		}

		state.Best = (await ChooseThing(executor, flags, state.Best.WithoutError(), cur.WithNoneOption()))
			.WithErrorOption();

		// A previously matched item won on type or @lock — it stays, and cur is not counted.
		if (state.Best.IsValid() && state.Best.WithoutError().WithoutNone().Object().DBRef != cur.Object().DBRef)
		{
			return;
		}

		if (full && !state.Exact)
		{
			// Ignore any previous partial matches now we have an exact match.
			state.Exact = true;
			state.Count = 1;
			state.RightType = 0;
		}
		else
		{
			state.Count++;
		}

		// match.c: `if (type != NOTYPE && (Typeof(bestmatch) & type)) right_type++` — whether the winner
		// is *of the preferred type*. Comparing it to cur's type is a tautology here: they are the same
		// object by the time this runs.
		var preferred = PreferredTypes(flags);
		if (preferred != SharpObjectTypes.None
				&& state.Best.IsValid()
				&& (preferred & TypeOf(state.Best.WithoutError().WithoutNone())) != SharpObjectTypes.None)
		{
			state.RightType++;
		}
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
		// One location fetch per hop: the old loop awaited Location() in the condition and again in the
		// body, so every step of the containment chain cost two. A corrupted chain is caught by the
		// visited set; maxDepth bounds a pathologically long acyclic one.
		const int maxDepth = 50;

		var current = await FriendlyWhereIs(content);
		var visited = new HashSet<DBRef> { current.Object().DBRef };

		while (visited.Count <= maxDepth)
		{
			var next = await current.Location();
			if (next.Id == current.Id || !visited.Add(next.Object().DBRef)) break;

			current = next;
		}

		return current;
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