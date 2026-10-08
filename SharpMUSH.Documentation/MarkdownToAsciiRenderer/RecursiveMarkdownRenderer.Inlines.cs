using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Renderers.Html;
using Markdig.Syntax.Inlines;
using MarkupString.Ansi;
using MarkupString.Html;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public partial class RecursiveMarkdownRenderer
{
	private MString RenderLiteral(LiteralInline literal)
	{
		var text = literal.Content.ToString();
		// A soft break renders as one space. EnableTrackTrivia leaves the next line's indentation at the
		// start of its first literal, which would follow that space: an item's wrapped line read "a  b".
		if (literal.PreviousSibling is LineBreakInline { IsHard: false })
		{
			text = text.TrimStart(' ', '\t');
		}
		return string.IsNullOrEmpty(text)
			? MarkupText.Empty
			: MarkupText.Plain(text);
	}

	private MString RenderCodeInline(CodeInline code)
		=> string.IsNullOrEmpty(code.Content)
			? MarkupText.Empty
			: RenderInlineCode(code);

	private MString RenderEmphasis(EmphasisInline emphasis)
	{
		var content = RenderInlines(emphasis.FirstChild);

		if (emphasis.DelimiterChar == '~')
		{
			return RenderStrikethrough(content);
		}

		// DelimiterCount determines bold (2) vs italic (1)
		if (emphasis.DelimiterCount == 2 || emphasis.DelimiterChar == '*')
		{
			return RenderBold(content);
		}
		else
		{
			return RenderItalic(content);
		}
	}

	/// <summary>
	/// Render bold text. Can be overridden for custom rendering.
	/// </summary>
	protected virtual MString RenderBold(MString content)
		=> MarkupText.Wrap(_boldStyle, content.ToPlainText());

	/// <summary>
	/// Render <c>~~struck~~</c> text: struck through where the client draws it, and plain text, never the
	/// tildes, where it does not.
	/// </summary>
	protected virtual MString RenderStrikethrough(MString content)
		=> MarkupText.Wrap(_strikeStyle, content.ToPlainText());

	/// <summary>
	/// Render italic text. Can be overridden for custom rendering.
	/// </summary>
	protected virtual MString RenderItalic(MString content)
		=> MarkupText.Wrap(_boldStyle, content.ToPlainText());

	/// <summary>
	/// Render underlined text. Can be overridden for custom rendering.
	/// </summary>
	protected virtual MString RenderUnderline(MString content)
		=> MarkupText.Wrap(_underlineStyle, content.ToPlainText());

	/// <summary>
	/// Render inline code. Can be overridden for custom rendering.
	/// </summary>
	protected virtual MString RenderInlineCode(CodeInline code)
		=> MarkupText.Wrap(InlineCodeStyle, code.Content);

	protected virtual MString RenderLink(LinkInline link, MString content)
	{
		// A terminal sees "[image: <alt>]" (or "[image]"); RenderImage wraps that in the picture
		// for clients that show one when the surface allows it.
		if (link.IsImage)
			return RenderImage(link, content);

		// Create hyperlink using ANSI OSC 8 escape sequence
		var url = link.Url ?? string.Empty;
		var contentText = content.ToPlainText().Trim();

		if (string.IsNullOrWhiteSpace(url))
		{
			return content;
		}

		if (string.IsNullOrWhiteSpace(contentText))
		{
			contentText = url;
		}

		// Help-topic shortcuts ([topic]) are command links; ordinary links navigate.
		// A markdown link title ([text](url "title")) becomes the link hint.
		var isCommand = link.GetData(HelpTopicInlineParser.CommandDataKey) is true;
		if (isCommand)
		{
			var separator = url.IndexOf(' ');
			// A table of topics or a See Also footer is a list of names to pick from, as PennMUSH prints
			// it; elsewhere the command is spelled out unless the text before the link already says it.
			contentText = separator > 0 && (_bareCommandLabels || IsInTableCell(link) || HasCommandPrefix(link, url[..separator]))
				? url[(separator + 1)..]
				: url;
		}
		var hint = string.IsNullOrWhiteSpace(link.Title) ? null : link.Title;
		var linkMarkup = Ansi.Create(
			linkUrl: url,
			linkKind: isCommand ? LinkKind.Command : LinkKind.Url,
			linkText: hint);
		return MarkupText.Wrap(linkMarkup, contentText);
	}

	/// <summary>Set while a See Also footer renders, whose topics are named bare.</summary>
	private bool _bareCommandLabels;

	private static bool IsInTableCell(Inline inline)
	{
		var root = inline;
		while (root.Parent is not null)
		{
			root = root.Parent;
		}
		return root is ContainerInline { ParentBlock.Parent: TableCell };
	}

	private static bool HasCommandPrefix(LinkInline link, string command)
	{
		if (link.PreviousSibling is not LiteralInline literal)
		{
			return false;
		}
		var text = literal.Content.ToString();
		if (text.Length == 0 || !char.IsWhiteSpace(text[^1]))
		{
			return false;
		}
		text = text.TrimEnd();
		if (text.Length == 0 && literal.PreviousSibling is CodeInline code)
		{
			text = code.Content;
		}
		var boundary = text.Length - command.Length;
		return boundary >= 0 && text.EndsWith(command, StringComparison.OrdinalIgnoreCase)
			&& (boundary == 0 || !char.IsLetterOrDigit(text[boundary - 1]) && text[boundary - 1] != '_');
	}

	/// <summary>
	/// Decides whether an image's address may be shown as a picture; null (the default) shows none.
	/// </summary>
	/// <remarks>
	/// A surface that knows the markdown's author may send pictures sets this, holding the address to
	/// <c>image_hosts</c> the way <c>image()</c> does. Help files and markdown from softcode that may not
	/// send out-of-band markup keep the placeholder only.
	/// </remarks>
	public Func<string, bool>? ImageAllowed { get; init; }

	/// <summary>
	/// Renders an image reference as a plain-text placeholder suitable for
	/// terminal/MUSH display: <c>[image: alt text]</c> or <c>[image]</c> when
	/// no alt text is available. When <see cref="ImageAllowed"/> lets the address through, the
	/// placeholder is wrapped in the picture itself, so a client that shows pictures (the web
	/// terminal, MXP, Pueblo) draws it in the line and every other client keeps the placeholder.
	/// </summary>
	protected virtual MString RenderImage(LinkInline link, MString content)
	{
		var alt = content.ToPlainText().Trim();
		var placeholder = ImagePlaceholder(alt);
		return ShownImage(link, alt) is { } image ? MarkupText.Wrap(image, placeholder) : placeholder;
	}

	/// <summary>
	/// An image alone in its paragraph, drawn as a <see cref="MarkupString.Layout.Figure"/> (what <c>figure()</c> builds): the
	/// portal shows it as a figure of its own and a screen reader hears "Image: alt". The figure's art is
	/// the inline picture, so MXP and Pueblo still draw it and a terminal reads the placeholder. An image
	/// <see cref="ImageAllowed"/> refuses is <see cref="RenderImage"/>'s placeholder.
	/// </summary>
	protected virtual MString RenderFigure(LinkInline link, MString content)
	{
		var alt = content.ToPlainText().Trim();
		return ShownImage(link, alt) is { } image
			? Laid(new MarkupString.Layout.Figure(image, MarkupText.Wrap(image, ImagePlaceholder(alt))))
			: RenderImage(link, content);
	}

	private MString ImagePlaceholder(string alt)
		=> MarkupText.Wrap(_dimStyle, string.IsNullOrWhiteSpace(alt) ? "[image]" : $"[image: {alt}]");

	/// <summary>The picture <paramref name="link"/> names, when <see cref="ImageAllowed"/> lets it be shown.</summary>
	private ImageMarkup? ShownImage(LinkInline link, string alt)
	{
		var source = link.Url?.Trim();
		if (string.IsNullOrEmpty(source) || source.Any(char.IsControl) || ImageAllowed?.Invoke(source) is not true)
		{
			return null;
		}

		var attributes = link.TryGetAttributes()?.Properties;
		return new ImageMarkup(source, alt.Length > 0 ? alt : null, Pixels(attributes, "width"), Pixels(attributes, "height"));
	}

	/// <summary>A <c>{width=120}</c> or <c>{width=120px}</c> attribute in pixels; a percentage has no pixel count.</summary>
	private static int? Pixels(List<KeyValuePair<string, string?>>? attributes, string name)
	{
		var value = attributes?.FirstOrDefault(a => string.Equals(a.Key, name, StringComparison.OrdinalIgnoreCase)).Value?.Trim();
		if (value is null) return null;
		if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase)) value = value[..^2];
		return int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var pixels) && pixels > 0
			? pixels
			: null;
	}

	/// <summary>
	/// Renders a <c>[[Page Name]]</c> wiki link as underlined display text, discarding the target.
	/// </summary>
	/// <remarks>
	/// The default rendering is neutral because most surfaces that reach it — help files, news
	/// files, <c>rendermarkdown()</c> — have nowhere to send the reader, and softcode that renders
	/// its own markdown should stay free to present a wiki link however it likes.
	/// <c>@wiki</c> overrides this (see <c>WikiCommandRenderer</c>) to emit a command link running
	/// <c>@wiki &lt;page&gt;</c>, which is how a terminal session does navigate the wiki.
	/// </remarks>
	protected virtual MString RenderWikiLink(WikiLinkInline wikiLink)
	{
		var text = wikiLink.DisplayText ?? wikiLink.Title;
		return string.IsNullOrWhiteSpace(text)
			? MarkupText.Empty
			: RenderUnderline(MarkupText.Plain(text));
	}

	/// <summary>
	/// Renders a task-list marker (<c>- [ ]</c> / <c>- [x]</c>) as literal
	/// bracket notation, which reads naturally in a terminal.
	/// </summary>
	protected virtual MString RenderTaskList(TaskList task)
		=> MarkupText.Plain(task.Checked ? "[x]" : "[ ]");

	protected virtual MString RenderAutolink(AutolinkInline autolink)
	{
		if (string.IsNullOrEmpty(autolink.Url))
		{
			return MarkupText.Empty;
		}

		var linkMarkup = Ansi.Create(linkUrl: autolink.Url);
		return MarkupText.Wrap(linkMarkup, autolink.Url);
	}

	private MString RenderHtmlInline(HtmlInline html)
	{
		var tag = html.Tag;
		if (string.IsNullOrWhiteSpace(tag) || tag.StartsWith("</"))
			return MarkupText.Empty;

		var tagName = ExtractTagName(tag);

		// <br>, <br/>, <br /> are self-closing void elements — render as newline.
		// The source newline after <br> causes Markdig to append a soft
		// LineBreakInline as the next sibling — skip it so it doesn't add a
		// redundant space.
		if (tagName == "br")
		{
			if (html.NextSibling is LineBreakInline { IsHard: false } softBreak)
				softBreak.Remove();
			return MarkupText.Plain("\n");
		}

		var ansi = ConvertHtmlTagToAnsi(tag, tagName);
		if (ansi is null)
			return MarkupText.Empty;

		var closingTag = $"</{tagName}>";
		var contentParts = new List<MString>();
		var sibling = html.NextSibling;
		Inline? closingNode = null;
		while (sibling != null)
		{
			if (sibling is HtmlInline closeHtml &&
				closeHtml.Tag.Equals(closingTag, StringComparison.OrdinalIgnoreCase))
			{
				closingNode = sibling;
				break;
			}
			contentParts.Add(Render(sibling));
			sibling = sibling.NextSibling;
		}

		if (closingNode is null)
			return MarkupText.Empty;

		// Remove rendered siblings from the inline chain so they are not rendered again.
		// Walk from html.NextSibling up to and including closingNode, unlinking each.
		var toRemove = html.NextSibling;
		while (toRemove != null)
		{
			var next = toRemove.NextSibling;
			var wasClosing = ReferenceEquals(toRemove, closingNode);
			toRemove.Remove();
			if (wasClosing) break;
			toRemove = next;
		}

		return MarkupText.Wrap(ansi, MarkupText.Concat(contentParts));
	}

	private MString RenderHtmlEntity(HtmlEntityInline entity)
	{
		var text = entity.Transcoded.ToString();
		return string.IsNullOrEmpty(text)
			? MarkupText.Empty
			: MarkupText.Plain(text);
	}

	private MString RenderDelimiter(DelimiterInline delimiter)
		=> RenderInlines(delimiter.FirstChild);
}
