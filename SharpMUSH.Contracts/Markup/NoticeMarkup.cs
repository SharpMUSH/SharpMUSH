using MarkupString;
using MarkupString.Html;

namespace SharpMUSH.Library.Markup;

/// <summary>
/// The lead <c>notice()</c> puts before a message, <c>[JOBS] Error:</c>, and what each reader is sent
/// for it. A browser gets the brackets hidden from its screen reader; a telnet screen reader is sent
/// <c>JOBS error:</c>, with no brackets; every other telnet client the lead as it reads.
/// </summary>
/// <remarks>
/// The lead is marked with <c>span</c> tags, which the portal draws and the rendering worker reads and
/// then drops (<see cref="ForTelnet"/>), so a Pueblo or MXP client is never sent them.
/// </remarks>
public static class NoticeMarkup
{
	/// <summary>The whole lead: the badge and the kind's word.</summary>
	public static readonly HtmlMarkup Lead = HtmlMarkup.Tag("span", new HtmlAttribute("class", "notice"));

	/// <summary>A bracket of the badge, which a screen reader skips.</summary>
	public static readonly HtmlMarkup Hidden = HtmlMarkup.Tag("span", new HtmlAttribute("aria-hidden", "true"));

	/// <summary>The words <c>notice()</c> gives a kind, which a screen reader is sent in lower case.</summary>
	private static readonly HashSet<string> KindWords = new(StringComparer.Ordinal) { "Done:", "Warning:", "Error:" };

	/// <summary>
	/// The lead for <paramref name="source"/> (none when empty) and <paramref name="word"/> (none when
	/// null), coloured <paramref name="colour"/>.
	/// </summary>
	public static MarkupText Build(MarkupText source, string? word, IMarkup colour)
	{
		var parts = new List<MarkupText>();
		if (source.ToPlainText().Trim().Length > 0)
			parts.Add(MarkupText.Concat([MarkupText.Wrap(Hidden, "["), source, MarkupText.Wrap(Hidden, "]")]));
		if (word is not null) parts.Add(MarkupText.Plain(word + ":"));
		return parts.Count == 0
			? MarkupText.Empty
			: MarkupText.Wrap(colour, MarkupText.Wrap(Lead, MarkupText.Join(MarkupText.Space, parts)));
	}

	/// <summary>
	/// <paramref name="text"/> for a telnet client: each lead as a screen reader reads it when
	/// <paramref name="screenReader"/>, and the lead's tags dropped either way.
	/// </summary>
	public static MarkupText ForTelnet(MarkupText text, bool screenReader)
	{
		if (text.Runs.IsDefaultOrEmpty || !text.Runs.Any(run => run.Markups.Contains(Lead))) return text;

		var parts = new List<MarkupText>();
		var position = 0;
		var runs = text.Runs;
		for (var r = 0; r < runs.Length; r++)
		{
			var run = runs[r];
			if (run.Start > position) parts.Add(MarkupText.Plain(text.Text[position..run.Start]));
			if (screenReader && run.Markups.Contains(Lead))
			{
				// The lead's runs, one after another, read as one.
				var last = r;
				while (last + 1 < runs.Length && runs[last + 1].Start == runs[last].End && runs[last + 1].Markups.Contains(Lead)) last++;
				var spoken = Spoken(text.Text[run.Start..runs[last].End]);
				parts.Add(Rewrap(run.Markups, spoken));
				position = runs[last].End;
				r = last;
				continue;
			}
			parts.Add(Rewrap(run.Markups, text.Text.Substring(run.Start, run.Length)));
			position = run.End;
		}
		if (position < text.Length) parts.Add(MarkupText.Plain(text.Text[position..]));
		return MarkupText.Concat(parts);
	}

	/// <summary><c>[JOBS] Error:</c> as <c>JOBS error:</c>, and <c>[JOBS]</c> as <c>JOBS:</c>.</summary>
	private static string Spoken(string lead)
	{
		var words = lead.Replace("[", string.Empty).Replace("]", string.Empty)
			.Split(' ', StringSplitOptions.RemoveEmptyEntries)
			.Select(word => KindWords.Contains(word) ? word.ToLowerInvariant() : word)
			.ToList();
		if (words.Count > 0 && !words[^1].EndsWith(':')) words[^1] += ":";
		return string.Join(' ', words);
	}

	/// <summary><paramref name="text"/> under the markups of a run, less the lead's tags.</summary>
	private static MarkupText Rewrap(MarkupSet markups, string text)
	{
		var kept = markups.Where(markup => !markup.Equals(Lead) && !markup.Equals(Hidden)).ToList();
		return kept.Count == 0 ? MarkupText.Plain(text) : MarkupText.Wrap(MarkupSet.Of(kept), text);
	}
}
