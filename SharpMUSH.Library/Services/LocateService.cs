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
	///
	/// It also holds the search's invariants, which is the other half of what those locals are:
	/// <c>flags</c>, <c>type</c> and <c>abs</c> are read once per candidate and never change, so
	/// deriving them per candidate meant re-running a <see cref="Regex"/> and four
	/// <see cref="Enum.HasFlag"/> tests down a hot loop.
	/// </summary>
	internal sealed class MatchState(LocateFlags flags, Option<DBRef> absolute, int final)
	{
		/// <summary>match.c's <c>flags</c>, after <c>parse_english</c> has had its say.</summary>
		public LocateFlags Flags { get; } = flags;

		/// <summary>match.c's <c>type</c>. Derived from <see cref="Flags"/> exactly once.</summary>
		public SharpObjectTypes Preferred { get; } = PreferredTypes(flags);

		/// <summary>match.c's <c>abs</c> — <c>parse_objid(xname)</c>, before english matching.</summary>
		public Option<DBRef> Absolute { get; } = absolute;

		/// <summary>The N of an English ordinal match; 0 when the search is not one.</summary>
		public int Final { get; } = final;

		/// <summary>match.c's <c>bestmatch</c>.</summary>
		public AnyOptionalSharpObjectOrError Best { get; set; } = new None();

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
			// Anything else that arrived as an error carries its own wording (the looker gate, say).
			{ IsError: true, AsError.Value: var e } when e != ErrorMessages.Returns.CantSeeThat => e,
			// match.c's remaining arm is "I can't see that here." for a plain miss as much as for a
			// candidate that could not be seen. Notifications.NoMatch ("I don't see that here.") is
			// do_look's string, not match_result_internal's.
			_ => ErrorMessages.Notifications.CantSeeThat
		};

	public async ValueTask<AnyOptionalSharpObjectOrError> LocateAndNotifyIfInvalid(IMUSHCodeParser parser,
		AnySharpObject looker, AnySharpObject executor, string name, LocateFlags flags)
	{
		var (loc, noControl) = await LocateWithDiagnosis(looker, executor, name, flags);
		if (!loc.IsValid())
		{
			var caller = await parser.CurrentState.CallerObject(mediator);
			await notifyService.Notify(executor, LocateNotifyMessage(loc, noControl), Sender(caller));
		}

		return loc;
	}

	public async ValueTask<AnySharpObjectOrErrorCallState> LocateAndNotifyIfInvalidWithCallState(IMUSHCodeParser parser,
		AnySharpObject looker, AnySharpObject executor,
		string name, LocateFlags flags)
	{
		var (loc, noControl) = await LocateWithDiagnosis(looker, executor, name, flags);
		if (loc.IsValid())
		{
			return loc.AsAnyObject;
		}

		var caller = await parser.CurrentState.CallerObject(mediator);
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
	/// Everything that is not a place to look, and so does not count as having named a scope.
	///
	/// match_result takes the preferred <c>type</c> as a parameter *separate* from <c>flags</c>, and
	/// fun_locate's injection tests only <c>match_flags &amp; ~(MAT_CHECK_KEYS | MAT_TYPE | MAT_EXACT |
	/// MAT_CONTROL)</c> — so a bare type preference leaves match_flags empty and the default set is
	/// injected. SharpMUSH folds the type into the same word, which is why the type bits have to be
	/// named here: without them `LocateFlags.PlayersPreference` alone reads as a scope, suppresses the
	/// injection, and leaves a search with nowhere to look that can only ever return nothing.
	///
	/// <c>MAT_NEAR</c> and <c>MAT_CONTENTS</c> are here for the same reason and are the same kind of
	/// thing — filters over whatever the scopes turn up. No fun_locate switch sets either, so Penn's
	/// four-bit test never had to name them; SharpMUSH's internal callers pass them bare, and a filter
	/// that suppressed the injection left those searches with nowhere to look at all.
	/// </summary>
	private const LocateFlags NonScopeFlags =
		// match.c's four modifiers.
		LocateFlags.PreferLockPass | LocateFlags.OnlyMatchTypePreference | LocateFlags.NoPartialMatches |
		LocateFlags.OnlyMatchLookerControlledObjects |
		// MAT_NEAR and MAT_CONTENTS, which narrow a scope rather than naming one.
		LocateFlags.OnlyMatchObjectsInLookerLocation | LocateFlags.OnlyMatchObjectsInLookerInventory |
		// The `type` parameter, which is not part of match_flags at all.
		LocateFlags.NoTypePreference | LocateFlags.PlayersPreference | LocateFlags.RoomsPreference |
		LocateFlags.ThingsPreference | LocateFlags.ExitsPreference |
		// SharpMUSH's own, and MAT_LAST, which fun_locate applies at the call rather than in match_flags.
		LocateFlags.UseLastIfAmbiguous;

	/// <summary>
	/// The scopes that make a search depend on where the looker stands, and so require the executor to
	/// be able to stand there too. fun_locate gates on exactly this set (fundb.c: <c>MAT_NEIGHBOR |
	/// MAT_CONTAINER | MAT_POSSESSION | MAT_HERE | MAT_EXIT | MAT_CARRIED_EXIT</c>).
	/// </summary>
	private const LocateFlags LookerRelativeScopes =
		LocateFlags.MatchObjectsInLookerLocation | LocateFlags.MatchAgainstLookerLocationName |
		LocateFlags.MatchObjectsInLookerInventory | LocateFlags.MatchHereForLookerLocation |
		LocateFlags.ExitsInTheRoomOfLooker | LocateFlags.ExitsInsideOfLooker;

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
		// fun_locate: with no scope named there is nowhere to search, so inject the default set. The old
		// test asked whether four unrelated flags were absent, which is a different question and fired
		// even when a scope had been named.
		if ((flags & ~NonScopeFlags) == 0)
		{
			flags |= LocateFlags.All | LocateFlags.MatchAgainstLookerLocationName | LocateFlags.ExitsInsideOfLooker;
		}

		if ((flags & LookerRelativeScopes) != 0
				// Cheapest first: See_All is a flag read, Nearby resolves up to two locations.
				&& !await executor.IsSee_All() && !await Nearby(executor, looker)
				&& !await permissionService.Controls(executor, looker))
		{
			return (new Error<string>(ErrorMessages.Returns.CannotEvaluateOnLooker), false);
		}

		// match.c parses `abs` once, from the name as typed and before english matching. It is asked for
		// three times over a search — here, in the dbref scope, and once per candidate — and every one
		// of those was re-running the same GeneratedRegex over the same string.
		var absolute = HelperFunctions.ParseDbRef(name);

		// And that is the whole of it. match_result_internal asks exactly one permission question of a
		// candidate — can_interact(match, who, INTERACT_MATCH), which MatchList does per candidate — and
		// there is no Can_Examine, DarkLegal or INTERACT_SEE anywhere in match.c. That block belongs to
		// fun_locate, which applies it to what match_result hands back; it lives in Functions.Locate now.
		// Having it here put fun_locate's filter on every caller, so commands rejected objects PennMUSH
		// resolves and reported them as "I can't see that here" rather than as a suppressed match.
		return await LocateMatch(executor, looker, flags, name, absolute);
	}

	// A player-name match is GLOBAL in PennMUSH: pmatch()/player lookup resolves any player by name
	// regardless of where they stand or whether the looker can "see" them. It no longer needs a flag to
	// say so — the dark/can-examine gate that used to reject a perfectly valid player here (404'ing
	// GET /api/profile/<name> for every character, since the profile http_handler #4 is neither near nor
	// a controller) was fun_locate's, and has gone back there.
	// AbsoluteMatch because lookup_player resolves "#1" as readily as a name, and every caller of this
	// helper hands it whatever the user typed. It used to arrive by accident: the flag set names a scope,
	// so nothing was injected, and a dbref reached no scope at all.
	private const LocateFlags PlayerMatchFlags =
		LocateFlags.PlayersPreference | LocateFlags.OnlyMatchTypePreference | LocateFlags.EnglishStyleMatching |
		LocateFlags.MatchOptionalWildCardForPlayerName | LocateFlags.AbsoluteMatch;

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

	// PennMUSH's match_result_internal has one object; SharpMUSH splits it. `executor` is match.c's
	// `who` — the permission subject every Controls/CanInteract/Nearby/Long_Fingers question is asked
	// of. `looker` is its `where` — the search origin, whose surroundings are walked and whose "me"
	// and "here" these are.
	//
	// None of the special-case blocks fails the search. match.c returns on a control *pass* and
	// otherwise sets nocontrol and falls through to normal matching, so "me" with MAT_CONTROL over an
	// object you do not control is not an error — it is a search that carries on and reports
	// "Permission denied." if it finds nothing else.
	private async ValueTask<(AnyOptionalSharpObjectOrError Match, bool NoControl)> LocateMatch(
		AnySharpObject executor,
		AnySharpObject looker,
		LocateFlags flags,
		string name,
		Option<DBRef> absolute)
	{
		var preferred = PreferredTypes(flags);
		var noControl = false;

		// match.c: loc = where for a room, Source(where) for an exit — the room it sits in, not where it
		// leads — and Location(where) otherwise. FriendlyWhereIs is all three.
		var location = await FriendlyWhereIs(looker);

		// MATCH_CONTENTS: under MAT_CONTENTS a candidate has to be in the looker's own contents.
		async ValueTask<bool> InLookerContents(AnySharpObject candidate)
			=> !flags.HasFlag(LocateFlags.OnlyMatchObjectsInLookerInventory)
				 || (await FriendlyWhereIs(candidate)).Object().DBRef == looker.Object().DBRef;

		// "me"
		if (TypeAllows(preferred, flags, TypeOf(looker))
				&& flags.HasFlag(LocateFlags.MatchMeForLooker)
				&& !flags.HasFlag(LocateFlags.OnlyMatchObjectsInLookerInventory)
				&& name.Equals("me", StringComparison.OrdinalIgnoreCase))
		{
			if (!flags.HasFlag(LocateFlags.OnlyMatchLookerControlledObjects)
					|| await permissionService.Controls(executor, looker))
			{
				return (looker.WithNoneOption().WithErrorOption(), noControl);
			}

			noControl = true;
		}

		// "here" — match.c takes Location(where), and NOTHING when where is itself a room, so a room
		// looking for "here" falls through to normal matching rather than answering with itself.
		if (!looker.IsRoom
				&& flags.HasFlag(LocateFlags.MatchHereForLookerLocation)
				&& !flags.HasFlag(LocateFlags.OnlyMatchObjectsInLookerInventory)
				&& name.Equals("here", StringComparison.OrdinalIgnoreCase)
				&& TypeAllows(preferred, flags, TypeOf(location.WithExitOption())))
		{
			if (!flags.HasFlag(LocateFlags.OnlyMatchLookerControlledObjects)
					|| await permissionService.Controls(executor, location.WithExitOption()))
			{
				return (location.WithExitOption().WithNoneOption().WithErrorOption(), noControl);
			}

			noControl = true;
		}

		// "*<player>" under MAT_PLAYER, or any name at all under MAT_PMATCH.
		if ((flags.HasFlag(LocateFlags.MatchOptionalWildCardForPlayerName)
				 || (flags.HasFlag(LocateFlags.MatchWildCardForPlayerName) && name.StartsWith('*')))
				&& TypeAllows(preferred, flags, SharpObjectTypes.Player))
		{
			// The leading '*' is a player-name indicator, not a wildcard: strip it before the lookup so
			// locate(%#, "*God", "p") finds the player named God.
			var playerName = name.StartsWith('*') ? name[1..] : name;
			var player = await mediator.CreateStream(new GetPlayerQuery(playerName)).FirstOrDefaultAsync();

			if (player is not null)
			{
				AnySharpObject found = player;
				if (await InLookerContents(found)
						&& (!flags.HasFlag(LocateFlags.OnlyMatchObjectsInLookerLocation)
								|| await executor.HasLongFingers()
								|| await Nearby(executor, found)
								|| await permissionService.Controls(executor, found)))
				{
					if (!flags.HasFlag(LocateFlags.OnlyMatchLookerControlledObjects)
							|| await permissionService.Controls(executor, found))
					{
						return (found.WithNoneOption().WithErrorOption(), noControl);
					}

					noControl = true;
				}
			}
		}

		// "#<dbref>"
		if (absolute.IsSome() && flags.HasFlag(LocateFlags.AbsoluteMatch))
		{
			var found = (await mediator.Send(new GetObjectNodeQuery(absolute.AsValue()))).WithErrorOption();
			if (!found.IsNone)
			{
				var known = found.WithoutError().WithoutNone();
				if (TypeAllows(preferred, flags, TypeOf(known))
						&& await InLookerContents(known)
						&& (!flags.HasFlag(LocateFlags.OnlyMatchObjectsInLookerLocation)
								|| await executor.HasLongFingers()
								|| await Nearby(executor, known)
								|| await permissionService.Controls(executor, known)))
				{
					// MATCH_CONTROLS is per candidate — the object that matched, never the search origin.
					if (!flags.HasFlag(LocateFlags.OnlyMatchLookerControlledObjects)
							|| await permissionService.Controls(executor, known))
					{
						return (found, noControl);
					}

					noControl = true;
				}
			}
		}

		var final = 0;
		if (flags.HasFlag(LocateFlags.EnglishStyleMatching))
		{
			(name, flags, final) = ParseEnglish(name, flags);
		}

		var state = new MatchState(flags, absolute, final) { NoControl = noControl };
		await MatchList(state, Candidates(looker, location, state), executor, name);

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
	/// match.c's <c>MATCH_TYPE</c>, whose third state is the point: a wrong-type object is only rejected
	/// under <c>MAT_TYPE</c>, and is otherwise still matchable and merely loses in ChooseThing.
	///
	/// The first arm is not redundant. PennMUSH's <c>NOTYPE</c> is <c>0xFFFF</c> — every bit set, so
	/// <c>type &amp; Typeof(match)</c> is truthy for anything and a search with no preference matches
	/// everything even under <c>MAT_TYPE</c>. <see cref="SharpObjectTypes.None"/> is that same sentinel
	/// spelt as zero, which masks to the exact opposite answer and has to be named.
	/// </summary>
	private static bool TypeAllows(SharpObjectTypes preferred, LocateFlags flags, SharpObjectTypes actual)
		=> preferred == SharpObjectTypes.None
			 || (preferred & actual) != SharpObjectTypes.None
			 || !flags.HasFlag(LocateFlags.OnlyMatchTypePreference);

	/// <summary>
	/// Every place a name may be found, in match.c's order, as one lazy stream. Each scope is gated on
	/// the flag that says *where to look*; a type preference says what to prefer once looked, and is no
	/// longer spelt with the same bits. Laziness is what MATCH_LIST's opening <c>if (done) break</c>
	/// buys: <see cref="MatchList"/> stops enumerating on the Nth ordinal match, so a scope past it is
	/// never queried.
	/// </summary>
	private async IAsyncEnumerable<AnySharpObject> Candidates(
		AnySharpObject looker,
		AnySharpContainer location,
		MatchState state)
	{
		var flags = state.Flags;
		var reader = new ContentsReader(mediator);
		var sameSpot = location.Object().DBRef == looker.Object().DBRef;
		var contentsOnly = flags.HasFlag(LocateFlags.OnlyMatchObjectsInLookerInventory);

		// Exits are only walked when they are a preferred type, or when nothing is being filtered by
		// type at all — match.c's `(type & TYPE_EXIT) || !(flags & MAT_TYPE)`, which wraps both exit
		// scopes and neither of the others.
		var walksExits = TypeAllows(state.Preferred, flags, SharpObjectTypes.Exit);

		// MAT_POSSESSION — the looker's own contents.
		if (flags.HasFlag(LocateFlags.MatchObjectsInLookerInventory) && looker.IsContainer)
		{
			foreach (var candidate in ContentsOf(await reader.Of(looker.AsContainer))) yield return candidate;
		}

		// MAT_NEIGHBOR — what is in the room with the looker.
		if (flags.HasFlag(LocateFlags.MatchObjectsInLookerLocation) && !contentsOnly && !sameSpot)
		{
			foreach (var candidate in ContentsOf(await reader.Of(location))) yield return candidate;
		}

		// MAT_EXIT, and note the order: the zone master room and the master room are searched *before*
		// the looker's own room, not after.
		if (walksExits && flags.HasFlag(LocateFlags.ExitsInTheRoomOfLooker) && location.IsRoom)
		{
			var remote = !flags.HasFlag(LocateFlags.OnlyMatchObjectsInLookerLocation) && !contentsOnly;

			// MAT_REMOTES — the Zone Master Room's exits.
			if (flags.HasFlag(LocateFlags.MatchRemoteContents) && remote)
			{
				var zone = await location.WithExitOption().Object().Zone.WithCancellation(CancellationToken.None);
				if (!zone.IsNone && zone.Known.IsRoom)
				{
					foreach (var candidate in ExitsIn(await reader.Of(zone.Known.AsRoom))) yield return candidate;
				}
			}

			// MAT_GLOBAL — the Master Room's exits.
			if (flags.HasFlag(LocateFlags.MatchGlobalExits) && remote)
			{
				var masterRoom = new DBRef(Convert.ToInt32(configuration.CurrentValue.Database.MasterRoom));
				foreach (var candidate in ExitsIn(await reader.Of(masterRoom))) yield return candidate;
			}

			foreach (var candidate in ExitsIn(await reader.Of(location))) yield return candidate;
		}

		// MAT_CONTAINER — the looker's location, matched by its *own* name. One candidate, not its
		// contents: that is MAT_NEIGHBOR above, and the two were crossed for as long as this existed.
		if (flags.HasFlag(LocateFlags.MatchAgainstLookerLocationName) && !contentsOnly)
		{
			yield return location.WithExitOption();
		}

		// MAT_CARRIED_EXIT — exits held by a looker that is itself a room.
		if (walksExits
				&& flags.HasFlag(LocateFlags.ExitsInsideOfLooker)
				&& looker.IsRoom
				&& (!sameSpot || !flags.HasFlag(LocateFlags.ExitsInTheRoomOfLooker)))
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
	internal async ValueTask MatchList(
		MatchState state,
		IAsyncEnumerable<AnySharpObject> list,
		AnySharpObject executor,
		string name)
	{
		var abs = state.Absolute;
		var allowPartial = !state.Flags.HasFlag(LocateFlags.NoPartialMatches);

		await foreach (var cur in list)
		{
			// match.c MATCH_TYPE: a wrong-type object is only *skipped* under MAT_TYPE. Otherwise it
			// stays a candidate and merely loses to a preferred-type one in ChooseThing.
			if (!TypeAllows(state.Preferred, state.Flags, TypeOf(cur))) continue;

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

			await Matched(state, cur, kind == MatchKind.Exact, executor);
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
		var objectName = cur.Object().Name;
		// match_aliases answers for players and exits and returns 0 for everything else, so the array is
		// not even worth fetching otherwise.
		ReadOnlySpan<string> aliases = cur.IsPlayer || cur.IsExit ? cur.Aliases : [];

		if (AnyAlias(aliases, name, prefix: false)
				|| objectName.Equals(name, StringComparison.OrdinalIgnoreCase))
		{
			return MatchKind.Exact;
		}

		if (!allowPartial) return MatchKind.None;

		return (cur.IsPlayer && AnyAlias(aliases, name, prefix: true))
					 || (!cur.IsExit && StringMatch(objectName, name))
			? MatchKind.Partial
			: MatchKind.None;
	}

	/// <summary>
	/// PennMUSH's <c>string_match</c> (strutil.c): <paramref name="sub"/> is a prefix of <em>any word</em>
	/// of <paramref name="src"/>, not just of the whole string. `sword` matches `Big Sword`, which is how
	/// players ordinarily refer to things — most object names are more than one word, and testing only
	/// the first left every one of them reachable by its leading word or in full and no other way.
	/// </summary>
	/// <remarks>
	/// The word separator is <c>isalnum</c>, not whitespace: `Myrddin's` advances past the apostrophe to
	/// `s`, so splitting on spaces is not the same function. Runs once per candidate per locate, so it
	/// walks spans rather than allocating.
	/// </remarks>
	private static bool StringMatch(ReadOnlySpan<char> src, ReadOnlySpan<char> sub)
	{
		if (sub.IsEmpty) return false;

		while (!src.IsEmpty)
		{
			if (src.StartsWith(sub, StringComparison.OrdinalIgnoreCase)) return true;

			// Scan to the beginning of the next word, exactly as string_match does. IsLetterOrDigit stands
			// in for isalnum, which PennMUSH runs under a UTF-8 ctype locale, so both are Unicode-aware.
			var i = 0;
			while (i < src.Length && char.IsLetterOrDigit(src[i])) i++;
			while (i < src.Length && !char.IsLetterOrDigit(src[i])) i++;

			// One of those two scans always advances, so i >= 1 — but this is a per-candidate loop and a
			// hang here would be a denial of service, so don't rest the whole thing on that reasoning.
			src = src[Math.Max(i, 1)..];
		}

		return false;
	}

	/// <summary>
	/// A span walk rather than <c>aliases.Any(a =&gt; …)</c>: this runs up to twice per candidate per
	/// locate, and the predicate would capture <paramref name="name"/> into a fresh closure every time.
	/// </summary>
	private static bool AnyAlias(ReadOnlySpan<string> aliases, string name, bool prefix)
	{
		foreach (var alias in aliases)
		{
			if (prefix
						? alias.StartsWith(name, StringComparison.OrdinalIgnoreCase)
						: alias.Equals(name, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	private async ValueTask<AnyOptionalSharpObject> ChooseThing(AnySharpObject who,
		MatchState state,
		AnyOptionalSharpObject thing1, AnyOptionalSharpObject thing2)
	{
		switch (thing1, thing2)
		{
			case ({ IsNone: true }, { IsNone: true }): return new None();
			case ({ IsNone: true }, _): return thing2;
			case (_, { IsNone: true }): return thing1;
		}

		if (state.Preferred != SharpObjectTypes.None)
		{
			var first = (state.Preferred & TypeOf(thing1.Known)) != SharpObjectTypes.None;
			var second = (state.Preferred & TypeOf(thing2.Known)) != SharpObjectTypes.None;

			// Only decisive when exactly one is preferred; two of the preferred type fall through to
			// the lock check, which is what match.c's nested if/else-if does.
			if (first && !second) return thing1;
			if (!first && second) return thing2;
		}

		if (state.Flags.HasFlag(LocateFlags.PreferLockPass))
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
	private async ValueTask Matched(
		MatchState state,
		AnySharpObject cur,
		bool full,
		AnySharpObject executor)
	{
		if (state.Flags.HasFlag(LocateFlags.OnlyMatchLookerControlledObjects)
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

		var chosen = await ChooseThing(executor, state, state.Best.WithoutError(), cur.WithNoneOption());
		state.Best = chosen.WithErrorOption();

		// A previously matched item won on type or @lock — it stays, and cur is not counted.
		if (!chosen.IsNone && chosen.Known.Object().DBRef != cur.Object().DBRef)
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
		if (state.Preferred != SharpObjectTypes.None
				&& !chosen.IsNone
				&& (state.Preferred & TypeOf(chosen.Known)) != SharpObjectTypes.None)
		{
			state.RightType++;
		}
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

	// Not async: a room already holds the answer, and the other three hand back the AsyncLazy's own
	// (cached) Task, which a ValueTask wraps for free. The state machine this used to allocate was paid
	// once per candidate under MAT_CONTENTS, twice per Nearby, and once per hop in Room.
	public static ValueTask<AnySharpContainer> FriendlyWhereIs(AnySharpObject obj)
		=> obj.Match<ValueTask<AnySharpContainer>>(
			player => new(player.Location.WithCancellation(CancellationToken.None)),
			room => ValueTask.FromResult<AnySharpContainer>(room),
			exit => new(exit.Location.WithCancellation(CancellationToken.None)),
			thing => new(thing.Location.WithCancellation(CancellationToken.None))
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

		// Each adjective narrows the search to the scope it names by clearing the others. The masks are
		// match.c's parse_english, member for member — they had drifted, and because they are cleared
		// rather than set, a wrong bit does not fail loudly: it silently searches somewhere the adjective
		// said not to, or refuses to search the one place it said to.
		if ((flags & LocateFlags.MatchObjectsInLookerLocation) != 0)
		{
			if (name.StartsWith("this here ", StringComparison.OrdinalIgnoreCase))
			{
				// MAT_POSSESSION | MAT_EXIT
				name = name[10..];
				flags &= ~(LocateFlags.MatchObjectsInLookerInventory | LocateFlags.ExitsInTheRoomOfLooker);
			}
			else if (name.StartsWith("here ", StringComparison.OrdinalIgnoreCase) ||
							 name.StartsWith("this ", StringComparison.OrdinalIgnoreCase))
			{
				// MAT_POSSESSION | MAT_EXIT | MAT_REMOTE_CONTENTS | MAT_CONTAINER
				name = name[5..];
				flags &= ~(LocateFlags.MatchObjectsInLookerInventory | LocateFlags.ExitsInTheRoomOfLooker |
									 LocateFlags.MatchRemoteContents | LocateFlags.MatchAgainstLookerLocationName);
			}
		}

		if (((flags & LocateFlags.MatchObjectsInLookerInventory) != 0) &&
				(name.StartsWith("my ", StringComparison.OrdinalIgnoreCase) ||
				 name.StartsWith("me ", StringComparison.OrdinalIgnoreCase)))
		{
			// MAT_NEIGHBOR | MAT_EXIT | MAT_CONTAINER | MAT_REMOTE_CONTENTS. MatchObjectsInLookerLocation
			// is the one that matters and the one that was missing — the exit bit appeared twice in its
			// place, so "my sword" went on searching the room.
			name = name[3..];
			flags &= ~(LocateFlags.MatchObjectsInLookerLocation | LocateFlags.ExitsInTheRoomOfLooker |
								 LocateFlags.MatchAgainstLookerLocationName | LocateFlags.MatchRemoteContents);
		}

		if (((flags & (LocateFlags.ExitsInTheRoomOfLooker | LocateFlags.ExitsInsideOfLooker)) != 0) &&
				(name.StartsWith("toward ", StringComparison.OrdinalIgnoreCase)))
		{
			// MAT_NEIGHBOR | MAT_POSSESSION | MAT_CONTAINER | MAT_REMOTE_CONTENTS. Note which is absent:
			// the exit scope is the one "toward" selects, and clearing it — as this did — left the
			// adjective unable to match any exit at all.
			name = name[7..];
			flags &= ~(LocateFlags.MatchObjectsInLookerLocation | LocateFlags.MatchObjectsInLookerInventory |
								 LocateFlags.MatchAgainstLookerLocationName | LocateFlags.MatchRemoteContents);
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
