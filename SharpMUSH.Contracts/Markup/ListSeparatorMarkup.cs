using System.Text.Json;
using MarkupString;
using MarkupString.Ansi;

namespace SharpMUSH.Library.Markup;

/// <summary>
/// The data layer <c>%*&lt;text&gt;</c> puts on its text: these characters separate the items of a list and are
/// not part of one. A list function given a marked delimiter splits only where the same text carries this mark,
/// so a <c>|</c> in a box border, or one a player typed, stays inside its item.
/// </summary>
/// <remarks>
/// It is data, not display (<see cref="IDataMarkup"/>): no format draws it, and stripping colour keeps it.
/// The mark carries nothing else, so two marked stretches side by side are one: <c>%*&lt;|&gt;%*&lt;|&gt;</c>
/// is <c>%*&lt;||&gt;</c>.
/// </remarks>
public sealed record ListSeparatorMarkup : IDataMarkup
{
	/// <summary>The one value; the mark carries nothing, so every instance is this one.</summary>
	public static readonly ListSeparatorMarkup Instance = new();

	private ListSeparatorMarkup() { }
}

/// <summary>Stores <see cref="ListSeparatorMarkup"/>: the kind alone, since the mark carries nothing.</summary>
public sealed class ListSeparatorCodec : IMarkupCodec
{
	public string Kind => "listsep";
	public Type MarkupType => typeof(ListSeparatorMarkup);
	public void Write(Utf8JsonWriter writer, IMarkup markup) { }
	public IMarkup Read(JsonElement element) => ListSeparatorMarkup.Instance;
}

/// <summary>Marking, finding and splitting on list separators (<see cref="ListSeparatorMarkup"/>).</summary>
public static class ListSeparators
{
	/// <summary>Registers the separator's codec, so a stored separator reads back as one.</summary>
	public static MarkupRegistry WithListSeparators(this MarkupRegistry registry) => registry.With(new ListSeparatorCodec());

	/// <summary>How the data view draws a separator: reverse video.</summary>
	private static readonly AnsiMarkup Shown = AnsiMarkup.Create(inverted: true);

	/// <summary><paramref name="text"/> marked as a list separator, keeping its colour.</summary>
	public static MarkupText Mark(MarkupText text) => MarkupText.Wrap(ListSeparatorMarkup.Instance, text);

	/// <summary>Whether <paramref name="delimiter"/> is a separator: not empty, and marked over its whole length.</summary>
	public static bool IsMarked(MarkupText delimiter) => delimiter.Length > 0 && Covers(delimiter, 0, delimiter.Length);

	/// <summary>Whether every character of <c>[start, start + length)</c> in <paramref name="text"/> is marked.</summary>
	public static bool Covers(MarkupText text, int start, int length)
	{
		var runs = text.Runs;
		if (runs.IsDefaultOrEmpty) return false;

		var position = start;
		var end = start + length;
		var index = RunAt(runs, position);
		while (position < end)
		{
			if (index >= runs.Length) return false;
			var run = runs[index];
			if (run.Start > position || !run.Markups.Contains(ListSeparatorMarkup.Instance)) return false;
			position = run.End;
			index++;
		}
		return true;
	}

	/// <summary>
	/// Every item of <paramref name="text"/> between the marked occurrences of <paramref name="delimiter"/>,
	/// empty ones included: the marked counterpart of <see cref="MarkupText.Split(string)"/>.
	/// </summary>
	public static MarkupText[] Split(MarkupText text, string delimiter)
	{
		if (text.Length == 0) return [];
		if (delimiter.Length == 0) return [text];

		var items = new List<MarkupText>();
		var cursor = 0;
		var at = IndexOf(text, delimiter, 0);
		while (at >= 0)
		{
			items.Add(text.Substring(cursor, at - cursor));
			cursor = at + delimiter.Length;
			at = IndexOf(text, delimiter, cursor);
		}
		items.Add(text.Substring(cursor));
		return [.. items];
	}

	/// <summary>The first marked occurrence of <paramref name="delimiter"/> at or after <paramref name="from"/>, or -1.</summary>
	public static int IndexOf(MarkupText text, string delimiter, int from)
	{
		var raw = text.Text;
		while (from <= raw.Length - delimiter.Length)
		{
			var at = raw.IndexOf(delimiter, from, StringComparison.Ordinal);
			if (at < 0) return -1;
			if (Covers(text, at, delimiter.Length)) return at;
			from = at + 1;
		}
		return -1;
	}

	/// <summary>
	/// <paramref name="text"/> with each separator drawn in reverse video, for a reader who asked to see them
	/// (the <c>SHOW_SEPARATORS</c> flag). Text with none comes back as it is.
	/// </summary>
	public static MarkupText Show(MarkupText text)
	{
		var runs = text.Runs;
		if (runs.IsDefaultOrEmpty || !runs.Any(run => run.Markups.Contains(ListSeparatorMarkup.Instance))) return text;

		var parts = new List<MarkupText>(runs.Length * 2 + 1);
		var position = 0;
		foreach (var run in runs)
		{
			if (!run.Markups.Contains(ListSeparatorMarkup.Instance)) continue;
			if (run.Start > position) parts.Add(text.Substring(position, run.Start - position));
			parts.Add(MarkupText.Wrap(Shown, text.Substring(run.Start, run.Length)));
			position = run.End;
		}
		if (position < text.Length) parts.Add(text.Substring(position));
		return MarkupText.Concat(parts);
	}

	/// <summary>The index of the first run that ends after <paramref name="position"/>.</summary>
	private static int RunAt(System.Collections.Immutable.ImmutableArray<Run> runs, int position)
	{
		int low = 0, high = runs.Length;
		while (low < high)
		{
			var middle = (low + high) / 2;
			if (runs[middle].End <= position) low = middle + 1;
			else high = middle;
		}
		return low;
	}
}
