using Markdig.Syntax;
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
		return MModule.concat(MModule.markupSingle(style, ""), content);
	}

	private MString RenderParagraph(ParagraphBlock para)
	{
		// Paragraph blocks contain inline elements in the Inline property
		return RenderInlines(para.Inline);
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
				var prefix = MModule.markupSingle(_dimStyle, $"{itemIndex}. ");

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

		var trimmed = MModule.trim(combined, MModule.single(" "), trimType: MModule.TrimType.TrimBoth);
		return trimmed;
	}

	protected virtual MString RenderQuote(QuoteBlock quote)
	{
		var parts = quote
			.Select(Render)
			.Where(rendered => rendered.Length > 0)
			.ToList();

		var content = MModule.multipleWithDelimiter(MModule.single("\n"), parts);

		// Add 2-space indentation to each line
		var plainText = content.ToPlainText();
		if (string.IsNullOrEmpty(plainText)) return MModule.empty();

		var indentedLines = TextAlignerModule.align(
			$"1 <{_maxWidth}",
			parts,
			MModule.single(" "),
			MModule.single(" "),
			MModule.single("\n")
		);
		return indentedLines;
	}

	private MString RenderThematicBreak()
		=> MModule.markupSingle(_dimStyle, string.Concat(Enumerable.Repeat("-", _maxWidth)));

	private MString RenderHtmlBlock(HtmlBlock html)
	{
		var htmlContent = string.Join("\n", html.Lines.Lines.Select(line => line.Slice.ToString()));
		if (string.IsNullOrWhiteSpace(htmlContent))
			return MModule.empty();

		var tagName = ExtractTagName(htmlContent);
		var ansi = ConvertHtmlTagToAnsi(htmlContent, tagName);
		return ansi is not null
			? MModule.markupSingle(ansi, "")
			: MModule.empty();
	}
}
