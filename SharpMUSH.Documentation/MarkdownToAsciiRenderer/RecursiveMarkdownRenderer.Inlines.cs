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
		// HTML inline tags are not fully supported in the recursive renderer.
		// They would require matching opening/closing tags to properly wrap content with markup.
		// For now, just skip the tags themselves. The content between tags is rendered separately
		// by Markdig as literal inlines, so it will still appear in the output.
		return MModule.empty();
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
