using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Client.Services;

/// <summary>Everything the palette ranks, gathered when it opens.</summary>
/// <param name="Destinations">The portal's pages the viewer may use.</param>
/// <param name="PageRows">The rows the current page adds (Edit, History, Run).</param>
/// <param name="Server">What <c>GET api/palette</c> answered, or null until it has (or when it cannot).</param>
/// <param name="Page">The current page's kind (<c>play</c>, <c>wiki</c>, <c>app:&lt;slug&gt;</c>), if it set one.</param>
/// <param name="TerminalConnected">Whether the Play terminal is connected, which input rows need.</param>
/// <param name="Words">The palette's own labels, localised.</param>
public sealed record PaletteSnapshot(
	IReadOnlyList<PaletteRow> Destinations,
	IReadOnlyList<PaletteRow> PageRows,
	PaletteResponse? Server,
	string? Page,
	bool TerminalConnected,
	PaletteWords? Words = null,
	IReadOnlyList<PaletteRow>? Configuration = null);

/// <summary>The labels of the rows the palette makes itself.</summary>
/// <param name="HelpOnFormat">"Help on {0}".</param>
public sealed record PaletteWords(string Leave = "Leave", string HelpOnFormat = "Help on {0}");

/// <summary>
/// Orders the palette's rows: the empty palette's groups, and the score of each row against typed text.
/// Plain C#, no Blazor, so it is tested on its own.
/// </summary>
public static class PaletteRanker
{
	/// <summary>Rows a group shows before its "more" row.</summary>
	public const int GroupLimit = 8;

	/// <summary>The most rows a search shows.</summary>
	public const int SearchLimit = 50;

	public const string InputGroup = "NavPaletteGroupInput";
	public const string NowGroup = "NavPaletteGroupNow";
	public const string PinnedGroup = "NavPaletteGroupPinned";
	public const string HereGroup = "NavPaletteGroupHere";
	public const string AlwaysGroup = "NavPaletteGroupAlways";
	public const string GoGroup = "NavPaletteGroupGo";

	/// <summary>
	/// The empty palette: the input session's rows, Now, Pinned, Here, Always, Go to. A row shows once, in
	/// the first group that has it; a group in <paramref name="expanded"/> shows every row.
	/// </summary>
	public static IReadOnlyList<PaletteGroup> Groups(PaletteSnapshot snapshot, IReadOnlySet<string>? expanded = null)
	{
		var entries = EntryRows(snapshot);
		var suggestions = Suggestions(snapshot);
		var pins = snapshot.Server?.Pins ?? [];
		var shown = new HashSet<string>(StringComparer.Ordinal);
		var groups = new List<PaletteGroup>();

		void Add(string name, IEnumerable<PaletteRow> rows)
		{
			var fresh = rows.Where(row => shown.Add(row.Id)).ToList();
			if (fresh.Count == 0) return;
			var limit = expanded?.Contains(name) == true ? int.MaxValue : GroupLimit;
			groups.Add(new PaletteGroup(name, [.. fresh.Take(limit)], Math.Max(0, fresh.Count - limit)));
		}

		Add(InputGroup, InputRows(snapshot, entries));
		Add(NowGroup, suggestions.Where(row => row.Urgent));
		Add(PinnedGroup, pins.Select(pin => Find(pin, snapshot, entries)).OfType<PaletteRow>());
		Add(HereGroup, [
			.. snapshot.PageRows,
			.. entries.Where(row => snapshot.Page is { } page && row.Entry?.Context == page),
			.. suggestions.Where(row => !row.Urgent)
		]);
		Add(AlwaysGroup, entries.Where(row => row.Entry?.Always == true));
		Add(GoGroup, snapshot.Destinations);
		return groups;
	}

	/// <summary>The rows matching <paramref name="query"/>, best first, at most <see cref="SearchLimit"/>.</summary>
	public static IReadOnlyList<PaletteRow> Search(PaletteSnapshot snapshot, string query)
	{
		var term = query.Trim();
		if (term.Length == 0) return [];

		var inputActive = InputActive(snapshot);
		var pins = (snapshot.Server?.Pins ?? []).ToHashSet(StringComparer.Ordinal);
		var raised = Suggestions(snapshot).ToDictionary(row => row.Id, StringComparer.Ordinal);
		var entries = EntryRows(snapshot).Where(row => row.Kind != PaletteRowKind.Input || inputActive);
		var declared = entries.Select(row => row.Key).ToHashSet(StringComparer.Ordinal);

		IEnumerable<PaletteRow> candidates =
		[
			.. inputActive ? InputRows(snapshot, []) : [],
			.. entries.Select(row => raised.GetValueOrDefault(row.Id) ?? row),
			.. snapshot.PageRows,
			.. snapshot.Configuration ?? [],
			.. snapshot.Destinations,
			.. BuiltinRows(snapshot).Where(row => !declared.Contains(row.Key)),
			.. entries.Select(row => row.Entry?.Help).OfType<string>().Select(topic => HelpRow(snapshot, topic))
		];

		return [.. candidates
			.DistinctBy(row => row.Id)
			.Select(row => (Row: row, Score: Score(row, term, snapshot.Page, pins, inputActive)))
			.Where(item => item.Score > 0)
			.OrderByDescending(item => item.Score)
			.ThenBy(item => item.Row.Order)
			.ThenBy(item => item.Row.Label, StringComparer.CurrentCultureIgnoreCase)
			.Take(SearchLimit)
			.Select(item => item.Row)];
	}

	/// <summary>
	/// A row's score against <paramref name="term"/>, or 0 when it does not match at all. Matching
	/// counts once (equals 100, starts 80, a word or the line's first word 60, letters in order 30), a
	/// keyword adds 20, and context adds the rest; a built-in sits just below a declared entry.
	/// </summary>
	public static double Score(PaletteRow row, string term, string? page, IReadOnlySet<string> pins, bool inputActive)
	{
		var label = row.Label;
		var match = label.Equals(term, StringComparison.CurrentCultureIgnoreCase) ? 100
			: label.StartsWith(term, StringComparison.CurrentCultureIgnoreCase) ? 80
			: Words(label).Any(word => word.StartsWith(term, StringComparison.CurrentCultureIgnoreCase))
				|| FirstWord(row.Line).StartsWith(term, StringComparison.CurrentCultureIgnoreCase) ? 60
			: InOrder(term, label) ? 30
			: 0;
		var description = row.Info?.Contains(term, StringComparison.CurrentCultureIgnoreCase) == true ? 10 : 0;
		var keyword = (row.Keywords?.Contains(term, StringComparison.CurrentCultureIgnoreCase) == true
			|| Words(row.Keywords ?? string.Empty).Any(word => word.StartsWith(term, StringComparison.CurrentCultureIgnoreCase))) ? 20 : 0;
		if (match + keyword + description == 0) return 0;

		var score = (double)(match + keyword + description);
		if (pins.Contains(row.Id)) score += 50;
		if (row.Kind == PaletteRowKind.Input && inputActive) score += 40;
		if (page is not null && row.Entry?.Context == page) score += 30;
		if (row.Entry?.Scope is PaletteScope.Self or PaletteScope.Inventory or PaletteScope.Room) score += 20;
		score += 60 * row.Weight;
		if (row.Urgent) score += 90;
		if (row.Entry?.Always == true) score += 10;
		if (row.Kind is PaletteRowKind.Builtin or PaletteRowKind.Help) score -= 0.5;
		return score;
	}

	/// <summary>Each server entry as a row, nearest first, one per line.</summary>
	public static IReadOnlyList<PaletteRow> EntryRows(PaletteSnapshot snapshot)
	{
		var entries = snapshot.Server?.Entries ?? [];
		var labels = entries.GroupBy(entry => entry.Label, StringComparer.CurrentCultureIgnoreCase)
			.Where(group => group.Select(entry => PaletteLines.Key(entry.Line)).Distinct().Count() > 1)
			.Select(group => group.Key)
			.ToHashSet(StringComparer.CurrentCultureIgnoreCase);

		return [.. entries
			.DistinctBy(entry => (entry.Context == PaletteSuggestions.InputContext, PaletteLines.Key(entry.Line)))
			.Select(entry => FromEntry(entry, labels.Contains(entry.Label)))];
	}

	/// <summary>The entries the context stack raises, as rows carrying the filled line, info and weight.</summary>
	public static IReadOnlyList<PaletteRow> Suggestions(PaletteSnapshot snapshot)
		=> snapshot.Server is { } server
			? [.. PaletteSuggestions.Suggest(server.Entries, server.Stack).Select(suggestion => FromEntry(suggestion.Entry, false) with
			{
				Line = suggestion.Line,
				Info = suggestion.Info,
				Weight = suggestion.Weight,
				Urgent = suggestion.Urgent
			})]
			: [];

	private static PaletteRow FromEntry(PaletteEntryView entry, bool hintSource)
		=> new(
			entry.Id,
			PaletteLines.Key(entry.Line),
			entry.Context == PaletteSuggestions.InputContext ? PaletteRowKind.Input
				: entry.Action == PaletteAction.Insert ? PaletteRowKind.Insert
				: PaletteRowKind.Run,
			entry.Label,
			AppIcons.Outline(entry.Icon, MudBlazor.Icons.Material.Outlined.ChevronRight),
			entry.Info,
			hintSource ? entry.System.StartsWith('#') ? entry.ObjectName : entry.System : entry.Group,
			Line: entry.Line,
			Order: (int)entry.Scope,
			Entry: entry,
			Keywords: entry.Keywords);

	private static bool InputActive(PaletteSnapshot snapshot)
		=> snapshot.TerminalConnected && snapshot.Server?.Input is not null;

	/// <summary>The input session's entries and, when its exit is a plain word, a row that leaves it.</summary>
	private static IEnumerable<PaletteRow> InputRows(PaletteSnapshot snapshot, IReadOnlyList<PaletteRow> entries)
	{
		if (!InputActive(snapshot)) yield break;
		foreach (var row in (entries.Count > 0 ? entries : EntryRows(snapshot)).Where(row => row.Kind == PaletteRowKind.Input))
			yield return row;
		if (snapshot.Server!.Input!.Exit is { } exit)
			yield return new PaletteRow($"input:{exit}", $"input:{exit}", PaletteRowKind.Input, (snapshot.Words ?? new()).Leave,
				MudBlazor.Icons.Material.Outlined.Logout, Hint: exit, Line: exit);
	}

	private static IEnumerable<PaletteRow> BuiltinRows(PaletteSnapshot snapshot)
	{
		foreach (var name in snapshot.Server?.Builtins ?? [])
		{
			var command = name.ToLowerInvariant();
			yield return new PaletteRow($"cmd:{command}", PaletteLines.Key(command), PaletteRowKind.Builtin, command,
				MudBlazor.Icons.Material.Outlined.Terminal, Line: command + " ", Order: PaletteRow.BuiltinOrder);
			yield return HelpRow(snapshot, command);
		}
	}

	/// <summary>A row that opens the help topic <paramref name="topic"/>.</summary>
	private static PaletteRow HelpRow(PaletteSnapshot snapshot, string topic)
	{
		var name = topic.ToLowerInvariant();
		return new PaletteRow($"help:{name}", $"help:{name}", PaletteRowKind.Help,
			string.Format((snapshot.Words ?? new()).HelpOnFormat, name), MudBlazor.Icons.Material.Outlined.HelpOutline,
			Href: $"/help/{Uri.EscapeDataString(name)}", Order: PaletteRow.BuiltinOrder);
	}

	/// <summary>The row a pin names, when it is within reach now.</summary>
	private static PaletteRow? Find(string pin, PaletteSnapshot snapshot, IReadOnlyList<PaletteRow> entries)
		=> entries.FirstOrDefault(row => row.Id == pin)
			?? snapshot.Destinations.FirstOrDefault(row => row.Id == pin)
			?? snapshot.Configuration?.FirstOrDefault(row => row.Id == pin)
			?? snapshot.PageRows.FirstOrDefault(row => row.Id == pin)
			?? (pin.StartsWith("cmd:", StringComparison.Ordinal)
				? BuiltinRows(snapshot).FirstOrDefault(row => row.Id == pin)
				: null);

	private static string[] Words(string text) => text.Split([' ', '-', '/', '_'], StringSplitOptions.RemoveEmptyEntries);

	private static string FirstWord(string? line)
		=> line is null ? string.Empty : line.TrimStart().Split([' ', '/', '='], 2)[0];

	/// <summary>Every letter of <paramref name="term"/> appears in <paramref name="text"/>, in order.</summary>
	private static bool InOrder(string term, string text)
	{
		var at = 0;
		foreach (var c in term)
		{
			if (char.IsWhiteSpace(c)) continue;
			at = text.IndexOf(c.ToString(), at, StringComparison.CurrentCultureIgnoreCase);
			if (at < 0) return false;
			at++;
		}

		return true;
	}
}
