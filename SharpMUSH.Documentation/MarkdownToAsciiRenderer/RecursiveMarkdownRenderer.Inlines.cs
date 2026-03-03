using Markdig.Syntax.Inlines;
using Microsoft.FSharp.Core;

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
			// Bold
			return RenderBold(content);
		}
		else
		{
			// Italic
			return RenderItalic(content);
		}
	}

	/// <summary>
	/// Render bold text. Can be overridden for custom rendering.
	/// </summary>
	protected virtual MString RenderBold(MString content)
		=> MModule.markupSingle(_boldStyle, content.ToPlainText());

	/// <summary>
	/// Render italic text. Can be overridden for custom rendering.
	/// </summary>
	protected virtual MString RenderItalic(MString content)
		=> MModule.markupSingle(_boldStyle, content.ToPlainText());

	/// <summary>
	/// Render underlined text. Can be overridden for custom rendering.
	/// </summary>
	protected virtual MString RenderUnderline(MString content)
		=> MModule.markupSingle(_underlineStyle, content.ToPlainText());

	/// <summary>
	/// Render inline code. Can be overridden for custom rendering.
	/// </summary>
	protected virtual MString RenderInlineCode(CodeInline code)
		=> MModule.markupSingle(InlineCodeStyle, code.Content);

	protected virtual MString RenderLink(LinkInline link, MString content)
	{
		// Create hyperlink using ANSI OSC 8 escape sequence
		var url = link.Url ?? string.Empty;
		var contentText = content.ToPlainText().Trim();

		if (string.IsNullOrWhiteSpace(url))
		{
			// No URL, just return the content
			return content;
		}

		if (string.IsNullOrWhiteSpace(contentText))
		{
			// No text, use URL as display text
			contentText = url;
		}

		// Create hyperlink markup with linkUrl parameter
		var linkMarkup = Ansi.Create(linkUrl: FSharpOption<string>.Some(url));
		return MModule.markupSingle(linkMarkup, contentText);
	}

	protected virtual MString RenderAutolink(AutolinkInline autolink)
	{
		if (string.IsNullOrEmpty(autolink.Url))
		{
			return MModule.empty();
		}

		// Create hyperlink with URL as both the text and the link
		var linkMarkup = Ansi.Create(linkUrl: FSharpOption<string>.Some(autolink.Url));
		return MModule.markupSingle(linkMarkup, autolink.Url);
	}

	private MString RenderHtmlInline(HtmlInline html)
	{
		var tag = html.Tag;
		if (string.IsNullOrWhiteSpace(tag) || tag.StartsWith("</"))
			return MModule.empty();

		var tagName = ExtractTagName(tag);
		var ansi = ConvertHtmlTagToAnsi(tag, tagName);
		if (ansi is null)
			return MModule.empty();

		// Collect sibling content until the matching closing tag, then wrap with markup.
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

		var content = MModule.multiple(contentParts);
		return MModule.markupMultiple(ansi, [content]);
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
