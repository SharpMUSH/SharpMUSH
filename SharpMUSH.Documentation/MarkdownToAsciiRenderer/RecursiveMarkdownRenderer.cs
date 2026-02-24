using ANSILibrary;
using ColorCode;
using ColorCode.Common;
using ColorCode.Compilation;
using ColorCode.Parsing;
using ColorCode.Styling;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.FSharp.Core;
using SharpMUSH.Library.ParserInterfaces;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

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
	private readonly IMUSHCodeParser? _mushParser;

	// Table border and separator character counts
	private const int START_BORDER_WIDTH = 2; // "| "
	private const int END_BORDER_WIDTH = 2; // " |"
	private const int COLUMN_SEPARATOR_WIDTH = 3; // " | "

	// Cached compiled regex patterns for performance
	private static readonly Regex ColorAttributeRegex = new(@"color\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
	private static readonly Regex StyleAttributeRegex = new(@"style\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
	private static readonly Regex StyleColorRegex = new(@"color\s*:\s*([^;]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
	private static readonly Regex StyleBackgroundColorRegex = new(@"background-color\s*:\s*([^;]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
	private static readonly Regex ColorTagRegex = new(@"<color\s+([^>]+)>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	// ColorCode.Core language parser and style dictionary for standard-language code blocks.
	// Shared across all renderer instances; lazy-initialised on first use.
	// The lock is held as a named static field (process lifetime) so the compiler's
	// CA2000 disposable-not-disposed analysis is satisfied. As a process-lifetime
	// singleton, it is cleaned up when the process exits.
	private static readonly ReaderWriterLockSlim _colorCodeLock = new();
	private static readonly Lazy<LanguageParser> ColorCodeParser = new(() =>
	{
		var compiler = new LanguageCompiler(new Dictionary<string, CompiledLanguage>(), _colorCodeLock);
		var repo = new LanguageRepository(Languages.All.ToDictionary(l => l.Id, l => l));
		return new LanguageParser(compiler, repo);
	});
	private static readonly StyleDictionary ColorCodeStyles = StyleDictionary.DefaultDark;

	// Dark-grey background applied to every line of every highlighted code block,
	// giving a subtle "gutter" effect that visually separates code from prose.
	// #2D2D2D is slightly lighter than a typical #1E1E1E terminal background.
	// Including a default foreground (#D4D4D4 light-grey, matching VS Code DefaultDark) is
	// essential: MString's WrapAndRestore restores the OUTER markup after each inner coloured
	// span, so if the outer style only has background the foreground from the inner span
	// bleeds into the following plain-text segment.  With both fg+bg in the outer style,
	// WrapAndRestore re-applies both after every inner span, keeping colours correct.
	private static readonly Ansi CodeBackgroundStyle = Ansi.Create(
		foreground: StringExtensions.rgb(Color.FromArgb(0xD4, 0xD4, 0xD4)),
		background: StringExtensions.rgb(Color.FromArgb(0x2D, 0x2D, 0x2D)));

	/// <summary>
	/// Initializes a new instance of the RecursiveMarkdownRenderer
	/// </summary>
	/// <param name="maxWidth">Maximum width for rendered output. Tables will fit to this width with nice column spacing. Default is 78.</param>
	/// <param name="mushParser">
	/// Optional MUSH code parser used to apply syntax highlighting to
	/// <c>sharp</c> fenced code blocks. When <c>null</c>, those blocks are
	/// rendered as plain indented text.
	/// </param>
	public RecursiveMarkdownRenderer(int maxWidth = 78, IMUSHCodeParser? mushParser = null)
	{
		_maxWidth = maxWidth > 0 ? maxWidth : 78;
		_mushParser = mushParser;
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
		// Apply syntax highlighting to 'sharp' fenced code blocks when a parser is available.
		if (code is FencedCodeBlock fenced &&
			string.Equals(fenced.Info, "sharp", StringComparison.OrdinalIgnoreCase) &&
			_mushParser != null)
		{
			return MModule.markupSingle2(CodeBackgroundStyle, RenderSharpCodeBlock(fenced));
		}

		// Apply ColorCode syntax highlighting for fenced blocks with a recognised language tag.
		if (code is FencedCodeBlock fencedStd &&
			!string.IsNullOrWhiteSpace(fencedStd.Info) &&
			!string.Equals(fencedStd.Info, "sharp", StringComparison.OrdinalIgnoreCase))
		{
			var colored = TryRenderColorCodeBlock(fencedStd);
			if (colored is not null) return MModule.markupSingle2(CodeBackgroundStyle, colored);
		}

		var lines = code.Lines.Lines?
			.Where(line => line.Slice.Text != null)
			.Select(line => MModule.single("  " + line.Slice.ToString()))
			.ToList() ?? new List<MString>();

		return MModule.multipleWithDelimiter(MModule.single("\n"), lines);
	}

	/// <summary>
	/// Lays out a single code line using <c>align()</c>:
	/// a 1-wide left-gutter column plus a 1-char column separator gives the standard
	/// 2-char indent, while the content column is padded with spaces to fill the
	/// remaining render width so that the background colour spans the full terminal line.
	/// </summary>
	private MString AlignCodeLine(MString lineContent)
	{
		var contentWidth = Math.Max(1, _maxWidth - 2); // 1 (gutter col) + 1 (separator)
		return SharpMUSH.MarkupString.TextAlignerModule.align(
			$"1 <{contentWidth}",
			[MModule.empty(), lineContent],
			MModule.single(" "),    // filler = space
			MModule.single(" "),    // column separator = 1 space → total 2-char indent
			MModule.single("\n")    // row separator = newline (standard align behaviour; used when a long line wraps)
		);
	}

	/// <summary>
	/// Attempts to render a fenced code block using ColorCode.Core for ANSI syntax colouring.
	/// Returns <c>null</c> if the language is not recognised.
	/// Each source line is colourised independently then laid out via <see cref="AlignCodeLine"/>.
	/// </summary>
	private MString? TryRenderColorCodeBlock(FencedCodeBlock code)
	{
		var language = Languages.FindById(code.Info!);
		if (language is null) return null;

		var sourceLines = code.Lines.Lines?
			.Where(l => l.Slice.Text != null)
			.Select(l => l.Slice.ToString())
			.ToList() ?? [];

		if (sourceLines.Count == 0) return MModule.empty();

		var renderedLines = sourceLines.Select(line =>
		{
			var parts = new List<MString>();
			ColorCodeParser.Value.Parse(line, language, (text, scopes) =>
				WriteColorCodeScopes(text, scopes, parts));
			return AlignCodeLine(MModule.multiple(parts));
		}).ToList();

		return MModule.multipleWithDelimiter(MModule.single("\n"), renderedLines);
	}

	/// <summary>
	/// Mirrors the algorithm used by <c>HtmlFormatter.Write</c> in ColorCode.Core:
	/// uses <c>Scope.Index</c> and <c>Scope.Length</c> to slice <paramref name="text"/>
	/// into plain and coloured segments, recursing into <c>Scope.Children</c> for nested grammars.
	/// The callback <paramref name="text"/> is the FULL regex group-0 match, which can include
	/// structural delimiters (e.g. <c>{</c> before a JSON key). Only the sub-range identified
	/// by each scope carries a colour; everything else is emitted as plain text.
	/// </summary>
	private static void WriteColorCodeScopes(string text, IList<Scope> scopes, List<MString> parts)
	{
		var ordered = scopes.OrderBy(s => s.Index).ToList();
		int offset = 0;

		foreach (var scope in ordered)
		{
			// Plain text before this scope's range
			if (scope.Index > offset)
				parts.Add(MModule.single(text[offset..scope.Index]));

			var scopeText = text[scope.Index..(scope.Index + scope.Length)];

			if (scope.Children.Count > 0)
			{
				// Nested grammar: recurse so child scopes get their own colours.
				WriteColorCodeScopes(scopeText, scope.Children, parts);
			}
			else
			{
				var style = ColorCodeStyles.Contains(scope.Name) ? ColorCodeStyles[scope.Name] : null;
				// ColorCode foreground is #AARRGGBB (8-digit) or #RRGGBB (6-digit).
				var color = style?.Foreground is not null ? ParseArgbHex(style.Foreground) : null;
				if (color is not null && scope.Name != ScopeName.PlainText)
				{
					var ansiStyle = Ansi.Create(foreground: StringExtensions.rgb(color.Value), bold: style!.Bold);
					parts.Add(MModule.markupSingle(ansiStyle, scopeText));
				}
				else
				{
					parts.Add(MModule.single(scopeText));
				}
			}

			offset = scope.Index + scope.Length;
		}

		// Trailing plain text after all scopes
		if (offset < text.Length)
			parts.Add(MModule.single(text[offset..]));
	}

	/// <summary>
	/// Parses a CSS hex colour string produced by ColorCode (e.g. <c>#FFRRGGBB</c> or <c>#RRGGBB</c>)
	/// into a <see cref="Color"/>. Returns <c>null</c> on failure.
	/// </summary>
	private static Color? ParseArgbHex(string hex)
	{
		if (string.IsNullOrEmpty(hex) || hex[0] != '#') return null;
		try
		{
			return hex.Length switch
			{
				// #AARRGGBB — ColorCode DefaultDark emits this 8-digit ARGB format.
				// The alpha byte (positions 1–2) is discarded: ANSI terminal colours
				// do not support transparency, so only the RGB components are used.
				9 => Color.FromArgb(
					Convert.ToByte(hex.Substring(3, 2), 16),
					Convert.ToByte(hex.Substring(5, 2), 16),
					Convert.ToByte(hex.Substring(7, 2), 16)),
				// #RRGGBB
				7 => Color.FromArgb(
					Convert.ToByte(hex.Substring(1, 2), 16),
					Convert.ToByte(hex.Substring(3, 2), 16),
					Convert.ToByte(hex.Substring(5, 2), 16)),
				_ => null
			};
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// Renders a <c>sharp</c>-tagged fenced code block with MUSH semantic token colours.
	/// Each source line is tokenised independently so that multi-line code blocks
	/// are handled safely regardless of parser line-tracking behaviour.
	/// </summary>
	private MString RenderSharpCodeBlock(FencedCodeBlock code)
	{
		var sourceLines = code.Lines.Lines?
			.Where(l => l.Slice.Text != null)
			.Select(l => l.Slice.ToString())
			.ToList() ?? [];

		if (sourceLines.Count == 0)
			return MModule.empty();

		var renderedLines = sourceLines.Select(RenderSharpLine).ToList();
		return MModule.multipleWithDelimiter(MModule.single("\n"), renderedLines);
	}

	/// <summary>
	/// Applies MUSH semantic token colours to a single source line, then lays it out
	/// via <see cref="AlignCodeLine"/> so the background fills the full render width.
	/// </summary>
	private MString RenderSharpLine(string line)
	{
		var tokens = _mushParser!.GetSemanticTokens(MModule.single(line));
		var sortedTokens = tokens
			.OrderBy(t => t.Range.Start.Line)
			.ThenBy(t => t.Range.Start.Character)
			.ToList();

		if (sortedTokens.Count == 0)
			return AlignCodeLine(MModule.single(line));

		var parts = new List<MString>();
		foreach (var token in sortedTokens)
		{
			var style = SemanticTokenAnsiPalette.GetStyle(token.TokenType, token.Modifiers);
			parts.Add(style is null
				? MModule.single(token.Text)
				: MModule.markupSingle(style, token.Text));
		}
		return AlignCodeLine(MModule.multiple(parts));
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
		return string.IsNullOrEmpty(code.Content) ? MModule.empty() : RenderInlineCode(code);
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
	protected virtual MString RenderInlineCode(CodeInline code)
	{
		return MModule.single(code.Content);
	}

	private MString RenderLineBreak()
	{
		return MModule.single("\n");
	}

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
		var colorMatch = ColorAttributeRegex.Match(tag);
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
		var styleMatch = StyleAttributeRegex.Match(tag);
		if (styleMatch.Success)
		{
			var style = styleMatch.Groups[1].Value;

			// Parse color
			var colorMatch = StyleColorRegex.Match(style);
			var bgColorMatch = StyleBackgroundColorRegex.Match(style);

			var result = new StringBuilder();

			if (colorMatch.Success)
			{
				var fg = ParseColorValue(colorMatch.Groups[1].Value.Trim());
				if (fg.HasValue)
					result.Append(ANSILibrary.ANSI.Foreground(ANSILibrary.ANSI.AnsiColor.NewRGB(fg.Value)));
			}

			if (bgColorMatch.Success)
			{
				var bg = ParseColorValue(bgColorMatch.Groups[1].Value.Trim());
				if (bg.HasValue)
					result.Append(ANSILibrary.ANSI.Background(ANSILibrary.ANSI.AnsiColor.NewRGB(bg.Value)));
			}

			return result.ToString();
		}
		return "";
	}

	private string ParseColorTagToAnsiCode(string tag)
	{
		// Extract color value: <color red> or <color #FF0000>
		var match = ColorTagRegex.Match(tag);
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
			if (colorStr.Length == 7) // #RRGGBB
			{
				if (byte.TryParse(colorStr.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) &&
						byte.TryParse(colorStr.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
						byte.TryParse(colorStr.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
				{
					return Color.FromArgb(r, g, b);
				}
			}
			else if (colorStr.Length == 4) // #RGB - expand each digit
			{
				if (byte.TryParse(colorStr.AsSpan(1, 1), System.Globalization.NumberStyles.HexNumber, null, out var r) &&
						byte.TryParse(colorStr.AsSpan(2, 1), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
						byte.TryParse(colorStr.AsSpan(3, 1), System.Globalization.NumberStyles.HexNumber, null, out var b))
				{
					return Color.FromArgb((byte)(r * 17), (byte)(g * 17), (byte)(b * 17));
				}
			}

			return null;
		}

		// Named colors - Color.FromName doesn't throw, but returns invalid color if name doesn't exist
		var namedColor = Color.FromName(colorStr);
		return namedColor.IsKnownColor ? namedColor : null;
	}
}
