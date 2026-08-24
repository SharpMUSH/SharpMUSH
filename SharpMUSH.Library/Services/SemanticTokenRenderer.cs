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
	/// Consulted with each token's start offset before falling back to the palette; a non-null
	/// result takes precedence over <see cref="SemanticTokenAnsiPalette"/>. Used by callers that
	/// need to layer additional styling (e.g. error spans) over the semantic colours.
	/// </param>
	/// <returns><paramref name="source"/> unstyled when <paramref name="tokens"/> is empty.</returns>
	public static MString Render(
		MString source,
		IReadOnlyList<SemanticToken> tokens,
		Func<int, Ansi?>? overrideAt = null)
	{
		if (tokens.Count == 0)
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
			var start = ToOffset(lineStarts, token.Range.Start);
			var end = ToOffset(lineStarts, token.Range.End);

			if (start > cursor)
				parts.Add(MModule.substring(cursor, start - cursor, source));

			if (end > start)
			{
				var span = MModule.substring(start, end - start, source);
				var style = overrideAt?.Invoke(start) ?? SemanticTokenAnsiPalette.GetStyle(token.TokenType, token.Modifiers);
				parts.Add(style is null ? span : MModule.MarkupSingle2(style, span));
			}

			cursor = Math.Max(cursor, end);
		}

		var totalLength = MModule.getLength(source);
		if (cursor < totalLength)
			parts.Add(MModule.substring(cursor, totalLength - cursor, source));

		// ConcatMany (a single StringBuilder pass) — never MModule.concat in a loop, which is
		// O(n) per call and quadratic over a token list.
		return MModule.multiple(parts);
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
