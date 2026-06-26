using Markdig.Extensions.TaskLists;
using Markdig.Syntax.Inlines;
using MarkupString.MarkupImplementation;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public partial class RecursiveMarkdownRenderer
{
	private MString RenderLiteral(LiteralInline literal)
	{
		var text = literal.Content.ToString();
		return string.IsNullOrEmpty(text)
			? MModule.empty()
			: MModule.single(text);
	}

	private MString RenderCodeInline(CodeInline code)
		=> string.IsNullOrEmpty(code.Content)
			? MModule.empty()
			: RenderInlineCode(code);

	private MString RenderEmphasis(EmphasisInline emphasis)
	{
		var content = RenderInlines(emphasis.FirstChild);

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
		=> MModule.MarkupSingle(_boldStyle, content.ToPlainText());

	/// <summary>
	/// Render italic text. Can be overridden for custom rendering.
	/// </summary>
	protected virtual MString RenderItalic(MString content)
		=> MModule.MarkupSingle(_boldStyle, content.ToPlainText());

	/// <summary>
	/// Render underlined text. Can be overridden for custom rendering.
	/// </summary>
	protected virtual MString RenderUnderline(MString content)
		=> MModule.MarkupSingle(_underlineStyle, content.ToPlainText());

	/// <summary>
	/// Render inline code. Can be overridden for custom rendering.
	/// </summary>
	protected virtual MString RenderInlineCode(CodeInline code)
		=> MModule.MarkupSingle(InlineCodeStyle, code.Content);

	protected virtual MString RenderLink(LinkInline link, MString content)
	{
		// Images have no meaningful textual representation in a terminal context.
		// Render as "[image: <alt>]" using the alt text extracted from the inline
		// content, falling back to "[image]" when no alt text is provided.
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
		var hint = string.IsNullOrWhiteSpace(link.Title) ? null : link.Title;
		var linkMarkup = Ansi.Create(
			linkUrl: url,
			linkKind: isCommand ? LinkKind.Command : LinkKind.Url,
			linkText: hint);
		return MModule.MarkupSingle(linkMarkup, contentText);
	}

	/// <summary>
	/// Renders an image reference as a plain-text placeholder suitable for
	/// terminal/MUSH display: <c>[image: alt text]</c> or <c>[image]</c> when
	/// no alt text is available.
	/// </summary>
	protected virtual MString RenderImage(LinkInline link, MString content)
	{
		var alt = content.ToPlainText().Trim();
		var placeholder = string.IsNullOrWhiteSpace(alt)
			? "[image]"
			: $"[image: {alt}]";
		return MModule.MarkupSingle(_dimStyle, placeholder);
	}

	/// <summary>
	/// Renders a <c>[[Page Name]]</c> wiki link as underlined display text.
	/// Wiki pages live on the web portal; a terminal session cannot navigate to
	/// them, so the link reads as emphasised prose rather than a hyperlink.
	/// </summary>
	protected virtual MString RenderWikiLink(WikiLinkInline wikiLink)
	{
		var text = wikiLink.DisplayText ?? wikiLink.Title;
		return string.IsNullOrWhiteSpace(text)
			? MModule.empty()
			: RenderUnderline(MModule.single(text));
	}

	/// <summary>
	/// Renders a task-list marker (<c>- [ ]</c> / <c>- [x]</c>) as literal
	/// bracket notation, which reads naturally in a terminal.
	/// </summary>
	protected virtual MString RenderTaskList(TaskList task)
		=> MModule.single(task.Checked ? "[x]" : "[ ]");

	protected virtual MString RenderAutolink(AutolinkInline autolink)
	{
		if (string.IsNullOrEmpty(autolink.Url))
		{
			return MModule.empty();
		}

		var linkMarkup = Ansi.Create(linkUrl: autolink.Url);
		return MModule.MarkupSingle(linkMarkup, autolink.Url);
	}

	private MString RenderHtmlInline(HtmlInline html)
	{
		var tag = html.Tag;
		if (string.IsNullOrWhiteSpace(tag) || tag.StartsWith("</"))
			return MModule.empty();

		var tagName = ExtractTagName(tag);

		// <br>, <br/>, <br /> are self-closing void elements — render as newline.
		// The source newline after <br> causes Markdig to append a soft
		// LineBreakInline as the next sibling — skip it so it doesn't add a
		// redundant space.
		if (tagName == "br")
		{
			if (html.NextSibling is LineBreakInline { IsHard: false } softBreak)
				softBreak.Remove();
			return MModule.single("\n");
		}

		var ansi = ConvertHtmlTagToAnsi(tag, tagName);
		if (ansi is null)
			return MModule.empty();

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
			return MModule.empty();

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

		return MModule.MarkupMultiple(ansi, contentParts);
	}

	private MString RenderHtmlEntity(HtmlEntityInline entity)
	{
		var text = entity.Transcoded.ToString();
		return string.IsNullOrEmpty(text)
			? MModule.empty()
			: MModule.single(text);
	}

	private MString RenderDelimiter(DelimiterInline delimiter)
		=> RenderInlines(delimiter.FirstChild);
}
