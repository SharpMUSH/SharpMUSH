using Markdig.Syntax;
using MarkupString;
using SharpMUSH.MarkupString;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public partial class RecursiveMarkdownRenderer
{
	protected virtual MString RenderHeading(HeadingBlock heading)
	{
		var style = heading.Level switch
		{
			1 or 2 => _headingStyle,
			3 => _heading3Style,
			_ => Ansi.Create()
		};

		var content = RenderInlines(heading.Inline);
		return MModule.MarkupSingle(style, content.ToPlainText());
	}

	private MString RenderParagraph(ParagraphBlock para)
	{
		// Paragraph blocks contain inline elements in the Inline property.
		// Trim trailing whitespace because EnableTrackTrivia appends a soft
		// LineBreakInline (rendered as " ") at the end of many paragraphs.
		var content = RenderInlines(para.Inline);
		return MModule.trim(content, " ", TrimType.TrimEnd);
	}

	private MString RenderList(ListBlock list)
	{
		if (!list.IsOrdered)
		{
			// Unordered lists render as a comma-separated list
			var unorderedItems = list
				.OfType<ListItemBlock>()
				.Select(listItem => RenderListItem(listItem))
				.ToList();
			return MModule.multipleWithDelimiter(MModule.single(", "), unorderedItems);
		}

		var itemIndex = 1;
		var items = list
			.OfType<ListItemBlock>()
			.Select(listItem =>
			{
				var prefix = MModule.MarkupSingle(_dimStyle, $"{itemIndex}. ");

				var content = RenderListItem(listItem, itemIndex - 1, list.IsOrdered);
				itemIndex++;
				return MModule.concat(prefix, content);
			})
			.ToList();

		return MModule.multipleWithDelimiter(MModule.single("\n"), items);
	}

	protected virtual MString RenderListItem(ListItemBlock listItem, int index = 0, bool isOrdered = false)
	{
		var parts = listItem
			.Select(child => Render(child))
			.Where(rendered => rendered.Length > 0)
			.ToList();

		var combined = MModule.multiple(parts);

		var trimmed = MModule.trim(combined, " ", TrimType.TrimBoth);
		return trimmed;
	}

	protected virtual MString RenderQuote(QuoteBlock quote)
	{
		var parts = quote
			.Select(Render)
			.Where(rendered => rendered.Length > 0)
			.ToList();

		var content = MModule.multipleWithDelimiter(MModule.single("\n"), parts);

		// Add 2-space indentation to each line via plain-text split.
		// TextAlignerModule.align cannot be used here because it expects exactly
		// N items for an N-column spec; parts has a variable count.
		var plainText = content.ToPlainText();
		if (string.IsNullOrEmpty(plainText)) return MModule.empty();

		var lines = plainText.Split('\n');
		var indentedParts = lines
			.Select(line => MModule.single("  " + line))
			.ToList();
		return MModule.multipleWithDelimiter(MModule.single("\n"), indentedParts);
	}

	private MString RenderThematicBreak()
		=> MModule.MarkupSingle(_dimStyle, string.Concat(Enumerable.Repeat("-", _maxWidth)));

	private MString RenderHtmlBlock(HtmlBlock html)
	{
		// HtmlBlock is a LeafBlock — Markdig does not recurse into it or parse
		// child inlines. The raw HTML lines are all we have, so pass them through.
		var htmlContent = string.Join("\n", html.Lines.Lines
			.Take(html.Lines.Count)
			.Select(line => line.Slice.ToString()));
		return string.IsNullOrWhiteSpace(htmlContent)
			? MModule.empty()
			: MModule.single(htmlContent);
	}
}
