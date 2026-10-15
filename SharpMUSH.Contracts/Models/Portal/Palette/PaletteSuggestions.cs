namespace SharpMUSH.Library.Models.Portal;

/// <summary>An entry the player's context puts forward, with its line filled from that context.</summary>
/// <param name="Line">
/// The line with every slot a raising context names filled in. When <paramref name="Ready"/>, it is the
/// line to send; otherwise the open slots are still written <c>&lt;name&gt;</c>, a literal <c>&lt;</c> as <c>\&lt;</c>.
/// </param>
/// <param name="Ready">True when no slot is left to ask for.</param>
/// <param name="Urgent">True when the entry is urgent and every context it needs is on the stack.</param>
/// <param name="Contexts">The contexts that raised it, newest first.</param>
/// <param name="Weight">The lowest weight among those contexts.</param>
/// <param name="Info">The newest raising context's info, else the entry's own.</param>
public sealed record PaletteSuggestion(
	PaletteEntryView Entry,
	string Line,
	bool Ready,
	bool Urgent,
	IReadOnlyList<string> Contexts,
	double Weight,
	string? Info);

/// <summary>
/// Which entries the context stack raises, in the order the empty palette's Now and Here groups show
/// them and <c>context()</c> returns them. Shared by the server and the portal so the two never disagree.
/// </summary>
public static class PaletteSuggestions
{
	/// <summary>The context entries for the <c>@input</c> session carry; they are never raised by the stack.</summary>
	public const string InputContext = "input";

	/// <summary>
	/// The entries <paramref name="stack"/> raises: urgent ones first, then by weight, newest context
	/// first; ties keep the nearer scope, then the entry's order, then its label. Of entries with the same
	/// line only the first in <paramref name="entries"/> (the nearest) counts.
	/// </summary>
	public static IReadOnlyList<PaletteSuggestion> Suggest(IReadOnlyList<PaletteEntryView> entries,
		IReadOnlyList<PaletteContextView> stack)
	{
		var byName = new Dictionary<string, (PaletteContextView Context, int Position)>(StringComparer.OrdinalIgnoreCase);
		for (var i = 0; i < stack.Count; i++) byName.TryAdd(stack[i].Name, (stack[i], i));

		var seen = new HashSet<string>();
		var raised = new List<PaletteSuggestion>();
		foreach (var entry in entries)
		{
			if (string.Equals(entry.Context, InputContext, StringComparison.OrdinalIgnoreCase)) continue;
			if (!seen.Add(PaletteLines.Key(entry.Line))) continue;
			if (Raise(entry, byName) is { } suggestion) raised.Add(suggestion);
		}

		return [.. raised
			.Select((suggestion, index) => (suggestion, index))
			.OrderByDescending(item => item.suggestion.Urgent)
			.ThenByDescending(item => item.suggestion.Weight)
			.ThenBy(item => item.suggestion.Entry.Scope)
			.ThenBy(item => item.suggestion.Entry.Order)
			.ThenBy(item => item.suggestion.Entry.Label, StringComparer.OrdinalIgnoreCase)
			.ThenBy(item => item.index)
			.Select(item => item.suggestion)];
	}

	/// <summary>
	/// The suggestion <paramref name="entry"/> makes, or null when its contexts are not all on the stack.
	/// With no <c>when</c>, its own system's context raises it.
	/// </summary>
	private static PaletteSuggestion? Raise(PaletteEntryView entry,
		Dictionary<string, (PaletteContextView Context, int Position)> stack)
	{
		IReadOnlyList<string> names = entry.When is { Count: > 0 } when ? when : [entry.System];
		var raising = new List<(PaletteContextView Context, int Position)>(names.Count);
		foreach (var name in names)
		{
			if (!stack.TryGetValue(name, out var found)) return null;
			raising.Add(found);
		}

		raising.Sort((a, b) => a.Position.CompareTo(b.Position));
		var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var (context, _) in raising)
		{
			foreach (var (key, value) in context.Values) values.TryAdd(key, value);
		}

		var (line, ready) = PaletteLines.Fill(entry.Line, values);
		return new PaletteSuggestion(
			entry,
			line,
			ready,
			entry.Urgent && entry.When is { Count: > 0 },
			[.. raising.Select(item => item.Context.Name)],
			raising.Min(item => item.Context.Weight),
			raising.Select(item => item.Context.Info).FirstOrDefault(info => !string.IsNullOrEmpty(info)) ?? entry.Info);
	}
}
