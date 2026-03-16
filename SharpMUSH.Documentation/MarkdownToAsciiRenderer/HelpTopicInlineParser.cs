using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Parsers.Inlines;
using Markdig.Syntax.Inlines;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

/// <summary>
/// Markdig <see cref="InlineParser"/> that recognises bare <c>[topic]</c> shortcut
/// reference links and converts them into proper <see cref="LinkInline"/> nodes with
/// a URL of <c>help &lt;topic&gt;</c>.
/// <para>
/// CommonMark does not define a "bare bracket" link syntax, so Markdig's built-in
/// <see cref="LinkInlineParser"/> would otherwise fall through and emit the brackets
/// as literal text.  This parser is registered <em>before</em>
/// <see cref="LinkInlineParser"/> via <see cref="HelpTopicLinkExtension"/> so it
/// intercepts <c>[topic]</c> first.
/// </para>
/// <para>
/// The parser only matches when the closing <c>]</c> is <em>not</em> immediately
/// followed by <c>(</c> or <c>[</c>; those cases are left to the built-in parser so
/// that regular inline links <c>[text](url)</c> and full reference links
/// <c>[text][ref]</c> are unaffected.
/// </para>
/// </summary>
public class HelpTopicInlineParser : InlineParser
{
	public HelpTopicInlineParser()
	{
		OpeningCharacters = ['['];
	}

	public override bool Match(InlineProcessor processor, ref StringSlice slice)
	{
		// slice.CurrentChar == '[' when called.
		// Save start position so we can restore if the pattern does not match.
		var savedStart = slice.Start;

		// Advance past '['.
		var c = slice.NextChar();

		// Record where the topic text begins, then scan forward.
		// Bail on '[' (nested bracket), '\n' (cross-line spans are not links), or '\0' (end).
		var topicStart = slice.Start;
		while (c != '\0' && c != ']' && c != '[' && c != '\n')
			c = slice.NextChar();

		// Must have hit ']' and the topic must be non-empty.
		if (c != ']' || slice.Start == topicStart)
		{
			slice.Start = savedStart;
			return false;
		}

		var topic = slice.Text.AsSpan(topicStart, slice.Start - topicStart).ToString();

		// Advance past ']'.
		c = slice.NextChar();

		// If followed by '(' or '[' this is a regular link or full reference link —
		// return false so LinkInlineParser handles it normally.
		if (c == '(' || c == '[')
		{
			slice.Start = savedStart;
			return false;
		}

		// Build a fully-formed LinkInline representing "help <topic>".
		// IsClosed = true prevents the inline processor from trying to add
		// further parsed inlines as children of this container.
		var link = new LinkInline("help " + topic, string.Empty)
		{
			IsClosed = true,
			IsShortcut = true,
		};
		link.AppendChild(new LiteralInline(topic));

		processor.Inline = link;
		return true;
	}
}
