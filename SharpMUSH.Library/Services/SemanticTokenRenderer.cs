using SharpMUSH.Library.Models;
using Ansi = MarkupString.MarkupImplementation.AnsiMarkup;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Renders a source <see cref="MString"/> styled by a list of <see cref="SemanticToken"/>s.
/// This is the single "semantic tokens → styled MString" loop in the tree — extracted from
/// <c>RecursiveMarkdownRenderer.BuildSharpLineContent</c> so help-file code blocks and other
/// consumers (e.g. the <c>@examine</c> attribute-syntax formatter) share it instead of each
/// carrying their own copy.
/// </summary>
public static class SemanticTokenRenderer
{
	/// <summary>
	/// Applies palette styles to <paramref name="source"/> over each token's span.
	/// </summary>
	/// <param name="source">
	/// The full source text, already an <see cref="MString"/> (may already carry author markup,
	/// which is preserved because spans are sliced out of it by offset rather than reconstructed
	/// from <see cref="SemanticToken.Text"/>).
	/// </param>
	/// <param name="tokens">The semantic tokens describing spans of <paramref name="source"/>.</param>
	/// <param name="overrideAt">
	/// Consulted per character offset — not once per token — before falling back to the palette; a
	/// non-null result takes precedence over <see cref="SemanticTokenAnsiPalette"/>. Gap spans (text
	/// no token covers) consult it too. Used by callers that need to layer additional styling (e.g.
	/// syntax-error spans) over the semantic colours; those spans are exactly the characters most
	/// likely to fall in an untokenized gap or straddle a token boundary, so per-token consultation
	/// would not serve that use case.
	/// </param>
	/// <returns><paramref name="source"/> unstyled when <paramref name="tokens"/> is empty and <paramref name="overrideAt"/> is
	/// <c>null</c>. An empty <paramref name="tokens"/> list with a non-null <paramref name="overrideAt"/>
	/// still runs the render loop — the whole source is one gap span, and gap spans consult
	/// <paramref name="overrideAt"/> too — which matters to a caller like <c>SoftcodeFormatter</c>
	/// highlighting a parse error over source that produced no semantic tokens at all (e.g. input
	/// that failed to parse before any token could be classified).</returns>
	public static MString Render(
		MString source,
		IReadOnlyList<SemanticToken> tokens,
		Func<int, Ansi?>? overrideAt = null)
	{
		if (tokens.Count == 0 && overrideAt is null)
			return source;

		var lineStarts = BuildLineStartTable(MModule.plainText(source));

		var sortedTokens = tokens
			.OrderBy(t => t.Range.Start.Line)
			.ThenBy(t => t.Range.Start.Character)
			.ToList();

		// Walk the token list left to right, emitting an unstyled span for any gap the tokens
		// don't cover (before the first token, between tokens, after the last) so that a token
		// list which fails to tile the input never loses characters — a deliberate divergence
		// from the loop this was lifted from, which assumed perfect tiling.
		var parts = new List<MString>(sortedTokens.Count * 2 + 1);
		var cursor = 0;
		foreach (var token in sortedTokens)
		{
			var tokenStart = ToOffset(lineStarts, token.Range.Start);
			var tokenEnd = ToOffset(lineStarts, token.Range.End);

			// A token entirely consumed by a preceding (overlapping) token contributes nothing.
			// The renderer must not depend on the LSP non-overlapping-token convention.
			if (tokenEnd <= cursor)
				continue;

			// Clamp instead of trusting tokenStart: an overlapping token's covered prefix was
			// already emitted by an earlier token and must not be sliced out (and styled) again.
			var start = Math.Max(tokenStart, cursor);

			if (start > cursor)
				EmitStyledRuns(source, cursor, start, null, overrideAt, parts);

			var baseStyle = SemanticTokenAnsiPalette.GetStyle(token.TokenType, token.Modifiers);
			EmitStyledRuns(source, start, tokenEnd, baseStyle, overrideAt, parts);

			cursor = tokenEnd;
		}

		var totalLength = MModule.getLength(source);
		if (cursor < totalLength)
			EmitStyledRuns(source, cursor, totalLength, null, overrideAt, parts);

		// ConcatMany (a single StringBuilder pass) — never MModule.concat in a loop, which is
		// O(n) per call and quadratic over a token list.
		return MModule.multiple(parts);
	}

	/// <summary>
	/// Emits one <see cref="MString"/> piece per contiguous run of <c>source[start, end)</c> that
	/// shares the same effective style, where the effective style at an offset is
	/// <c>overrideAt(offset) ?? baseStyle</c>. When <paramref name="overrideAt"/> is <c>null</c> the
	/// whole range shares <paramref name="baseStyle"/> and is emitted as a single piece — the common,
	/// override-free path (help-file rendering) costs no extra per-character calls and produces
	/// exactly the same number of pieces as before this fix. When an override is supplied, its answer
	/// is sampled once per character to find the offsets where it changes, but only one piece is
	/// emitted per run — never one piece per character, which would defeat <c>ConcatMany</c>.
	/// </summary>
	private static void EmitStyledRuns(
		MString source, int start, int end, Ansi? baseStyle, Func<int, Ansi?>? overrideAt, List<MString> parts)
	{
		if (end <= start)
			return;

		if (overrideAt is null)
		{
			EmitRun(source, start, end, baseStyle, parts);
			return;
		}

		var runStart = start;
		var runStyle = overrideAt(start) ?? baseStyle;
		for (var offset = start + 1; offset < end; offset++)
		{
			var style = overrideAt(offset) ?? baseStyle;
			if (!StylesEqual(style, runStyle))
			{
				EmitRun(source, runStart, offset, runStyle, parts);
				runStart = offset;
				runStyle = style;
			}
		}

		EmitRun(source, runStart, end, runStyle, parts);
	}

	/// <summary>
	/// Structural equality for run-merging. <see cref="Ansi"/> (<c>AnsiMarkup</c>) is a plain class
	/// with no <c>Equals</c> override (<c>SharpMUSH.MarkupString/Markup/Markup.cs</c>), so comparing
	/// the markup instances themselves would be reference equality — which happens to hold today
	/// because <see cref="SemanticTokenAnsiPalette.GetStyle"/> is called once per token and the same
	/// instance is reused across its characters, but a caller-supplied <c>overrideAt</c> is not
	/// obligated to do the same, and an <c>overrideAt</c> that allocates a fresh style per offset (the
	/// natural way to write one) would otherwise re-fragment into one run per character. Comparing
	/// <see cref="AnsiMarkup.Details"/> (a <c>readonly record struct</c>) instead makes run-merging
	/// depend on content, not identity, so it holds regardless of how a caller's override is written.
	/// </summary>
	private static bool StylesEqual(Ansi? a, Ansi? b) => Equals(a?.Details, b?.Details);

	private static void EmitRun(MString source, int start, int end, Ansi? style, List<MString> parts)
	{
		var span = MModule.substring(start, end - start, source);
		parts.Add(style is null ? span : MModule.MarkupSingle2(style, span));
	}

	/// <summary>
	/// Builds a line-start offset table over <paramref name="plainText"/>: <c>result[i]</c> is the
	/// absolute character offset of the first character of (zero-based) line <c>i</c>. Consumed by
	/// <see cref="ToOffset"/> to convert an LSP-style <see cref="Position"/> (line/character) into
	/// an absolute offset. Attribute values can contain embedded newlines (PR #775, Softcode Editor
	/// newline storage), so this table must not assume single-line input.
	/// </summary>
	internal static int[] BuildLineStartTable(string plainText)
	{
		var starts = new List<int> { 0 };
		for (var i = 0; i < plainText.Length; i++)
		{
			if (plainText[i] == '\n')
				starts.Add(i + 1);
		}
		return [.. starts];
	}

	/// <summary>
	/// Converts a <see cref="Position"/> (line/character) to an absolute offset using a table built
	/// by <see cref="BuildLineStartTable"/>.
	/// </summary>
	internal static int ToOffset(int[] lineStarts, Position position)
	{
		var line = Math.Clamp(position.Line, 0, lineStarts.Length - 1);
		return lineStarts[line] + position.Character;
	}
}
