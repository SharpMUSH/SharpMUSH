using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Utilities;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using SharpMUSH.Library.Softcode;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	/// <summary>
	/// PennMUSH's <c>do_find</c> (<c>src/look.c:1066</c>): every object you control in
	/// <c>[begin, end)</c>, exits excepted, whose name has a word starting with <c>&lt;name&gt;</c>
	/// (<c>string_match</c>), one <c>object_header</c> line each, then the count.
	/// </summary>
	[SharpCommand(Name = "@FIND", Output = CommandOutput.Value, Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged,
		MinArgs = 0, MaxArgs = 3, ParameterNames = ["name", "flags"])]
	public async ValueTask<Option<CallState>> Find(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		string Arg(string key) => args.TryGetValue(key, out var value) ? value.Message.ToPlainText().Trim() ?? "" : "";

		var name = Arg("0");
		int? begin = null;
		int? end = null;

		// A range bound may carry a '#' and must name an object that exists (GoodObject).
		foreach (var (key, assign) in new (string, Action<int>)[] { ("1", v => begin = v), ("2", v => end = v) })
		{
			var text = Arg(key);
			if (text.Length == 0) continue;
			if (!int.TryParse(text.TrimStart('#'), out var number)
				|| await Mediator.Send(new GetObjectNodeQuery(new DBRef(number))) is not AnySharpObject)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FindInvalidRange), executor);
				return new CallState(ErrorMessages.Returns.InvalidArgument);
			}

			assign(number);
		}

		var filter = new ObjectSearchFilter
		{
			Types = ["ROOM", "THING", "PLAYER"],
			MinDbRef = begin,
			// do_find's upper bound is exclusive.
			MaxDbRef = end - 1
		};

		// The output is the list lsearch(me, name, <name>) would give; the count is only shown.
		var found = new List<string>();
		await foreach (var obj in Mediator.CreateStream(new GetFilteredObjectsQuery(filter)))
		{
			if ((name.Length > 0 && !StringMatch(obj.Name, name))
				|| await Mediator.Send(new GetObjectNodeQuery(obj.DBRef)) is not AnySharpObject node
				|| !await PermissionService.Controls(executor, node))
			{
				continue;
			}

			await NotifyService.Notify(executor, await MessageFormatting.UnparseObjectAsync(PermissionService, executor, node, ConnectionService), executor);
			found.Add($"#{obj.DBRef.Number}");
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FindObjectsFoundFormat), executor, found.Count);
		return new CallState(string.Join(" ", found));
	}

	/// <summary>
	/// PennMUSH's <c>string_match</c> (<c>src/strutil.c:268</c>): <paramref name="sub"/> is a
	/// case-insensitive prefix of some word of <paramref name="src"/>, a word starting after any run of
	/// characters that are not letters or digits.
	/// </summary>
	private static bool StringMatch(string src, string sub)
	{
		var i = 0;
		while (i < src.Length)
		{
			if (src.AsSpan(i).StartsWith(sub, StringComparison.OrdinalIgnoreCase)) return true;
			while (i < src.Length && char.IsLetterOrDigit(src[i])) i++;
			while (i < src.Length && !char.IsLetterOrDigit(src[i])) i++;
		}

		return false;
	}

	[SharpCommand(Name = "@SCAN", Output = CommandOutput.Value, Switches = ["ROOM", "SELF", "ZONE", "GLOBALS"], Behavior = CB.Default | CB.NoGagged,
		MinArgs = 1, MaxArgs = 0, ParameterNames = ["object", "code"])]
	public async ValueTask<Option<CallState>> Scan(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var arg0 = parser.CurrentState.Arguments["0"].Message;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches.Any()
			? parser.CurrentState.Switches.ToArray()
			: ["ROOM", "SELF", "ZONE", "GLOBALS"];

		var perceive = await ObserveRealityAsync(parser, executor);
		var here = executor.AsOptionalContent is AnySharpContent scanner ? await scanner.Location() : null;

		// Both zones are wanted by the ZONE branch and by the master room's already-scanned guard, and
		// resolving one costs a fetch, so only pay for them when a branch that reads them will run.
		var needsZones = switches.Contains("ZONE") || switches.Contains("GLOBALS");
		var hereZone = !needsZones || here is null ? null : await ZoneOfAsync(here.Object());
		var personalZone = !needsZones ? null : await ZoneOfAsync(executor.Object());

		var run = new ScanRun(parser, executor, arg0, perceive, here, hereZone, personalZone);

		if (here is not null && switches.Contains("ROOM")) await ScanRoomAsync(run, here);
		if (switches.Contains("SELF")) await ScanSelfAsync(run);
		if (switches.Contains("ZONE")) await ScanZonesAsync(run);
		if (switches.Contains("GLOBALS")) await ScanGlobalsAsync(run);

		// The return value is the scan_list shape - a flat list of obj/attr pairs (src/game.c:1729) -
		// not the printed report, and scan_list never repeats an object: it drops CHECK_SELF once
		// CHECK_NEIGHBORS has run (src/game.c:1763-1764). Deduplicate to match, so the executor's own
		// $-command appears once here even though it is printed under two headings above.
		return new CallState(string.Join(" ", run.Matched.Distinct()));
	}

	/// <summary>One @scan: who scans for what, where they stand, and every obj/attr pair matched so far.</summary>
	private sealed record ScanRun(IMUSHCodeParser Parser, AnySharpObject Executor, MString Command,
		Func<DBRef, CancellationToken, ValueTask<bool>> Perceive, AnySharpContainer? Here, AnySharpObject? HereZone,
		AnySharpObject? PersonalZone)
	{
		public List<string> Matched { get; } = [];
	}

	private const string ScanEntry = nameof(ErrorMessages.Notifications.ScanMatchEntryFormat);

	private static async ValueTask<AnySharpObject?> ZoneOfAsync(SharpObject obj)
		=> await obj.Zone.WithCancellation(CancellationToken.None) is AnySharpObject zone ? zone : null;

	private static IAsyncEnumerable<AnySharpObject> Just(AnySharpObject obj) => new[] { obj }.ToAsyncEnumerable();

	private IAsyncEnumerable<AnySharpObject> ContentsOf(DBRef container)
		=> Mediator.CreateStream(new GetContentsQuery(container))?.Select(x => x.WithRoomOption())
			?? AsyncEnumerable.Empty<AnySharpObject>();

	private async ValueTask ScanRoomAsync(ScanRun run, AnySharpContainer here)
	{
		// Penn splits this into two flags and @scan with no switches sets both: CHECK_NEIGHBORS for
		// the contents of the location, CHECK_HERE for the location object itself
		// (src/game.c:1890-1909). The heading belongs to CHECK_NEIGHBORS and prints whether or not
		// anything matched; CHECK_HERE has no heading and prints only on a match.
		await NotifyService.NotifyLocalized(run.Executor,
			nameof(ErrorMessages.Notifications.ScanMatchesOnRoomContents), run.Executor);
		await ReportScanAsync(run, ScanEntry, await FindScanMatchesAsync(run, here.Content(Mediator).Select(x => x.WithRoomOption())));

		await ReportScanAsync(run, nameof(ErrorMessages.Notifications.ScanMatchedHereFormat),
			await FindScanMatchesAsync(run, Just(here.WithExitOption())));
	}

	private async ValueTask ScanSelfAsync(ScanRun run)
	{
		var executor = run.Executor;

		// CHECK_INVENTORY, then CHECK_SELF (src/game.c:1911-1929). The self check is not gated on
		// being a container: Penn scans the executor whether or not it can hold anything.
		await NotifyService.NotifyLocalized(executor,
			nameof(ErrorMessages.Notifications.ScanMatchesOnCarriedObjects), executor);

		if (executor.AsOptionalContainer is AnySharpContainer carrier)
		{
			await ReportScanAsync(run, ScanEntry,
				await FindScanMatchesAsync(run, carrier.Content(Mediator).Select(x => x.WithRoomOption())));
		}

		// An executor standing in the room is in its contents too, so with the default switch set a
		// $-command on the executor is reported twice - once under "Matches on contents of this room:"
		// and again as "Matched self:". do_scan makes no attempt to suppress that (unlike scan_list,
		// src/game.c:1763-1764, which has no headings to separate the two), and a live 1.8.8 prints
		// both lines, so neither does this.
		await ReportScanAsync(run, nameof(ErrorMessages.Notifications.ScanMatchedSelfFormat),
			await FindScanMatchesAsync(run, Just(executor)));
	}

	private async ValueTask ScanZonesAsync(ScanRun run)
	{
		// A zone that is a room is a Zone Master Room and its CONTENTS carry the commands, under a
		// heading; a zone that is anything else carries them itself and gets a one-line report with
		// no heading (src/game.c:1931-1981).
		if (run.HereZone is { } hereZone)
		{
			await ScanZoneAsync(run, hereZone,
				nameof(ErrorMessages.Notifications.ScanMatchesOnZoneMasterRoomOfLocation),
				nameof(ErrorMessages.Notifications.ScanMatchedZoneOfLocationFormat));
		}

		if (run.PersonalZone is { } personalZone
				&& (run.HereZone is null || personalZone.Object().DBRef != run.HereZone.Object().DBRef))
		{
			await ScanZoneAsync(run, personalZone,
				nameof(ErrorMessages.Notifications.ScanMatchesOnPersonalZoneMasterRoom),
				nameof(ErrorMessages.Notifications.ScanMatchedPersonalZoneFormat));
		}
	}

	private async ValueTask ScanZoneAsync(ScanRun run, AnySharpObject zone, string headerKey, string matchedKey)
	{
		if (!zone.IsRoom)
		{
			await ReportScanAsync(run, matchedKey, await FindScanMatchesAsync(run, Just(zone)));
			return;
		}

		// Penn guards both zone blocks with the same expression - Location(player) != Zone(player)
		// (src/game.c:1936, 1963) - which compares the location to the PERSONAL zone even while
		// scanning the location's zone. Reads like a slip, but it is what Penn does, and it is
		// materially different from comparing against the zone being scanned: with no personal
		// zone set, Zone(player) is NOTHING and the location's Zone Master Room is always scanned.
		// The heading sits inside that guard, so a suppressed block prints nothing at all.
		if (run.Here is not null && run.PersonalZone is not null
				&& run.Here.Object().DBRef == run.PersonalZone.Object().DBRef)
		{
			return;
		}

		await NotifyService.NotifyLocalized(run.Executor, headerKey, run.Executor);
		await ReportScanAsync(run, ScanEntry, await FindScanMatchesAsync(run, ContentsOf(zone.Object().DBRef)));
	}

	private async ValueTask ScanGlobalsAsync(ScanRun run)
	{
		var masterRoom = new DBRef(Convert.ToInt32(Configuration.CurrentValue.Database.MasterRoom));

		// Penn's own guard, verbatim: skip when the executor stands in the master room, or the master
		// room is either zone (src/game.c:1984-1986). Note it tests only those three dbrefs - it does
		// NOT ask whether the ROOM or ZONE branch actually ran, so `@scan/globals` from inside the
		// master room reports nothing in PennMUSH either, not even the heading.
		var alreadyScanned = run.Here?.Object().DBRef == masterRoom
			|| run.HereZone?.Object().DBRef == masterRoom
			|| run.PersonalZone?.Object().DBRef == masterRoom;

		if (alreadyScanned) return;

		await NotifyService.NotifyLocalized(run.Executor,
			nameof(ErrorMessages.Notifications.ScanMatchesOnMasterRoomObjects), run.Executor);
		await ReportScanAsync(run, ScanEntry, await FindScanMatchesAsync(run, ContentsOf(masterRoom)));
	}

	/// <summary>
	/// do_scan reports one line per OBJECT, not per attribute: atr_comm_match returns how many of
	/// that object's attributes matched and appends each as " #&lt;dbref&gt;/&lt;ATTR&gt;" to one buffer, which
	/// the caller prints as "&lt;object&gt;  [&lt;count&gt;:&lt;attrs&gt;]" (src/game.c:1895, src/attrib.c:1990-2000).
	/// Grouping here is what makes an object with two matching $-commands one line and not two.
	/// </summary>
	private async ValueTask<List<(AnySharpObject Obj, List<string> Attributes)>> FindScanMatchesAsync(
		ScanRun run, IAsyncEnumerable<AnySharpObject> candidates)
	{
		// The list keeps Penn's report order; the index keeps the grouping off O(n^2), which a
		// default scan of a well-populated master room would otherwise pay.
		List<(AnySharpObject Obj, List<string> Attributes)> grouped = [];
		Dictionary<DBRef, List<string>> byObject = [];

		var matched = await CommandDiscoveryService.MatchUserDefinedCommand(run.Parser,
			candidates.Where((item, ct) => run.Perceive(item.Object().DBRef, ct)), run.Command, run.Executor);
		if (!matched.TryGetValue(out var matches))
		{
			return grouped;
		}

		foreach (var (obj, attr, _) in matches)
		{
			if (!await CanScanAsync(run.Executor, obj))
			{
				continue;
			}

			var dbref = obj.Object().DBRef;
			run.Matched.Add($"#{dbref.Number}/{attr.LongName}");

			if (byObject.TryGetValue(dbref, out var attributes))
			{
				attributes.Add(attr.LongName);
				continue;
			}

			attributes = [attr.LongName];
			byObject[dbref] = attributes;
			grouped.Add((obj, attributes));
		}

		return grouped;
	}

	private async ValueTask<bool> CanScanAsync(AnySharpObject executor, AnySharpObject obj)
		=> await PermissionService.Controls(executor, obj) || await obj.HasFlag("VISUAL");

	/// <summary>
	/// Prints the grouped matches under the wording <paramref name="key"/> names - the bare entry under a
	/// section heading, or one of do_scan's four "Matched &lt;where&gt;:" one-liners.
	/// </summary>
	private async ValueTask ReportScanAsync(ScanRun run, string key, List<(AnySharpObject Obj, List<string> Attributes)> matches)
	{
		var executor = run.Executor;
		var flagView = await FlagView.ForAsync(executor, ConnectionService);
		foreach (var (obj, attributes) in matches)
		{
			var dbref = obj.Object().DBRef.Number;
			// The attribute list carries its own leading space, because Penn's buffer does
			// (safe_chr(' ') per match, src/attrib.c:1990) and the "[%d:%s]" format supplies none.
			var attributeList = string.Concat(attributes.Select(attribute => $" #{dbref}/{attribute}"));

			await NotifyService.NotifyLocalizedMarkup(executor, key, executor,
				await MessageFormatting.FormatObjectWithDbrefMString(obj.Object(), flagView),
				MarkupText.Plain(attributes.Count.ToString()),
				MarkupText.Plain(attributeList));
		}
	}

	[SharpCommand(Name = "@SEARCH", Output = CommandOutput.Value, Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse,
		MinArgs = 0, MaxArgs = int.MaxValue, ParameterNames = ["player", "class=restriction..."])]
	public async ValueTask<Option<CallState>> Search(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		var (playerText, pairs) = ParseSearchCommandArgs(args);

		DBRef? ownerFilter;
		if (playerText == null)
		{
			// PennMUSH: no <player> given defaults to ANY_OWNER for wizards (See_All/Search_All), else the executor's own objects.
			ownerFilter = await executor.IsWizard() ? null : executor.Object().DBRef;
		}
		else if (playerText.Equals("all", StringComparison.OrdinalIgnoreCase))
		{
			ownerFilter = null;
		}
		else if (playerText.Equals("me", StringComparison.OrdinalIgnoreCase))
		{
			ownerFilter = executor.Object().DBRef;
		}
		else
		{
			var maybeOwner = await LocateService.Locate(parser, executor, executor, playerText, LocateFlags.All);
			if (maybeOwner is not AnySharpObject owner)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SearchUnknownOwner), executor);
				return new CallState(ErrorMessages.Returns.NotFound);
			}

			ownerFilter = owner.Object().DBRef;
		}

		return await SearchSpecEngine.ExecuteResultAsync(
				parser, Mediator, LocateService, AttributeService, BooleanExpressionParser, PermissionService,
				executor, ownerFilter, pairs, useRegex: false) switch
		{
			SearchSpecEngine.SearchResult search => await ReportSearchResultAsync(executor, search),
			Error<string> rejected => await RejectSearchAsync(executor, rejected.Value)
		};
	}

	/// <summary>do_search when fill_search_spec rejects the spec: the searcher is told why, and nothing is searched.</summary>
	private async ValueTask<Option<CallState>> RejectSearchAsync(AnySharpObject executor, string notification)
	{
		await NotifyService.NotifyLocalized(executor, notification, executor);
		return new CallState(ErrorMessages.Returns.Nothing);
	}

	private async ValueTask<Option<CallState>> ReportSearchResultAsync(AnySharpObject executor, SearchSpecEngine.SearchResult search)
	{
		var matches = search.Matches;

		if (matches.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SearchNothingFound), executor);
			return new CallState(MarkupText.Empty) { HadErrors = search.HadErrors };
		}

		await ReportSearchAsync(executor, matches);

		// The list lsearch() gives for the same search; the report above is only shown.
		return new CallState(string.Join(" ", matches.Select(obj => $"#{obj.Key}"))) { HadErrors = search.HadErrors };
	}

	/// <summary>
	/// PennMUSH's <c>do_search</c> report (<c>src/wiz.c:1289-1414</c>): the matches split by type, each
	/// type under its own heading, each object as <c>object_header</c> with its owner, an exit with where
	/// it runs from and to, a player with its location when the searcher may search or see everything;
	/// then the totals.
	/// </summary>
	private async ValueTask ReportSearchAsync(AnySharpObject executor, IReadOnlyList<SharpObject> matches)
	{
		var nodes = new List<AnySharpObject>(matches.Count);
		foreach (var match in matches)
		{
			if (await Mediator.Send(new GetObjectNodeQuery(match.DBRef)) is AnySharpObject node)
			{
				nodes.Add(node);
			}
		}

		async ValueTask<string> Header(AnySharpObject obj)
			=> await MessageFormatting.UnparseObjectAsync(PermissionService, executor, obj, ConnectionService);

		async ValueTask<string> HeaderOrNowhere(DBRef? dbref)
			=> dbref is { } where && await Mediator.Send(new GetObjectNodeQuery(where)) is AnySharpObject node
				? await Header(node)
				: ErrorMessages.Notifications.SearchNowhere;

		async ValueTask<string> Owned(AnySharpObject obj)
			=> string.Format(ErrorMessages.Notifications.SearchOwnedEntryFormat, await Header(obj),
				await Header(new AnySharpObject(await obj.Object().Owner.WithCancellation(CancellationToken.None))));

		var rooms = nodes.Where(n => n.IsRoom).ToList();
		var exits = nodes.Where(n => n.IsExit).ToList();
		var things = nodes.Where(n => n.IsThing).ToList();
		var players = nodes.Where(n => n.IsPlayer).ToList();

		if (rooms.Count > 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SearchRoomsHeader), executor);
			foreach (var room in rooms)
			{
				await NotifyService.Notify(executor, await Owned(room), executor);
			}
		}

		if (exits.Count > 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SearchExitsHeader), executor);
			foreach (var exit in exits)
			{
				if (exit is not SharpExit sharpExit) continue;
				var from = AnySharpContainer.RefOf(await sharpExit.Location.WithCancellation(CancellationToken.None));
				var to = AnyOptionalSharpContainer.RefOf(await sharpExit.Home.WithCancellation(CancellationToken.None));
				await NotifyService.Notify(executor, string.Format(ErrorMessages.Notifications.SearchExitEntryFormat,
					await Header(exit), await HeaderOrNowhere(from), await HeaderOrNowhere(to)), executor);
			}
		}

		if (things.Count > 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SearchThingsHeader), executor);
			foreach (var thing in things)
			{
				await NotifyService.Notify(executor, await Owned(thing), executor);
			}
		}

		if (players.Count > 0)
		{
			var showLocation = await ObjectStatsHelpers.CanSearchAll(executor) || await executor.HasPower("SEE_ALL");
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SearchPlayersHeader), executor);
			foreach (var player in players)
			{
				var line = await Header(player);
				if (showLocation)
				{
					var location = AnySharpContainer.RefOf(await player.Where());
					line = string.Format(ErrorMessages.Notifications.SearchPlayerLocationFormat, line, await HeaderOrNowhere(location));
				}

				await NotifyService.Notify(executor, line, executor);
			}
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SearchDone), executor);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SearchTotalsFormat), executor,
			rooms.Count, exits.Count, things.Count, players.Count);
	}

	/// <summary>
	/// Parses @search's own syntax, per PennMUSH's <c>do_search</c>:
	/// <c>@search [&lt;player&gt;] [&lt;class1&gt;=&lt;restriction1&gt;[,&lt;class2&gt;=&lt;restriction2&gt;...]]</c>.
	/// <para>The command's <c>CB.EqSplit | CB.RSArgs</c> behavior only splits the RAW text on the FIRST
	/// top-level '=' (giving <c>args["0"]</c> the whole left side verbatim) and then comma-splits
	/// everything right of it (<c>args["1"]</c>, <c>args["2"]</c>, ...). Unlike lsearch(), whose
	/// positional function args are already one token per class/restriction, @search's own player name
	/// and its first search class are both crammed into that same left-hand chunk (e.g. "all type" for
	/// <c>@search all type=PLAYER</c>), and every restriction after the first carries its own class via
	/// an embedded '=' inside its comma chunk (e.g. "flags=W" in "...,flags=W"). This re-splits that
	/// left chunk on its first whitespace run, then walks the remaining chunks for their own '='.</para>
	/// </summary>
	private static (string? Player, List<SearchSpecEngine.SearchPair> Pairs) ParseSearchCommandArgs(
		IReadOnlyDictionary<string, CallState> args)
	{
		var lhs = args.TryGetValue("0", out var arg0) ? arg0.Message.ToPlainText() : "";

		var rhsChunks = Enumerable.Range(1, args.Count)
			.Select(i => i.ToString())
			.TakeWhile(args.ContainsKey)
			.Select(key => args[key].Message.ToPlainText())
			.ToList();

		var (player, leadingClass) = SplitSearchOwnerAndClass(lhs, hasRestriction: rhsChunks.Count > 0);

		List<SearchSpecEngine.SearchPair> leadingPair = leadingClass != null && rhsChunks.Count > 0
			? [new SearchSpecEngine.SearchPair(leadingClass, rhsChunks[0])]
			: [];

		var embeddedPairs = rhsChunks
			.Skip(leadingPair.Count)
			.Select(chunk => (Chunk: chunk, EqIndex: chunk.IndexOf('=')))
			.Where(split => split.EqIndex > 0)
			.Select(split => new SearchSpecEngine.SearchPair(split.Chunk[..split.EqIndex], split.Chunk[(split.EqIndex + 1)..]));

		return (player, [.. leadingPair, .. embeddedPairs]);
	}

	/// <summary>
	/// Splits @search's left-hand chunk into its player and the class of its first restriction: a quoted
	/// player name, or the first word, with whatever follows it as the class.
	/// </summary>
	private static (string? Player, string? LeadingClass) SplitSearchOwnerAndClass(string lhs, bool hasRestriction)
		=> lhs switch
		{
			"" => (null, null),
			['"', ..] => SplitQuotedSearchOwner(lhs),
			_ when lhs.IndexOf(' ') is var space and >= 0 => (lhs[..space], NonEmptyOrNull(lhs[(space + 1)..].TrimStart())),
			// A single bare token: it's the leading class if there's a restriction waiting for it
			// on the right of the '=' (e.g. "type=room"); otherwise it's a plain player/owner filter
			// (e.g. "@search SomePlayer").
			_ when hasRestriction => (null, lhs),
			_ => (lhs, null)
		};

	private static (string? Player, string? LeadingClass) SplitQuotedSearchOwner(string lhs)
	{
		var closeIndex = lhs.IndexOf('"', 1);
		return closeIndex < 0
			? (lhs.TrimStart('"'), null)
			: (lhs[1..closeIndex], NonEmptyOrNull(lhs[(closeIndex + 1)..].TrimStart()));
	}

	private static string? NonEmptyOrNull(string text) => text.Length > 0 ? text : null;

	[SharpCommand(Name = "@WHEREIS", Output = CommandOutput.Value, Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 1, MaxArgs = 1, ParameterNames = ["name"])]
	public async ValueTask<Option<CallState>> WhereIs(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		if (args.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WhereIsMustSpecifyPlayer), executor);
			return new CallState(ErrorMessages.Returns.NoPlayerSpecified);
		}

		var targetName = args["0"].Message.ToPlainText();

		var maybeTarget = await LocateService.LocateAndNotifyIfInvalid(
			parser,
			executor,
			executor,
			targetName,
			LocateFlags.All);

		if (maybeTarget is not AnySharpObject target)
		{
			return new CallState(ErrorMessages.Returns.NotFound);
		}

		if (target is not SharpPlayer targetPlayer)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WhereIsCanOnlyLocatePlayers), executor);
			return new CallState(ErrorMessages.Returns.NotAPlayer);
		}

		var targetObject = target.Object();

		// Unfind(x) is has_flag_by_name(x, "UNFINDABLE", NOTYPE) (hdrs/dbdefs.h:160): by name or alias,
		// never by letter.
		var isUnfindable = await targetObject.HasFlag("UNFINDABLE");

		if (isUnfindable)
		{
			await NotifyService.Notify(target,
				$"{executor.Object().Name} tried to locate you, but was unable to.", executor);
			await NotifyService.Notify(executor,
				$"{targetObject.Name} is UNFINDABLE.", executor);
			return new CallState(ErrorMessages.Returns.Unfindable);
		}

		var targetLocation = await targetPlayer.Location.WithCancellation(CancellationToken.None);
		var locationName = targetLocation.Object().Name;

		await NotifyService.Notify(target,
			$"{executor.Object().Name} has just located your position.", executor);

		await NotifyService.Notify(executor,
			$"{targetObject.Name} is in {locationName}.", executor);

		return new CallState($"#{targetLocation.Object().DBRef.Number}");
	}

	[SharpCommand(Name = "@DECOMPILE", Output = CommandOutput.Value, Switches = ["DB", "NAME", "PREFIX", "TF", "FLAGS", "ATTRIBS", "SKIPDEFAULTS"],
		Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0, ParameterNames = ["object", "name"])]
	public async ValueTask<Option<CallState>> Decompile(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var objectSpec = args.Count == 0 ? null : args["0"].Message.ToPlainText();
		if (string.IsNullOrEmpty(objectSpec))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.YouMustSpecifyObjectToDecompile), executor);
			return new CallState(ErrorMessages.Returns.NoObjectSpecified);
		}

		var isTf = switches.Contains("TF");
		var prefix = await DecompilePrefixAsync(executor, args, isTf);

		var (objectName, attributePattern) = HelperFunctions.SplitDbRefAndOptionalAttr(objectSpec) is { Object: var name, Attribute: var pattern }
			? (name, pattern)
			: (objectSpec, null);

		if (await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, objectName, LocateFlags.All)
				is not AnySharpObject target)
		{
			return new None();
		}

		if (!await PermissionService.CanExamine(executor, target))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var obj = target.Object();
		var hasPattern = !string.IsNullOrEmpty(attributePattern);
		// NAME is the default: the lines name the object unless /DB asks for its dbref.
		var useDbRef = switches.Contains("DB");
		// A pattern decompiles only the attributes it matches.
		var showFlags = !hasPattern && (switches.Contains("FLAGS") || !switches.Contains("ATTRIBS"));
		var showAttribs = hasPattern || switches.Contains("ATTRIBS") || !switches.Contains("FLAGS");
		var decompile = new DecompileRun(executor, obj, useDbRef ? $"#{obj.DBRef.Number}" : obj.Name, prefix,
			switches.Contains("SKIPDEFAULTS"), isTf);

		if (showFlags)
		{
			await DecompileObjectAsync(decompile);
		}

		if (showAttribs)
		{
			await DecompileAttributesAsync(decompile, target, attributePattern);
		}

		foreach (var output in decompile.Outputs)
		{
			await NotifyService.Notify(executor, output, executor);
		}

		// The commands are what it shows; its output is the object, as for look and examine.
		return new CallState(obj.DBRef.ToString());
	}

	/// <summary>
	/// One @decompile: the object, how its lines name it and what they start with, and the lines written
	/// so far.
	/// </summary>
	private sealed record DecompileRun(AnySharpObject Executor, SharpObject Object, string ObjectRef, string Prefix,
		bool SkipDefaults, bool IsTf)
	{
		public List<string> Outputs { get; } = [];

		public void Add(string command) => Outputs.Add($"{Prefix}{command}");
	}

	/// <summary>The text each line starts with: /TF reads the executor's TFPREFIX, otherwise the second argument.</summary>
	private async ValueTask<string> DecompilePrefixAsync(AnySharpObject executor, IReadOnlyDictionary<string, CallState> args, bool isTf)
	{
		if (!isTf)
		{
			return args.Count >= 2 ? args["1"].Message.ToPlainText() : "";
		}

		var tfPrefixAttr = await AttributeService.GetAttributeAsync(executor, executor, "TFPREFIX",
			IAttributeService.AttributeMode.Read, false);

		return tfPrefixAttr is SharpAttribute[] attr
			? attr.Last().Value.ToPlainText()
			: "FugueEdit > ";
	}

	/// <summary>The object itself: how to create it, its flags, roles and powers, locks, and parent.</summary>
	private async ValueTask DecompileObjectAsync(DecompileRun run)
	{
		var obj = run.Object;
		run.Add(obj.Type.ToUpperInvariant() switch
		{
			"ROOM" => $"@dig {run.ObjectRef}",
			"EXIT" => $"@open {run.ObjectRef}",
			"THING" => $"@create {run.ObjectRef}",
			"PLAYER" => $"@pcreate {run.ObjectRef}",
			_ => $"@create {run.ObjectRef}"
		});

		await foreach (var flag in obj.Flags.Value.Where(flag => !run.SkipDefaults || !IsDefaultFlag(obj.Type, flag.Name)))
		{
			run.Add($"@set {run.ObjectRef}={flag.Name}");
		}

		await DecompileGrantsAsync(run);

		await foreach (var power in obj.Powers.Value)
		{
			run.Add($"@power {run.ObjectRef}={power.Name}");
		}

		foreach (var (lockName, lockData) in obj.Locks.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
		{
			await DecompileLockAsync(run, lockName, lockData);
		}

		if (await obj.Parent.WithCancellation(CancellationToken.None) is AnySharpObject parent)
		{
			var parentObj = parent.Object();
			run.Add($"@parent {run.ObjectRef}={parentObj.DBRef}");
		}
	}

	/// <summary>
	/// What is set on the object itself, not what reaches it through an account: WIZARD and
	/// ROYALTY and the Guest and Builder powers are its roles, the other powers its overrides.
	/// </summary>
	private static async ValueTask DecompileGrantsAsync(DecompileRun run)
	{
		var objectRef = run.ObjectRef;
		var grants = await run.Object.Grants.WithCancellation(ExecutionBudget.CurrentToken);
		var ownRoles = grants.Roles.Where(held => held.Source == RoleSource.Object).Select(held => held.Role.Slug).ToArray();
		foreach (var slug in ownRoles)
		{
			run.Add(RoleFlags.ForRole(slug) is { } roleFlag ? $"@set {objectRef}={roleFlag.Name}"
				: GamePowers.ForRole(slug) is { } rolePower ? $"@power {objectRef}={rolePower.Name}"
				: $"@role/assign {objectRef}={slug}");
		}

		foreach (var (scope, state) in grants.Context.ObjectOverrides.OrderBy(o => o.Key, StringComparer.Ordinal))
		{
			run.Add(state == PermissionState.Allow && GamePowers.ForScope(scope) is { } power
				? $"@power {objectRef}={power.Name}"
				: $"@role/{(state == PermissionState.Allow ? "allow" : "deny")} {objectRef}={scope}");
		}
	}

	/// <summary>One lock as the @lock that sets it, then an @lset for each privilege that differs from the lock's defaults.</summary>
	private async ValueTask DecompileLockAsync(DecompileRun run, string lockName, SharpLockData lockData)
	{
		var objectRef = run.ObjectRef;
		if (!BooleanExpressionParser.IsBound(lockData.LockString))
		{
			run.Add($"@@ Invalid {lockName} lock omitted; replace it explicitly before decompiling.");
			return;
		}
		var expression = await BooleanExpressionParser.RenderAsync(lockData.LockString, run.Executor, LockRenderMode.Decompile, ExecutionBudget.CurrentToken);
		var standard = LockService.SystemLocks.TryGetValue(lockName, out var defaults);
		var switchName = standard ? LockNames.Display(lockName) : $"user:{lockName}";
		run.Add($"@lock/{switchName} {objectRef}={expression}");
		foreach (var (flagName, (_, flag)) in LockService.LockPrivileges)
		{
			var set = lockData.Flags.HasFlag(flag);
			if (set && (!run.SkipDefaults || !defaults.HasFlag(flag))) run.Add($"@lset {objectRef}/{lockName}={flagName}");
			else if (!set && defaults.HasFlag(flag)) run.Add($"@lset {objectRef}/{lockName}=!{flagName}");
		}
	}

	private async ValueTask DecompileAttributesAsync(DecompileRun run, AnySharpObject target, string? attributePattern)
	{
		// Penn's do_decompile reads "**" when no pattern is given: the whole tree, not only its roots.
		var atrs = await AttributeService.GetAttributePatternAsync(
			run.Executor,
			target,
			string.IsNullOrEmpty(attributePattern) ? "**" : attributePattern,
			false, // don't check parents for decompile
			IAttributeService.AttributePatternMode.Wildcard);

		if (atrs is not SharpAttribute[] decompiledAttributes)
		{
			return;
		}

		const string VeiledFlagName = "VEILED";
		foreach (var attr in decompiledAttributes.Where(attr => !attr.Flags.Any(f => f.Name.Equals(VeiledFlagName, StringComparison.OrdinalIgnoreCase))))
		{
			await DecompileAttributeAsync(run, attr);
		}
	}

	private async ValueTask DecompileAttributeAsync(DecompileRun run, SharpAttribute attr)
	{
		var objectRef = run.ObjectRef;

		// Penn's AL_NAME: the full tree path, so FUN`FOOTER`DISPLAY and not DISPLAY.
		var attrName = attr.LongName;
		run.Add(attr.Value.Runs.Length > 0
			// Markup only survives as the softcode that makes it, which @set evaluates.
			? $"@set {objectRef}={attrName}:{SoftcodeDecomposer.Decompose(attr.Value)}"
			: $"&{attrName} {objectRef}={attr.Value.ToPlainText()}");

		// Branch is Penn's AF_ROOT: structure the tree rebuilds itself, never decompiled.
		// The rest go on one @set line, as privs_to_string writes them.
		var attrFlags = attr.Flags.Where(flag => !flag.Name.Equals("branch", StringComparison.OrdinalIgnoreCase)).ToArray();
		if (!run.IsTf && attrFlags.Length > 0
			&& (!run.SkipDefaults || !await AreDefaultAttrFlagsAsync(attrName, attrFlags)))
		{
			run.Add($"@set {objectRef}/{attrName}={string.Join(" ", attrFlags.Select(flag => flag.Name))}");
		}
	}

	/// <summary>
	/// Checks if a flag is a default flag for the object type
	/// </summary>
	private bool IsDefaultFlag(string type, string flagName)
	{
		var defaultFlags = type.ToUpperInvariant() switch
		{
			"PLAYER" => Configuration?.CurrentValue.Flag.PlayerFlags ?? [],
			"ROOM" => Configuration?.CurrentValue.Flag.RoomFlags ?? [],
			"THING" => Configuration?.CurrentValue.Flag.ThingFlags ?? [],
			"EXIT" => Configuration?.CurrentValue.Flag.ExitFlags ?? [],
			_ => Array.Empty<string>()
		};

		return defaultFlags.Any(f => f.Equals(flagName, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Checks if attribute flags are the default for that attribute
	/// </summary>
	private async ValueTask<bool> AreDefaultAttrFlagsAsync(string attrName, IEnumerable<SharpAttributeFlag> flags)
	{
		var entry = await Mediator.Send(new GetAttributeEntryQuery(attrName.ToUpper()));

		if (entry == null)
		{
			// No entry means no custom defaults; empty flags are considered default
			return !flags.Any();
		}

		var currentFlagNames = flags.Select(f => f.Name.ToUpper()).OrderBy(n => n);
		var defaultFlagNames = entry.DefaultFlags.Select(f => f.ToUpper()).OrderBy(n => n);

		return currentFlagNames.SequenceEqual(defaultFlagNames);
	}

	[SharpCommand(Name = "@ENTRANCES", Output = CommandOutput.Value, Switches = ["EXITS", "THINGS", "PLAYERS", "ROOMS"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 3, ParameterNames = ["object", "flags"])]
	public async ValueTask<Option<CallState>> Entrances(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;

		var targetName = args.TryGetValue("0", out var arg0) ? arg0.Message.ToPlainText() : null;
		if (string.IsNullOrEmpty(targetName))
		{
			return await ReportEntrancesAsync(executor, (await executor.Where()).WithExitOption(), args, switches);
		}

		return await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, targetName, LocateFlags.All) switch
		{
			AnySharpObject located => await ReportEntrancesAsync(executor, located, args, switches),
			_ => new CallState(ErrorMessages.Returns.NotFound)
		};
	}

	private static readonly (string Switch, string Label)[] EntranceTypeSwitches =
		[("EXITS", "exits"), ("THINGS", "things"), ("PLAYERS", "players"), ("ROOMS", "rooms")];

	private static int? EntrancesBound(IReadOnlyDictionary<string, CallState> args, string key)
		=> args.TryGetValue(key, out var arg) && int.TryParse(arg.Message.ToPlainText(), out var bound) ? bound : null;

	private async ValueTask<CallState> ReportEntrancesAsync(AnySharpObject executor, AnySharpObject targetObject,
		IReadOnlyDictionary<string, CallState> args, IEnumerable<string> switches)
	{
		var beginDbref = EntrancesBound(args, "1");
		var endDbref = EntrancesBound(args, "2");
		var hasRange = beginDbref.HasValue || endDbref.HasValue;

		var targetObj = targetObject.Object();
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EntrancesToFormat), executor, targetObj.Name);

		var filterTypes = EntranceTypeSwitches.Where(type => switches.Contains(type.Switch)).Select(type => type.Label).ToList();

		if (filterTypes.Count > 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EntrancesFilteringForFormat), executor, string.Join(", ", filterTypes));
		}

		if (hasRange)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EntrancesRangeFormat), executor, beginDbref ?? 0, endDbref?.ToString() ?? "end");
		}

		// GetEntrancesQuery only returns exits, so a type filter without exits needs no read at all.
		var entrances = filterTypes.Count > 0 && !filterTypes.Contains("exits")
			? []
			: await Mediator.CreateStream(new GetEntrancesQuery(targetObj.DBRef)).ToListAsync();

		if (hasRange)
		{
			entrances = entrances.Where(e =>
			{
				var key = e.Object.Key;
				return (!beginDbref.HasValue || key >= beginDbref.Value) &&
							 (!endDbref.HasValue || key <= endDbref.Value);
			}).ToList();
		}

		if (entrances.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EntrancesZeroFound), executor);
		}
		else
		{
			foreach (var entrance in entrances)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EntrancesObjectEntryFormat), executor, entrance.Object.Key, entrance.Object.Name);
			}
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.EntrancesCountFormat), executor, entrances.Count);
		}

		// The list entrances() gives; the count is only shown.
		return new CallState(string.Join(" ", entrances.Select(entrance => entrance.Object.DBRef.ToString())));
	}

	[SharpCommand(Name = "@GREP", Output = CommandOutput.Value, Switches = ["LIST", "PRINT", "ILIST", "IPRINT", "REGEXP", "WILD", "NOCASE", "PARENT"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 2, MaxArgs = 2, ParameterNames = ["object", "pattern"])]
	public async ValueTask<Option<CallState>> Grep(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator);

		if (!args.TryGetValue("0", out var objAttrArg) || !args.TryGetValue("1", out var patternArg))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.GrepInvalidArguments), executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		var objAttrText = objAttrArg.Message.ToPlainText();
		var pattern = patternArg.Message.ToPlainText();
		if (HelperFunctions.SplitDbRefAndOptionalAttr(objAttrText) is not { Object: var dbref, Attribute: var maybeAttributePattern })
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontSeeThatHere), executor);
			return new CallState(ErrorMessages.Returns.InvalidObject);
		}

		var attributePattern = string.IsNullOrEmpty(maybeAttributePattern) ? "*" : maybeAttributePattern;

		return await LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
			executor,
			executor,
			dbref,
			LocateFlags.All) switch
		{
			AnySharpObject targetObject => await GrepAsync(parser, executor, targetObject, switches, pattern, attributePattern),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> GrepAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject targetObject, IEnumerable<string> switches, string pattern, string attributePattern)
	{
		var checkParents = switches.Contains("PARENT");

		// PennMUSH treats the obj/attr half of @grep as a single wildcard pattern
		// (predicat.c:1610-1617 defaults it to "*", then hands it to atr_iter_get), and "**" is
		// not a separate matching mode - it is the attribute-name wildcard that is allowed to
		// cross "`" (wild.c:89-107, real_atr_wild). That distinction lives in the wildcard-to-regex
		// translation in the database providers, so every pattern here is Wildcard.
		return await AttributeService.LazilyGetAttributePatternAsync(
			executor,
			targetObject,
			attributePattern,
			checkParents,
			IAttributeService.AttributePatternMode.Wildcard) switch
		{
			IAsyncEnumerable<LazySharpAttribute> attributes => await GrepAttributesAsync(parser, executor, attributes, switches, pattern),
			Error<string> error => await GrepUnreadableAsync(executor, error.Value)
		};
	}

	private async ValueTask<Option<CallState>> GrepUnreadableAsync(AnySharpObject executor, string error)
	{
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.GrepErrorReadingAttributesFormat), executor, error);
		return new CallState($"#-1 {error}");
	}

	/// <summary>How @grep tests a value: as plain text, as a wildcard, or as a regular expression.</summary>
	private enum GrepMode { Substring, Wildcard, Regexp }

	/// <summary>What the @grep switches ask for: the test, its case, and whether matches are printed with their values.</summary>
	private readonly record struct GrepOptions(GrepMode Mode, bool NoCase, bool Print, string Pattern)
	{
		public StringComparison Comparison => NoCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

		/// <summary>Only a plain-text match is highlighted in a printed value.</summary>
		public bool Highlights => Mode == GrepMode.Substring;

		public static GrepOptions From(IEnumerable<string> switches, string pattern) => new(
			switches.Contains("REGEXP") ? GrepMode.Regexp
				: switches.Contains("WILD") ? GrepMode.Wildcard
				: GrepMode.Substring,
			switches.Contains("NOCASE") || switches.Contains("ILIST") || switches.Contains("IPRINT"),
			switches.Contains("PRINT") || switches.Contains("IPRINT"),
			pattern);
	}

	/// <summary>A pattern that could not be tested: the notification that says so and the value @grep returns.</summary>
	internal readonly record struct GrepFailure(string Notification, string Returns);

	/// <summary>
	/// Reports the attributes whose value matches <paramref name="pattern"/>, the way the switches ask.
	/// The attributes arrive with their values unread, already past the read gate; each body is read for
	/// its test and released unless it matched, so the scan holds only the matches it will report
	/// (<see cref="LazySharpAttributeExtensions.ReadValueOnceAsync"/>).
	/// </summary>
	private async ValueTask<Option<CallState>> GrepAttributesAsync(IMUSHCodeParser parser, AnySharpObject executor,
		IAsyncEnumerable<LazySharpAttribute> attributes, IEnumerable<string> switches, string pattern)
	{
		var options = GrepOptions.From(switches, pattern);
		var matchingAttributes = new List<(LazySharpAttribute Attribute, MString Value)>();
		var token = ExecutionBudget.CurrentToken;

		await foreach (var attr in attributes.WithCancellation(token))
		{
			ExecutionBudget.Current?.ThrowIfExceeded();
			var value = await attr.ReadValueOnceAsync(token);

			switch (TestGrepValue(options, value.ToPlainText()))
			{
				case GrepFailure failure:
					await NotifyService.NotifyLocalized(executor, failure.Notification, executor, pattern);
					return new CallState(failure.Returns);
				case true:
					matchingAttributes.Add((attr, value));
					break;
			}
		}

		if (matchingAttributes.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.GrepNoMatchingAttributesFound), executor);
			return new CallState(string.Empty);
		}

		if (options.Print)
		{
			await PrintGrepMatchesAsync(parser, executor, matchingAttributes, options);
		}
		else
		{
			var attrNames = string.Join(" ", matchingAttributes.Select(a => a.Attribute.LongName));
			await NotifyService.Notify(executor, attrNames, executor);
		}

		// The attribute list grep() gives, whichever way the matches were shown.
		return new CallState(string.Join(" ", matchingAttributes.Select(a => a.Attribute.LongName)));
	}

	private static readonly (GrepFailure TimedOut, GrepFailure Invalid) GrepRegexpFailures = (
		new GrepFailure(nameof(ErrorMessages.Notifications.GrepRegexpTimedOutFormat), ErrorMessages.Returns.RegexpTimeout),
		new GrepFailure(nameof(ErrorMessages.Notifications.GrepInvalidRegexpFormat), ErrorMessages.Returns.InvalidRegexp));

	private static readonly (GrepFailure TimedOut, GrepFailure Invalid) GrepWildcardFailures = (
		new GrepFailure(nameof(ErrorMessages.Notifications.GrepWildcardTimedOutFormat), ErrorMessages.Returns.PatternTimeout),
		new GrepFailure(nameof(ErrorMessages.Notifications.GrepInvalidWildcardFormat), ErrorMessages.Returns.InvalidPattern));

	/// <summary>Whether <paramref name="value"/> matches, or why the pattern could not be tested against it.</summary>
	private static GrepTest TestGrepValue(GrepOptions options, string value) => options.Mode switch
	{
		GrepMode.Regexp => TestGrepPattern(GrepRegexpFailures,
			() => Regex.IsMatch(value, options.Pattern, options.NoCase ? RegexOptions.IgnoreCase : RegexOptions.None, TimeSpan.FromSeconds(1))),
		// grep_util passes cs = ((flags & GREP_NOCASE) == 0), so the wildcard grep is
		// case-SENSITIVE unless this is the "i" variant — unlike every other wildcard in
		// the game, which goes through quick_wild and its cs = 0.
		GrepMode.Wildcard => TestGrepPattern(GrepWildcardFailures,
			() => SoftcodeRegex.Wildcard(options.Pattern, caseSensitive: !options.NoCase).IsMatch(value)),
		_ => value.Contains(options.Pattern, options.Comparison)
	};

	private static GrepTest TestGrepPattern((GrepFailure TimedOut, GrepFailure Invalid) failures, Func<bool> test)
	{
		try
		{
			return test();
		}
		catch (RegexMatchTimeoutException)
		{
			return failures.TimedOut;
		}
		// An invalid pattern: the Regex constructor's RegexParseException is an ArgumentException.
		catch (ArgumentException)
		{
			return failures.Invalid;
		}
	}

	private async ValueTask PrintGrepMatchesAsync(IMUSHCodeParser parser, AnySharpObject executor,
		List<(LazySharpAttribute Attribute, MString Value)> matchingAttributes, GrepOptions options)
	{
		// Lazily computed: only a flagged attribute needs it, and most @grep/PRINT calls have none.
		int? width = null;
		async ValueTask<int> Width() => width ??= await ExecutorFormatWidthAsync(executor);

		foreach (var (attr, value) in matchingAttributes)
		{
			var displayValue = attr.SyntaxParseType() switch
			{
				// Byte-identical to the pre-formatting behavior: nothing in this branch may
				// change when SyntaxParseType() is null, since that is the regression contract
				// covering all existing traffic.
				null => options.Highlights ? HighlightGrepMatch(value, options) : value,
				{ } parseType => await FormattedGrepValueAsync(parser, value, parseType, options, Width)
			};

			await NotifyService.Notify(executor,
				MarkupText.Concat(MarkupText.Plain($"{attr.LongName}: ").Hilight(), displayValue), executor);
		}
	}

	/// <summary>Highlights the first match in an unflagged value, rebuilt from plain-text spans.</summary>
	private static MString HighlightGrepMatch(MString value, GrepOptions options)
	{
		// Highlight the matching parts using Span to avoid allocations
		var plainValue = value.ToPlainText();
		var pattern = options.Pattern;
		var index = plainValue.IndexOf(pattern, options.Comparison);

		if (index < 0)
		{
			return value;
		}

		var valueSpan = plainValue.AsSpan();
		var before = valueSpan.Slice(0, index).ToString();
		var match = valueSpan.Slice(index, pattern.Length).ToString();
		var after = valueSpan.Slice(index + pattern.Length).ToString();

		return MarkupText.Concat(MarkupText.Concat(MarkupText.Plain(before), MarkupText.Plain(match).Hilight()), MarkupText.Plain(after));
	}

	/// <summary>
	/// The attribute carries a syntax flag: render the formatted, wrapped block instead
	/// of the raw value. Empty values are left alone (mirrors @examine) rather than run
	/// through the formatter, since an empty funsyntax/cmdsyntax body is itself a parse
	/// error and would otherwise surface a stray parser-failure summary in place of blank.
	/// </summary>
	private static async ValueTask<MString> FormattedGrepValueAsync(IMUSHCodeParser parser, MString value, ParseType parseType,
		GrepOptions options, Func<ValueTask<int>> width)
	{
		if (value.Length == 0)
		{
			return options.Highlights ? HighlightFormattedGrepMatch(value, 0, options) : value;
		}

		var tokens = parser.Tokenize(value);
		var semanticTokens = parser.GetSemanticTokens(value, parseType);
		var errors = SoftcodeSource.Validate(parser, value, parseType);
		// codeLength is the plain-text length of the code portion of `formatted` — everything but the
		// error summary the formatter appends beneath it.
		var formatted = SoftcodeFormatter.Format(value, tokens, semanticTokens, errors, await width(), parser,
			parseType, out var codeLength);

		return options.Highlights ? HighlightFormattedGrepMatch(formatted, codeLength, options) : formatted;
	}

	/// <summary>
	/// Same highlight as the unflagged path, but sliced from the formatted block via
	/// MarkupText.Substring (rather than rebuilt from plain-text spans) so the formatter's
	/// own syntax colouring survives around the highlighted match.
	/// </summary>
	/// <remarks>
	/// Bounded by <paramref name="codeLength"/>: the attribute matched on its *value*, so the match is in the
	/// code. Searching the whole block would let a pattern that occurs only in the appended
	/// "#-1 PARSER FAILURE ..." summary highlight as though it were the match that put this
	/// attribute in the result set.
	/// </remarks>
	private static MString HighlightFormattedGrepMatch(MString formatted, int codeLength, GrepOptions options)
	{
		var pattern = options.Pattern;
		var plainFormatted = formatted.ToPlainText();
		var index = plainFormatted.IndexOf(pattern, 0, codeLength, options.Comparison);

		if (index < 0)
		{
			return formatted;
		}

		var before = formatted.Substring(0, index);
		var match = formatted.Substring(index, pattern.Length).Hilight();
		var after = formatted.Substring(index + pattern.Length, plainFormatted.Length - index - pattern.Length);

		return MarkupText.Concat(MarkupText.Concat(before, match), after);
	}

	[SharpCommand(Name = "@SWEEP", Switches = ["CONNECTED", "HERE", "INVENTORY", "EXITS"], Behavior = CB.Default,
		MinArgs = 0, MaxArgs = 0, ParameterNames = ["flags"])]
	public async ValueTask<Option<CallState>> Sweep(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var switches = parser.CurrentState.Switches;
		var connectFlag = switches.Contains("CONNECTED");
		var hereFlag = switches.Contains("HERE");
		var inventoryFlag = switches.Contains("INVENTORY");
		var exitsFlag = switches.Contains("EXITS");

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var perceive = await ObserveRealityAsync(parser, executor);
		var location = await executor.Where();
		var locationOwner = await location.Object().Owner.WithCancellation(CancellationToken.None);

		if (!inventoryFlag && !exitsFlag)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepListeningInRoom), executor);
			await SweepLocationAsync(executor, location, locationOwner, connectFlag);
			await SweepContentsAsync(executor, location, perceive, connectFlag);
		}

		if (!connectFlag && !inventoryFlag && location.IsRoom && exitsFlag)
		{
			await SweepExitsAsync(executor, location, perceive);
		}

		if (!hereFlag && !exitsFlag && inventoryFlag)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepListeningInInventory), executor);
			// An exit carries nothing, so its inventory sweep lists nothing.
			if (executor.AsOptionalContainer is AnySharpContainer carrier)
			{
				await SweepContentsAsync(executor, carrier, perceive, connectFlag);
			}
		}

		return CallState.Empty;
	}

	/// <summary>@sweep's line for the executor's location: who is connected there, or what it hears, runs and broadcasts.</summary>
	private async ValueTask SweepLocationAsync(AnySharpObject executor, AnySharpContainer location, SharpPlayer locationOwner,
		bool connectFlag)
	{
		var locationObj = location.Object();
		var locationAnyObject = location.WithExitOption();

		if (connectFlag)
		{
			if (!await IsConnectedOrPuppetConnectedAsync(locationAnyObject)) return;

			if (location.IsPlayer)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectIsListeningFormat), executor, locationObj.Name);
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectOwnerIsListeningFormat), executor, locationObj.Name, locationOwner.Object.Name);
			}

			return;
		}

		await SweepSpeechAsync(executor, locationAnyObject,
			nameof(ErrorMessages.Notifications.SweepRoomSpeechConnectedFormat), nameof(ErrorMessages.Notifications.SweepRoomSpeechFormat));

		if (await locationAnyObject.HasActiveCommands())
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepRoomCommandsFormat), executor, locationObj.Name);
		if (await locationAnyObject.IsAudible())
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepRoomBroadcastingFormat), executor, locationObj.Name);
	}

	/// <summary>@sweep's lines for what <paramref name="container"/> holds that the executor perceives.</summary>
	private async ValueTask SweepContentsAsync(AnySharpObject executor, AnySharpContainer container,
		Func<DBRef, CancellationToken, ValueTask<bool>> perceive, bool connectFlag)
	{
		var contents = container.Content(Mediator)
			.Where((item, ct) => perceive(item.Object().DBRef, ct));
		await foreach (var obj in contents.WithCancellation(ExecutionBudget.CurrentToken))
		{
			await SweepObjectAsync(executor, obj.WithRoomOption(), connectFlag);
		}
	}

	private async ValueTask SweepObjectAsync(AnySharpObject executor, AnySharpObject obj, bool connectFlag)
	{
		var name = obj.Object().Name;

		if (connectFlag)
		{
			if (!await IsConnectedOrPuppetConnectedAsync(obj)) return;

			if (obj.IsPlayer)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectIsListeningFormat), executor, name);
			}
			else
			{
				// The owner is read only for the line that names it.
				var objOwner = await obj.Object().Owner.WithCancellation(CancellationToken.None);
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectOwnerIsListeningFormat), executor, name, objOwner.Object.Name);
			}

			return;
		}

		await SweepSpeechAsync(executor, obj,
			nameof(ErrorMessages.Notifications.SweepObjectSpeechConnectedFormat), nameof(ErrorMessages.Notifications.SweepObjectSpeechFormat));

		if (await obj.HasActiveCommands())
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepObjectCommandsFormat), executor, name);
	}

	/// <summary>The line for an object that hears speech, worded by whether it is connected.</summary>
	private async ValueTask SweepSpeechAsync(AnySharpObject executor, AnySharpObject obj, string connectedKey, string key)
	{
		if (!await obj.IsHearer(ConnectionService, AttributeService) && !await obj.IsListener()) return;

		var wording = await ConnectionService.IsConnected(obj) ? connectedKey : key;
		await NotifyService.NotifyLocalized(executor, wording, executor, obj.Object().Name);
	}

	private async ValueTask SweepExitsAsync(AnySharpObject executor, AnySharpContainer location,
		Func<DBRef, CancellationToken, ValueTask<bool>> perceive)
	{
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepListeningExits), executor);
		if (!await location.WithExitOption().IsAudible()) return;

		var exits = location.Content(Mediator).Where(x => x.IsExit)
			.Where((item, ct) => perceive(item.Object().DBRef, ct))
			.Where((exit, _) => exit.WithRoomOption().IsAudible());
		await foreach (var exit in exits.WithCancellation(ExecutionBudget.CurrentToken))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SweepExitBroadcastingFormat), executor, exit.Object().Name);
		}
	}

	private async ValueTask<bool> IsConnectedOrPuppetConnectedAsync(AnySharpObject obj)
	{
		if (await ConnectionService.IsConnected(obj)) return true;

		return await obj.IsPuppet()
					 && await ConnectionService.IsConnected(await obj.Object().Owner.WithCancellation(CancellationToken.None));
	}
}

/// <summary>An @grep test's outcome: whether the value matched, or why the pattern could not be tested.</summary>
internal union GrepTest(bool, Commands.GrepFailure);
