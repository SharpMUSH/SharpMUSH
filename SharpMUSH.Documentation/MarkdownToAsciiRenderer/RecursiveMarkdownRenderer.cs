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
using SharpMUSH.MarkupString;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

/// <summary>
/// Recursive renderer core: dispatch, document, and inline traversal.
/// </summary>
public partial class RecursiveMarkdownRenderer
{
	private readonly Ansi _dimStyle = Ansi.Create(faint: true);
	private readonly Ansi _boldStyle = Ansi.Create(foreground: StringExtensions.rgb(Color.White), bold: true);
	private readonly Ansi _underlineStyle = Ansi.Create(underlined: true);
	private readonly Ansi _headingStyle = Ansi.Create(foreground: StringExtensions.rgb(Color.White), underlined: true, bold: true);
	private readonly Ansi _heading3Style = Ansi.Create(foreground: StringExtensions.rgb(Color.White), underlined: true);
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
		var compiler = new LanguageCompiler([], _colorCodeLock);
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

	// Light-blue colour applied to inline code spans (`...`).
	// #9CDCFE matches VS Code Dark+'s variable/property colour and reads well on dark terminals.
	private static readonly Ansi InlineCodeStyle = Ansi.Create(
		foreground: StringExtensions.rgb(Color.FromArgb(0x9C, 0xDC, 0xFE)));

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
			LineBreakInline lb => RenderLineBreak(lb),
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

	private static bool IsNonWhitespace(MString rendered)
		=> rendered.Length > 0 && !string.IsNullOrWhiteSpace(rendered.ToPlainText());

	private MString RenderContainerBlock(ContainerBlock container)
	{
		var parts = container
			.Select(child => Render(child))
			.Where(IsNonWhitespace)
			.ToList();
		return MModule.multipleWithDelimiter(MModule.single("\n"), parts);
	}

	private MString RenderDocument(MarkdownDocument doc)
	{
		// EnableTrackTrivia records blank lines from the source as LinesBefore /
		// LinesAfter trivia on each block. Use that to reproduce the original
		// vertical spacing: a single "\n" always separates adjacent blocks, plus
		// one extra "\n" for every blank line the author placed between them.
		var items = doc
			.Select(child => (block: child, rendered: Render(child)))
			.Where(x => IsNonWhitespace(x.rendered))
			.ToList();

		if (items.Count == 0) return MModule.empty();

		var result = new List<MString> { items[0].rendered };
		for (var i = 1; i < items.Count; i++)
		{
			var blankLines = (items[i - 1].block.LinesAfter?.Count ?? 0)
			               + (items[i].block.LinesBefore?.Count ?? 0);
			var delimiter = "\n" + new string('\n', blankLines);
			result.Add(MModule.single(delimiter));
			result.Add(items[i].rendered);
		}

		return MModule.multiple(result);
	}

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
	=> RenderInlines(container.FirstChild);

	// Soft line breaks (IsHard=false) are word-wrap points in Markdown and should
	// render as spaces in terminal output, not newlines. EnableTrackTrivia appends
	// them to every paragraph; rendering them as "\n" would produce double-newlines
	// between document blocks. Only hard breaks (two trailing spaces or backslash)
	// produce actual newlines.
	private MString RenderLineBreak(LineBreakInline lineBreak)
	=> lineBreak.IsHard ? MModule.single("\n") : MModule.single(" ");
}
