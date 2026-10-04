using MarkupString.Ansi;
using System.Text;
using System.Text.RegularExpressions;

namespace SharpMUSH.Implementation.Commands;

/// <summary>
/// One attribute's <c>@edit</c>: the value to store, the value <c>@edit</c> shows with each replacement
/// highlighted, and whether the search matched at all.
/// </summary>
/// <param name="Text">The edited value, as it is stored.</param>
/// <param name="Shown">
/// The edited value as the <c>- Set:</c> line shows it: <c>edit_helper</c> and <c>regedit_helper</c>
/// (<c>src/set.c:826-890</c>, <c>:1105-1121</c>) wrap each replacement in <c>ANSI_HILITE</c> ... <c>ANSI_END</c>
/// for every player; a client without colour gets the plain text, as for any other markup.
/// </param>
/// <param name="Matched">
/// Whether the search matched: <c>edit_helper</c> counts an attribute as edited on a match, even when the
/// replacement leaves the value as it was (<c>src/set.c:917</c>).
/// </param>
public readonly record struct AttributeEdit(string Text, MString Shown, bool Matched)
{
	/// <summary>An attribute the search did not match: stored and shown as it was.</summary>
	public static AttributeEdit Unmatched(string text) => new(text, MarkupText.Plain(text), false);

	/// <summary><c>ANSI_HILITE</c> around <paramref name="replacement"/>: bold, rendered by every format.</summary>
	public static MString Highlight(string replacement) => MarkupText.Wrap(AnsiMarkup.Create(bold: true), replacement);

	/// <summary>
	/// <c>edit_helper</c>'s plain search (<c>src/set.c:791</c>): <c>$</c> appends, <c>^</c> prepends, anything else
	/// is replaced where it occurs (only the first time under <paramref name="firstOnly"/>), compared exactly.
	/// </summary>
	public static AttributeEdit Simple(string text, string search, string replace, bool firstOnly)
	{
		switch (search)
		{
			case "$":
				return new AttributeEdit(text + replace, MarkupText.Concat(MarkupText.Plain(text), Highlight(replace)), true);
			case "^":
				return new AttributeEdit(replace + text, MarkupText.Concat(Highlight(replace), MarkupText.Plain(text)), true);
		}

		var spans = new List<(int Index, int Length, string Replacement)>();
		for (var index = text.IndexOf(search, StringComparison.Ordinal);
				 index >= 0;
				 index = text.IndexOf(search, index + search.Length, StringComparison.Ordinal))
		{
			spans.Add((index, search.Length, replace));
			if (firstOnly)
			{
				break;
			}
		}

		return spans.Count == 0 ? Unmatched(text) : Spliced(text, spans, highlightEmpty: true);
	}

	/// <summary>
	/// <paramref name="text"/> with <c>matches[from..]</c> replaced by the matching <paramref name="replacements"/>.
	/// <c>regedit_helper</c> highlights only a replacement that is not empty (<c>src/set.c:1107</c>).
	/// </summary>
	public static AttributeEdit Spliced(string text, Match[] matches, string[] replacements, int from)
		=> from >= matches.Length
			? new AttributeEdit(text, MarkupText.Plain(text), matches.Length > 0)
			: Spliced(text,
				[.. Enumerable.Range(from, matches.Length - from).Select(i => (matches[i].Index, matches[i].Length, replacements[i]))],
				highlightEmpty: false);

	private static AttributeEdit Spliced(string text, IReadOnlyList<(int Index, int Length, string Replacement)> spans,
		bool highlightEmpty)
	{
		var stored = new StringBuilder(text.Length);
		var shown = new List<MString>(spans.Count * 2 + 1);
		var position = 0;
		foreach (var (index, length, replacement) in spans)
		{
			var unchanged = text[position..index];
			stored.Append(unchanged).Append(replacement);
			shown.Add(MarkupText.Plain(unchanged));
			shown.Add(highlightEmpty || replacement.Length > 0 ? Highlight(replacement) : MarkupText.Plain(replacement));
			position = index + length;
		}

		var rest = text[position..];
		stored.Append(rest);
		shown.Add(MarkupText.Plain(rest));
		return new AttributeEdit(stored.ToString(), MarkupText.Concat(shown), true);
	}
}
