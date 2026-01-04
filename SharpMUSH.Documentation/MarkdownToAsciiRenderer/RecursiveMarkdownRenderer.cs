using System.Drawing;
using System.Text;
using ANSILibrary;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

/// <summary>
/// Recursive renderer that returns MString for each markdown element,
/// enabling easy composition and use of TextAlignerModule for tables.
/// </summary>
public class RecursiveMarkdownRenderer
{
	private readonly Ansi _dimStyle = Ansi.Create(faint: true);
	private readonly Ansi _boldStyle = Ansi.Create(foreground: StringExtensions.rgb(Color.White), bold: true);
	private readonly Ansi _headingStyle = Ansi.Create(underlined: true, bold: true);
	private readonly Ansi _heading3Style = Ansi.Create(underlined: true);
	private readonly int _maxWidth;

	// Table border and separator character counts
	private const int START_BORDER_WIDTH = 2; // "| "
	private const int END_BORDER_WIDTH = 2; // " |"
	private const int COLUMN_SEPARATOR_WIDTH = 3; // " | "
	
	/// <summary>
	/// Initializes a new instance of the RecursiveMarkdownRenderer
	/// </summary>
	/// <param name="maxWidth">Maximum width for rendered output. Tables will fit to this width with nice column spacing. Default is 78.</param>
	public RecursiveMarkdownRenderer(int maxWidth = 78)
	{
		_maxWidth = maxWidth > 0 ? maxWidth : 78;
	}

	/// <summary>
	/// Main entry point - renders any MarkdownObject to MString
	/// </summary>
	public MString Render(MarkdownObject obj)
	{
		return obj switch
		{
			// Block elements
			MarkdownDocument doc => RenderDocument(doc),
			HeadingBlock heading => RenderHeading(heading),
			ParagraphBlock para => RenderParagraph(para),
			CodeBlock code => RenderCodeBlock(code),
			ListBlock list => RenderList(list),
			ListItemBlock listItem => RenderListItem(listItem),
			QuoteBlock quote => RenderQuote(quote),
			ThematicBreakBlock _ => RenderThematicBreak(),
			HtmlBlock html => RenderHtmlBlock(html),
			Table table => RenderTable(table),
			TableRow row => RenderTableRow(row),
			TableCell cell => RenderTableCell(cell),
			
			// Inline elements - specific types first, then base ContainerInline
			LiteralInline literal => RenderLiteral(literal),
			CodeInline code => RenderCodeInline(code),
			EmphasisInline emphasis => RenderEmphasis(emphasis),
			LineBreakInline _ => RenderLineBreak(),
			LinkInline link => RenderLink(link, RenderInlines(link.FirstChild)),
			AutolinkInline autolink => RenderAutolink(autolink),
			HtmlInline html => RenderHtmlInline(html),
			HtmlEntityInline entity => RenderHtmlEntity(entity),
			DelimiterInline delimiter => RenderDelimiter(delimiter),
			ContainerInline container => RenderContainerInline(container),
			
			// Default case - try to render children if it's a container block
			ContainerBlock container => RenderContainerBlock(container),
			
			_ => MModule.empty()
		};
	}
	
	private MString RenderContainerBlock(ContainerBlock container)
	{
		var parts = container
			.Select(child => Render(child))
			.Where(rendered => rendered.Length > 0)
			.ToList();
		return MModule.multipleWithDelimiter(MModule.single("\n"), parts);
	}

	private MString RenderDocument(MarkdownDocument doc)
	{
		var parts = doc
			.Select(child => Render(child))
			.Where(rendered => rendered.Length > 0)
			.ToList();
		return MModule.multipleWithDelimiter(MModule.single("\n"), parts);
	}

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

	protected virtual MString RenderCodeBlock(CodeBlock code)
	{
		var lines = code.Lines.Lines?
			.Where(line => line.Slice.Text != null)
			.Select(line => MModule.single(line.Slice.ToString()))
			.ToList() ?? new List<MString>();
		
		return MModule.multipleWithDelimiter(MModule.single("\n"), lines);
	}

	private MString RenderList(ListBlock list)
	{
		var itemIndex = 1;
		var items = list
			.OfType<ListItemBlock>()
			.Select(listItem =>
			{
				var prefix = list.IsOrdered 
					? MModule.markupSingle(_dimStyle, $"{itemIndex}. ")
					: MModule.markupSingle(_dimStyle, "- ");
				
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
		
		// Join list item blocks without newlines between them
		var combined = MModule.multiple(parts);
		
		// Trim leading/trailing whitespace from the plain text and recreate MString
		// This removes extra spaces that Markdig might preserve from markdown formatting
		var trimmed = combined.ToPlainText().Trim();
		return MModule.single(trimmed);
	}

	protected virtual MString RenderQuote(QuoteBlock quote)
	{
		var parts = quote
			.Select(child => Render(child))
			.Where(rendered => rendered.Length > 0)
			.ToList();
		
		var content = MModule.multipleWithDelimiter(MModule.single("\n"), parts);
		
		// Add 2-space indentation to each line
		var plainText = content.ToPlainText();
		if (string.IsNullOrEmpty(plainText)) return MModule.empty();
		
		var lines = plainText.Split('\n');
		var indentedLines = lines.Select(line => MModule.single("  " + line));
		return MModule.multipleWithDelimiter(MModule.single("\n"), indentedLines);
	}

	private MString RenderThematicBreak()
	{
		return MModule.markupSingle(_dimStyle, "---");
	}

	private MString RenderHtmlBlock(HtmlBlock html)
	{
		// Parse HTML block and convert to ANSI markup
		var htmlContent = string.Join("\n", html.Lines.Lines.Select(line => line.Slice.ToString()));
		return ParseHtmlToAnsi(htmlContent);
	}

	protected virtual MString RenderTable(Table table)
	{
		var borderStyle = _dimStyle;
		
		// Collect all rows with their cell contents using LINQ
		var allRows = table
			.OfType<TableRow>()
			.Select(row => (
				IsHeader: row.IsHeader,
				Cells: row.OfType<TableCell>()
					.Select(cell => RenderTableCell(cell))
					.ToList()
			))
			.ToList();
		
		if (allRows.Count == 0) return MModule.empty();
		
		// Calculate column widths
		var columnCount = allRows.Max(r => r.Cells.Count);
		var columnWidths = new int[columnCount];
		
		for (int col = 0; col < columnCount; col++)
		{
			columnWidths[col] = allRows.Max(r => col < r.Cells.Count ? r.Cells[col].ToPlainText().Length : 0);
			columnWidths[col] = Math.Max(columnWidths[col], 3);
		}
		
		// Fit table to available width by distributing space across columns
		// Format: "| cell1 | cell2 | cell3 |"
		// Total width = START_BORDER + content widths + separators + END_BORDER
		var borderAndSeparatorWidth = START_BORDER_WIDTH + END_BORDER_WIDTH + 
		                               (columnCount - 1) * COLUMN_SEPARATOR_WIDTH;
		var availableWidth = _maxWidth - borderAndSeparatorWidth;
		var totalWidth = columnWidths.Sum();
		
		if (totalWidth > availableWidth && availableWidth > columnCount * 3)
		{
			// Scale down column widths proportionally when table is too wide
			for (int col = 0; col < columnCount; col++)
			{
				var proportion = (double)columnWidths[col] / totalWidth;
				columnWidths[col] = Math.Max(3, (int)(availableWidth * proportion));
			}
		}
		else if (totalWidth < availableWidth)
		{
			// Expand columns proportionally to fit the available width for nice spacing
			var extraSpace = availableWidth - totalWidth;
			for (int col = 0; col < columnCount; col++)
			{
				var proportion = (double)columnWidths[col] / totalWidth;
				columnWidths[col] += (int)(extraSpace * proportion);
			}
		}
		
		// Build column specifications with alignment
		var columnSpecs = new StringBuilder();
		for (int col = 0; col < columnCount; col++)
		{
			if (col > 0) columnSpecs.Append(' ');
			
			var alignment = "<"; // Default to left
			if (table.ColumnDefinitions.Count > col && table.ColumnDefinitions[col].Alignment.HasValue)
			{
				alignment = table.ColumnDefinitions[col].Alignment!.Value switch
				{
					TableColumnAlign.Left => "<",
					TableColumnAlign.Center => "-",
					TableColumnAlign.Right => ">",
					_ => "<"
				};
			}
			
			columnSpecs.Append(alignment);
			columnSpecs.Append(columnWidths[col]);
		}
		
		// Render each row
		var renderedRows = new List<MString>();
		for (int rowIndex = 0; rowIndex < allRows.Count; rowIndex++)
		{
			var (isHeader, cells) = allRows[rowIndex];
			
			// Use TextAlignerModule to align the cells
			var alignedRow = SharpMUSH.MarkupString.TextAlignerModule.align(
				columnSpecs.ToString(),
				cells,
				MModule.single(" "),
				MModule.markupSingle(borderStyle, " | "),
				MModule.single("")
			);
			
			// Wrap in borders
			var rowWithBorders = MModule.multiple([
				MModule.markupSingle(borderStyle, "| "),
				alignedRow,
				MModule.markupSingle(borderStyle, " |")
			]);
			
			renderedRows.Add(rowWithBorders);
			
			// Add separator after header
			if (isHeader)
			{
				var separator = new StringBuilder();
				separator.Append("|");
				for (int col = 0; col < columnCount; col++)
				{
					separator.Append('-', columnWidths[col] + 2);
					separator.Append('|');
				}
				renderedRows.Add(MModule.markupSingle(borderStyle, separator.ToString()));
			}
		}
		
		return MModule.multipleWithDelimiter(MModule.single("\n"), renderedRows);
	}

	private MString RenderTableRow(TableRow row)
	{
		// Rows are handled by RenderTable for proper alignment
		return MModule.empty();
	}

	private MString RenderTableCell(TableCell cell)
	{
		var parts = cell
			.Select(child => Render(child))
			.Where(rendered => rendered.Length > 0)
			.ToList();
		
		// Join cell blocks without newlines between them
		return MModule.multiple(parts);
	}

	// Inline renderers
	
	private MString RenderInlines(Inline? inline)
	{
		var parts = new List<MString>();
		while (inline != null)
		{
			var rendered = Render(inline);
			if (rendered.Length > 0)
			{
				parts.Add(rendered);
			}
			inline = inline.NextSibling;
		}
		return MModule.multiple(parts);
	}

	
	private MString RenderContainerInline(ContainerInline container)
	{
		// ContainerInline has FirstChild - render all children
		return RenderInlines(container.FirstChild);
	}

	private MString RenderLiteral(LiteralInline literal)
	{
		// StringSlice.ToString() handles the conversion properly
		var text = literal.Content.ToString();
		return string.IsNullOrEmpty(text) ? MModule.empty() : MModule.single(text);
	}

	private MString RenderCodeInline(CodeInline code)
	{
		// Code content is a string, not StringSlice
		return string.IsNullOrEmpty(code.Content) ? MModule.empty() : RenderInlineCode(code.Content);
	}

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
	{
		// Apply bold style to the content's plain text
		return MModule.markupSingle(_boldStyle, content.ToPlainText());
	}

	/// <summary>
	/// Render italic text. Can be overridden for custom rendering.
	/// </summary>
	protected virtual MString RenderItalic(MString content)
	{
		// Default uses same style as bold (ANSI italic support varies)
		// Apply italic (displayed as bold) to the content's plain text
		return MModule.markupSingle(_boldStyle, content.ToPlainText());
	}

	/// <summary>
	/// Render underlined text. Can be overridden for custom rendering.
	/// </summary>
	protected virtual MString RenderUnderline(MString content)
	{
		var underlineStyle = Ansi.Create(underlined: true);
		return MModule.markupSingle(underlineStyle, content.ToPlainText());
	}

	/// <summary>
	/// Render inline code. Can be overridden for custom rendering.
	/// </summary>
	protected virtual MString RenderInlineCode(string content)
	{
		return MModule.single(content);
	}

	private MString RenderLineBreak()
	{
		return MModule.single("\n");
	}

	protected virtual MString RenderLink(LinkInline link, MString content)
	{
		// Use the provided content parameter (already rendered by caller)
		return content;
	}

	private MString RenderAutolink(AutolinkInline autolink)
	{
		return string.IsNullOrEmpty(autolink.Url) ? MModule.empty() : MModule.single(autolink.Url);
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
		return string.IsNullOrEmpty(text) ? MModule.empty() : MModule.single(text);
	}

	private MString RenderDelimiter(DelimiterInline delimiter)
	{
		return RenderInlines(delimiter.FirstChild);
	}

	/// <summary>
	/// Parses HTML tags and converts them to ANSI markup.
	/// Supports basic color tags, bold, italic, underline, etc.
	/// </summary>
	private MString ParseHtmlToAnsi(string tag)
	{
		if (string.IsNullOrWhiteSpace(tag))
			return MModule.empty();

		var tagName = ExtractTagName(tag);
		var isClosing = tag.StartsWith("</");

		if (isClosing)
		{
			// Closing tag - return clear formatting as actual ANSI string
			return MModule.single(ANSILibrary.ANSI.Clear);
		}
		else
		{
			// Opening tag - convert to ANSI code string
			var ansiCode = ConvertHtmlTagToAnsiCode(tag, tagName);
			return string.IsNullOrEmpty(ansiCode) ? MModule.empty() : MModule.single(ansiCode);
		}
	}

	private string ExtractTagName(string tag)
	{
		var start = tag.StartsWith("</") ? 2 : 1;
		var end = tag.IndexOfAny([' ', '>', '/'], start);
		if (end == -1) end = tag.Length;
		return tag.Substring(start, end - start).ToLowerInvariant();
	}

	private string ConvertHtmlTagToAnsiCode(string tag, string tagName)
	{
		return tagName switch
		{
			"b" or "strong" => ANSILibrary.ANSI.Bold + ANSILibrary.ANSI.Foreground(ANSILibrary.ANSI.AnsiColor.NewRGB(Color.White)),
			"i" or "em" => ANSILibrary.ANSI.Italic,
			"u" => ANSILibrary.ANSI.Underlined,
			"s" or "strike" or "del" => ANSILibrary.ANSI.StrikeThrough,
			"font" => ParseFontTagToAnsiCode(tag),
			"span" => ParseSpanTagToAnsiCode(tag),
			"color" => ParseColorTagToAnsiCode(tag),
			_ => ""
		};
	}

	private string ParseFontTagToAnsiCode(string tag)
	{
		// Extract color attribute: <font color="red"> or <font color="#FF0000">
		var colorMatch = System.Text.RegularExpressions.Regex.Match(tag, @"color\s*=\s*[""']([^""']+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
		if (colorMatch.Success)
		{
			var color = ParseColorValue(colorMatch.Groups[1].Value);
			if (color.HasValue)
				return ANSILibrary.ANSI.Foreground(ANSILibrary.ANSI.AnsiColor.NewRGB(color.Value));
		}
		return "";
	}

	private string ParseSpanTagToAnsiCode(string tag)
	{
		// Extract style attribute: <span style="color: red"> or <span style="background-color: blue">
		var styleMatch = System.Text.RegularExpressions.Regex.Match(tag, @"style\s*=\s*[""']([^""']+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
		if (styleMatch.Success)
		{
			var style = styleMatch.Groups[1].Value;
			
			// Parse color
			var colorMatch = System.Text.RegularExpressions.Regex.Match(style, @"color\s*:\s*([^;]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
			var bgColorMatch = System.Text.RegularExpressions.Regex.Match(style, @"background-color\s*:\s*([^;]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
			
			var result = "";
			
			if (colorMatch.Success)
			{
				var fg = ParseColorValue(colorMatch.Groups[1].Value.Trim());
				if (fg.HasValue)
					result += ANSILibrary.ANSI.Foreground(ANSILibrary.ANSI.AnsiColor.NewRGB(fg.Value));
			}
			
			if (bgColorMatch.Success)
			{
				var bg = ParseColorValue(bgColorMatch.Groups[1].Value.Trim());
				if (bg.HasValue)
					result += ANSILibrary.ANSI.Background(ANSILibrary.ANSI.AnsiColor.NewRGB(bg.Value));
			}
			
			return result;
		}
		return "";
	}

	private string ParseColorTagToAnsiCode(string tag)
	{
		// Extract color value: <color red> or <color #FF0000>
		var match = System.Text.RegularExpressions.Regex.Match(tag, @"<color\s+([^>]+)>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
		if (match.Success)
		{
			var color = ParseColorValue(match.Groups[1].Value.Trim());
			if (color.HasValue)
				return ANSILibrary.ANSI.Foreground(ANSILibrary.ANSI.AnsiColor.NewRGB(color.Value));
		}
		return "";
	}

	private Color? ParseColorValue(string colorStr)
	{
		colorStr = colorStr.Trim();
		
		// Hex color: #RRGGBB or #RGB
		if (colorStr.StartsWith("#"))
		{
			try
			{
				if (colorStr.Length == 7) // #RRGGBB
				{
					var r = Convert.ToByte(colorStr.Substring(1, 2), 16);
					var g = Convert.ToByte(colorStr.Substring(3, 2), 16);
					var b = Convert.ToByte(colorStr.Substring(5, 2), 16);
					return Color.FromArgb(r, g, b);
				}
				else if (colorStr.Length == 4) // #RGB - expand each digit
				{
					var r = (byte)(Convert.ToByte(colorStr.Substring(1, 1), 16) * 17);
					var g = (byte)(Convert.ToByte(colorStr.Substring(2, 1), 16) * 17);
					var b = (byte)(Convert.ToByte(colorStr.Substring(3, 1), 16) * 17);
					return Color.FromArgb(r, g, b);
				}
			}
			catch { return null; }
		}
		
		// Named colors
		try
		{
			return Color.FromName(colorStr);
		}
		catch
		{
			return null;
		}
	}
}
