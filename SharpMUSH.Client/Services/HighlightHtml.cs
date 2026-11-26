using System.Net;
using System.Text;
using SharpMUSH.Client.Models;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Puts what the highlight rules did to a line (<see cref="HighlightHit"/>) into the line's HTML: each marked part
/// of its text wrapped in the part's look, and rewritten text in place of what it replaces.
/// </summary>
/// <remarks>
/// <para>A mark is found by its place in the line's plain text, which is the HTML's text read without its tags.
/// The HTML's tags all stay where they are, a sound's or a link's too; a mark is closed before a tag and opened
/// again after it, so it never crosses one, and a rewrite takes the styling of the text it starts in.</para>
/// <para>When the HTML's text is not the line's text (a picture that stands for its description, say), nothing
/// can be placed by position, so there is no marked HTML; a rewrite of the whole line is then shown as plain text.</para>
/// </remarks>
public static class HighlightHtml
{
	/// <summary>The line's HTML with <paramref name="hit"/> applied, or nothing when the HTML's text is not <paramref name="text"/>.</summary>
	public static Found<string> Apply(string html, string text, HighlightHit hit)
	{
		var plain = text.TrimEnd('\r', '\n');
		IReadOnlyList<HighlightMark> marks = hit.Rewrite is { } whole
			? [new HighlightMark(0, plain.Length, HighlightLook.None, whole)]
			: hit.Marks;
		if (marks.Count == 0) return html;
		if (Mark(html, plain, marks) is string marked) return marked;
		return hit.Rewrite is { } rewrite ? WebUtility.HtmlEncode(rewrite) : new NotFound();
	}

	private static Found<string> Mark(string html, string plain, IReadOnlyList<HighlightMark> marks)
	{
		var output = new StringBuilder(html.Length + marks.Count * 64);
		var read = new StringBuilder(plain.Length);
		var next = 0;
		HighlightMark? open = null;
		var spanOpen = false;

		void CloseSpan()
		{
			if (!spanOpen) return;
			output.Append("</span>");
			spanOpen = false;
		}

		void OpenSpan(HighlightMark mark)
		{
			if (mark.Look.IsNone) return;
			output.Append("<span class=\"").Append(mark.Look.Classes(line: false)).Append("\">");
			spanOpen = true;
		}

		// Ends the mark the text has reached the end of, inserts what is inserted here, and starts the mark that
		// starts here.
		void At(int position)
		{
			if (open is { } current && position >= current.End)
			{
				CloseSpan();
				open = null;
			}
			while (open is null && next < marks.Count && marks[next].Start <= position)
			{
				var mark = marks[next++];
				OpenSpan(mark);
				if (mark.Rewrite is not null) output.Append(WebUtility.HtmlEncode(mark.Rewrite));
				if (mark.Length > 0 && mark.End > position) open = mark;
				else CloseSpan();
			}
		}

		var at = 0;
		while (at < html.Length)
		{
			if (StartsTag(html, at))
			{
				var end = TagEnd(html, at);
				CloseSpan();
				output.Append(html, at, end - at);
				at = end;
				continue;
			}

			var length = PieceLength(html, at);
			var piece = html.AsSpan(at, length);
			At(read.Length);
			if (open is { } mark)
			{
				// A rewrite was written where its mark starts; the text it replaces is left out.
				if (mark.Rewrite is null)
				{
					if (!spanOpen) OpenSpan(mark);
					output.Append(piece);
				}
			}
			else output.Append(piece);
			read.Append(piece[0] == '&' && length > 1 ? WebUtility.HtmlDecode(piece.ToString()) : piece);
			at += length;
		}
		At(read.Length);
		CloseSpan();

		return next == marks.Count && open is null && read.ToString().TrimEnd('\r', '\n') == plain
			? output.ToString()
			: new NotFound();
	}

	/// <summary>
	/// The text <paramref name="html"/> shows: its tags left out and its entities read, as <see cref="Apply"/> reads it
	/// to place a mark.
	/// </summary>
	public static string TextOf(string html)
	{
		var read = new StringBuilder(html.Length);
		var at = 0;
		while (at < html.Length)
		{
			if (StartsTag(html, at))
			{
				at = TagEnd(html, at);
				continue;
			}
			var length = PieceLength(html, at);
			var piece = html.AsSpan(at, length);
			read.Append(piece[0] == '&' && length > 1 ? WebUtility.HtmlDecode(piece.ToString()) : piece);
			at += length;
		}
		return read.ToString();
	}

	/// <summary>
	/// Whether the <c>&lt;</c> at <paramref name="at"/> opens a tag, as a browser reads it: followed by a letter,
	/// <c>/</c> and a letter, <c>!</c> or <c>?</c>. Any other <c>&lt;</c> is shown as text.
	/// </summary>
	private static bool StartsTag(string html, int at)
	{
		if (html[at] != '<' || at + 1 >= html.Length) return false;
		var c = html[at + 1];
		return char.IsAsciiLetter(c) || c is '!' or '?'
			|| (c == '/' && at + 2 < html.Length && char.IsAsciiLetter(html[at + 2]));
	}

	/// <summary>Where the tag starting at <paramref name="at"/> ends: after its <c>&gt;</c>, outside any quoted value.</summary>
	private static int TagEnd(string html, int at)
	{
		var quote = '\0';
		for (var i = at + 1; i < html.Length; i++)
		{
			var c = html[i];
			if (quote != '\0')
			{
				if (c == quote) quote = '\0';
			}
			else if (c is '"' or '\'') quote = c;
			else if (c == '>') return i + 1;
		}
		return html.Length;
	}

	/// <summary>The length of one piece of text at <paramref name="at"/>: an entity, a surrogate pair, or one character.</summary>
	private static int PieceLength(string html, int at)
	{
		if (html[at] == '&')
		{
			var semicolon = html.IndexOf(';', at + 1, Math.Min(32, html.Length - at - 1));
			if (semicolon > at + 1 && html.AsSpan(at + 1, semicolon - at - 1).IndexOfAny(" <&") < 0)
			{
				// Only an entity the decoder reads is one piece; an unknown one stays its own characters, as it is shown.
				var entity = html.Substring(at, semicolon - at + 1);
				if (WebUtility.HtmlDecode(entity) != entity) return entity.Length;
			}
			return 1;
		}
		return char.IsHighSurrogate(html[at]) && at + 1 < html.Length && char.IsLowSurrogate(html[at + 1]) ? 2 : 1;
	}
}
