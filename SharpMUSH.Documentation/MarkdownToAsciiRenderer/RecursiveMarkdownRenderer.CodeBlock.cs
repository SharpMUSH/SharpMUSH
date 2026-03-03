using ANSILibrary;
using ColorCode;
using ColorCode.Common;
using ColorCode.Parsing;
using Markdig.Syntax;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.MarkupString;
using System.Drawing;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public partial class RecursiveMarkdownRenderer
{
	protected virtual MString RenderCodeBlock(CodeBlock code)
	{
		// Apply syntax highlighting to 'sharp' fenced code blocks when a parser is available.
		// Background colour is applied per-line inside RenderSharpCodeBlock so that each
		// MUSH line carries its own ANSI start/reset (MUSH clients reset ANSI on \r\n).
		if (code is FencedCodeBlock fenced &&
			string.Equals(fenced.Info, "sharp", StringComparison.OrdinalIgnoreCase) &&
			_mushParser != null)
		{
			return RenderSharpCodeBlock(fenced);
		}

		// Apply ColorCode syntax highlighting for fenced blocks with a recognised language tag.
		if (code is FencedCodeBlock fencedStd &&
			!string.IsNullOrWhiteSpace(fencedStd.Info) &&
			!string.Equals(fencedStd.Info, "sharp", StringComparison.OrdinalIgnoreCase))
		{
			var colored = TryRenderColorCodeBlock(fencedStd);
			if (colored is not null) return MModule.markupSingle2(CodeBackgroundStyle, colored);
		}

		// Apply background styling to unlabeled fenced code blocks so they are visually
		// distinct from regular prose, consistent with labelled code blocks.
		if (code is FencedCodeBlock fencedPlain && string.IsNullOrWhiteSpace(fencedPlain.Info))
		{
			var bgLines = fencedPlain.Lines.Lines?
				.Where(line => line.Slice.Text != null)
				.Select(line => MModule.single(line.Slice.ToString()))
				.ToList() ?? new List<MString>();
			if (bgLines.Count == 0) return MModule.empty();
			return MModule.markupSingle2(CodeBackgroundStyle, AlignAllCodeLines(bgLines));
		}

		var lines = code.Lines.Lines?
			.Where(line => line.Slice.Text != null)
			.Select(line => MModule.single("  " + line.Slice.ToString()))
			.ToList() ?? new List<MString>();

		return MModule.multipleWithDelimiter(MModule.single("\n"), lines);
	}

	/// <summary>
	/// Lays out all code lines at once using <c>align()</c>:
	/// a 1-wide left-gutter column plus a 1-char column separator gives the standard
	/// 2-char indent, while the content column is padded with spaces to fill the
	/// remaining render width so that the background colour spans the full terminal line.
	/// All rows are handled in a single call — <c>align()</c> splits on <c>\n</c> internally,
	/// so there is no need to iterate line-by-line in the caller.
	/// </summary>
	private MString AlignAllCodeLines(IEnumerable<MString> lineContents)
	{
		var contentWidth = Math.Max(1, _maxWidth - 2); // 1 (gutter col) + 1 (separator)
		var allContent = MModule.multipleWithDelimiter(MModule.single("\n"), lineContents);
		return TextAlignerModule.align(
			$"1 <{contentWidth}",
			[MModule.empty(), allContent],
			MModule.single(" "),    // filler = space
			MModule.single(" "),    // column separator = 1 space → total 2-char indent
			MModule.single("\n")    // row separator used when a long line wraps
		);
	}

	/// <summary>
	/// Attempts to render a fenced code block using ColorCode.Core for ANSI syntax colouring.
	/// Returns <c>null</c> if the language is not recognised.
	/// Each source line is colourised independently; all colourised lines are then laid out
	/// via a single <see cref="AlignAllCodeLines"/> call.
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

		var coloredLines = sourceLines.Select(line =>
		{
			var parts = new List<MString>();
			ColorCodeParser.Value.Parse(line, language, (text, scopes) =>
				WriteColorCodeScopes(text, scopes, parts));
			return MModule.multiple(parts);
		});

		return AlignAllCodeLines(coloredLines);
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
		var offset = 0;

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
	/// Each source line is colourised independently and wrapped in its own
	/// <see cref="CodeBackgroundStyle"/> ANSI span, so that MUSH clients which reset ANSI
	/// attributes on <c>\r\n</c> line endings still display the background on every line.
	/// </summary>
	private MString RenderSharpCodeBlock(FencedCodeBlock code)
	{
		var sourceLines = code.Lines.Lines?
			.Where(l => l.Slice.Text != null)
			.Select(l => l.Slice.ToString())
			.ToList() ?? [];

		if (sourceLines.Count == 0)
			return MModule.empty();

		var contentWidth = Math.Max(1, _maxWidth - 2);
		var styledLines = sourceLines.Select(line =>
		{
			var content = BuildSharpLineContent(line);
			var aligned = TextAlignerModule.align(
				$"1 <{contentWidth}",
				[MModule.empty(), content],
				MModule.single(" "),
				MModule.single(" "),
				MModule.single("\n")
			);
			return MModule.markupSingle2(CodeBackgroundStyle, aligned);
		});

		return MModule.multipleWithDelimiter(MModule.single("\n"), styledLines);
	}

	/// <summary>
	/// Applies MUSH semantic token colours to a single source line and returns the colourised
	/// <see cref="MString"/> without any layout/alignment applied.
	/// Alignment and background styling are applied per-line by <see cref="RenderSharpCodeBlock"/>.
	/// </summary>
	/// <remarks>
	/// Parse type is auto-detected: lines whose first non-whitespace character is
	/// <c>&amp;</c> (attribute), <c>@</c> (command), or <c>$</c> (trigger pattern) are
	/// command-list lines and are passed to the parser as <see cref="ParseType.CommandList"/>;
	/// everything else is treated as a function expression (<see cref="ParseType.Function"/>).
	/// </remarks>
	private MString BuildSharpLineContent(string line)
	{
		var trimmed = line.TrimStart();

		// Strip "> " prompt prefix commonly used in helpfile code examples so
		// the parse-type detection and tokeniser see the actual MUSH code.
		var promptPrefix = string.Empty;
		if (trimmed.StartsWith("> "))
		{
			var leadingSpaces = line.Length - trimmed.Length;
			promptPrefix = line[..(leadingSpaces + 2)]; // leading whitespace + "> "
			line = line[promptPrefix.Length..];
			trimmed = trimmed[2..];
		}

		var parseType = trimmed.Length > 0 && (trimmed[0] == '&' || trimmed[0] == '@' || trimmed[0] == '$')
			? ParseType.CommandList
			: ParseType.Function;
		var tokens = _mushParser!.GetSemanticTokens(MModule.single(line), parseType);
		var sortedTokens = tokens
			.OrderBy(t => t.Range.Start.Line)
			.ThenBy(t => t.Range.Start.Character)
			.ToList();

		if (sortedTokens.Count == 0)
			return promptPrefix.Length > 0
				? MModule.concat(MModule.single(promptPrefix), MModule.single(line))
				: MModule.single(line);

		var parts = new List<MString>();
		if (promptPrefix.Length > 0)
			parts.Add(MModule.single(promptPrefix));
		foreach (var token in sortedTokens)
		{
			var style = SemanticTokenAnsiPalette.GetStyle(token.TokenType, token.Modifiers);
			parts.Add(style is null
				? MModule.single(token.Text)
				: MModule.markupSingle(style, token.Text));
		}
		return MModule.multiple(parts);
	}
}
