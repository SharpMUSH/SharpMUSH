using MarkupString.MarkupImplementation;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using Ansi = MarkupString.MarkupImplementation.AnsiMarkup;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Composes the softcode layout engine (<see cref="SoftcodeLayout"/>) and the shared semantic-token
/// renderer (<see cref="SemanticTokenRenderer"/>) into the one entry point <c>@examine</c> and
/// <c>@grep/PRINT</c> call to display formatted, coloured softcode. Owns no highlighting or layout
/// logic of its own — every rule about *what* colour a span gets or *where* a break may land lives in
/// one of those two services; this type only sequences them and stitches the results back together.
/// </summary>
public static class SoftcodeFormatter
{
	/// <summary>
	/// The style applied over a <see cref="ParseError"/>'s span, layered on top of (and taking
	/// precedence over) whatever <see cref="SemanticTokenAnsiPalette"/> would otherwise paint there —
	/// inverse video plus a red foreground, so a syntax error reads as a highlighted block rather than
	/// blending into ordinary syntax colouring.
	/// </summary>
	private static readonly Ansi ErrorStyle = AnsiCodeParser.ParseCodes("i r");

	/// <summary>
	/// Formats <paramref name="source"/> for display: colours it by <paramref name="semanticTokens"/>
	/// (with <paramref name="errors"/>' spans painted in <see cref="ErrorStyle"/> on top), wraps it to
	/// <paramref name="width"/> columns using <paramref name="tokens"/>, and appends a one-line-per-error
	/// summary beneath the code when <paramref name="errors"/> is non-empty.
	/// </summary>
	/// <param name="source">
	/// The full source text. May already carry author markup — spans are sliced out of it by offset
	/// (via <see cref="SemanticTokenRenderer"/> and <see cref="MModule.substring"/>) rather than
	/// reconstructed from token text, so that markup survives alongside the new colouring and breaks.
	/// </param>
	/// <param name="tokens">
	/// <paramref name="source"/> lexed to <see cref="TokenInfo"/> — the same shape
	/// <c>SoftcodeLayoutEquivalenceTests</c>' <c>TestLexer.Lex</c> and production's
	/// <c>MUSHCodeParser.Tokenize</c> produce. Drives <see cref="SoftcodeLayout.Compute"/>.
	/// </param>
	/// <param name="semanticTokens">Drives the colouring step; see <see cref="SemanticTokenRenderer.Render"/>.</param>
	/// <param name="errors">
	/// Parse errors to both highlight in the coloured source and summarise beneath it. <see cref="ParseError.Line"/>
	/// is 1-based, <see cref="ParseError.Column"/> is 0-based; an error with no <see cref="ParseError.OffendingToken"/>
	/// has no natural span length and is treated as a single character at its start offset.
	/// </param>
	/// <param name="width">Target line width in columns, forwarded to <see cref="SoftcodeLayout.Compute"/>.</param>
	/// <param name="parser">
	/// The parser to build a <see cref="SoftcodeLayout.ClassifierFor"/> classifier from. This is a
	/// deliberate deviation from the plan's original signature (which predated
	/// <see cref="SoftcodeLayout.ClassifierFor"/>): a classifier cannot be constructed without a parser,
	/// and formatting without one would silently fall back to <c>Compute</c>'s no-classifier default —
	/// every call treated as <see cref="SoftcodeCallKind.CopiesArgumentSource"/> — which defeats the
	/// tri-state safety analysis entirely rather than merely degrading it.
	/// </param>
	/// <param name="parseType">
	/// The dialect <paramref name="source"/> will be evaluated as, forwarded to
	/// <see cref="SoftcodeLayout.Compute"/> to decide whether a root <c>;</c> is a break position. Defaults
	/// to <see cref="ParseType.Function"/>, matching <c>Compute</c>'s own default.
	/// </param>
	public static MString Format(
		MString source,
		IReadOnlyList<TokenInfo> tokens,
		IReadOnlyList<SemanticToken> semanticTokens,
		IReadOnlyList<ParseError> errors,
		int width,
		IMUSHCodeParser parser,
		ParseType parseType = ParseType.Function)
	{
		var overrideAt = BuildErrorOverride(source, errors);
		var colored = SemanticTokenRenderer.Render(source, semanticTokens, overrideAt);

		var classifyFunction = SoftcodeLayout.ClassifierFor(parser);
		var breaks = SoftcodeLayout.Compute(tokens, width, classifyFunction: classifyFunction, parseType: parseType);

		var laidOut = ApplyBreaks(colored, tokens, breaks);

		return errors.Count == 0 ? laidOut : AppendErrorSummary(laidOut, errors);
	}

	/// <summary>
	/// Builds the <c>overrideAt</c> delegate <see cref="SemanticTokenRenderer.Render"/> consults per
	/// character offset, or <c>null</c> when there is nothing to override — preserving the fast,
	/// override-free path through <c>Render</c> for the common error-free call. Each
	/// <see cref="ParseError"/>'s 1-based line / 0-based column is converted to an absolute offset with
	/// the very line-start table <see cref="SemanticTokenRenderer"/> itself uses
	/// (<see cref="SemanticTokenRenderer.BuildLineStartTable"/> / <see cref="SemanticTokenRenderer.ToOffset"/>,
	/// both <c>internal</c> to this assembly for exactly this reuse) so there is only one such table in
	/// the tree.
	/// </summary>
	private static Func<int, Ansi?>? BuildErrorOverride(MString source, IReadOnlyList<ParseError> errors)
	{
		if (errors.Count == 0)
		{
			return null;
		}

		var lineStarts = SemanticTokenRenderer.BuildLineStartTable(MModule.plainText(source));
		var spans = errors
			.Select(error =>
			{
				var start = SemanticTokenRenderer.ToOffset(lineStarts, new Position(error.Line - 1, error.Column));
				var length = error.OffendingToken?.Length ?? 1;
				return (Start: start, End: start + length);
			})
			.ToList();

		return offset => spans.Any(span => offset >= span.Start && offset < span.End) ? ErrorStyle : null;
	}

	/// <summary>
	/// Applies <paramref name="breaks"/> to <paramref name="colored"/> by offset, walking
	/// <paramref name="tokens"/> — which tile <paramref name="colored"/> contiguously, the same
	/// assumption <c>SoftcodeRenderer</c> (the plain-text equivalent used by the layout equivalence
	/// corpus) relies on. A break trims the trailing whitespace the token absorbed and inserts <c>\n</c>
	/// plus <see cref="SoftcodeBreak.Indent"/> spaces, exactly as that plain-text renderer does — but
	/// sliced from the coloured <see cref="MString"/> rather than rebuilt from <see cref="TokenInfo.Text"/>,
	/// so styling and any author markup already in <paramref name="colored"/> survive the reflow.
	/// <para>
	/// Unlike <see cref="SemanticTokenRenderer.Render"/>, this emits only token spans — no gap-filling
	/// for text no token covers. That asymmetry is deliberate, not an oversight: <c>Render</c> must
	/// tolerate a token list that fails to tile its input because a caller could hand it any
	/// <c>IReadOnlyList&lt;SemanticToken&gt;</c>, but <paramref name="tokens"/> here always comes from a
	/// full lex of <paramref name="colored"/>'s own source — <c>MUSHCodeParser.Tokenize</c> in
	/// production, and <c>TestLexer.Lex</c>, which mirrors it, in the pure-unit layout tests. Both drop
	/// only the synthetic EOF token, so the tiling guarantee already holds. A future token source that
	/// doesn't tile would need this brought in line with <c>Render</c>'s gap handling; it isn't needed
	/// today.
	/// </para>
	/// <para>
	/// <b>That stream is not the one the evaluator parses.</b> <c>Tokenize</c> (<c>MUSHCodeParser.cs:648-681</c>)
	/// is the only lexing site in <c>MUSHCodeParser</c> that skips
	/// <c>RewriteOrphanedBracketClosers</c>/<c>RewriteOrphanedBraceClosers</c>; <c>ParseInternal</c>
	/// (<c>:353-354</c>), <c>CommandListParseVisitor</c> (<c>:531-532</c>), <c>ValidateAndGetErrors</c>
	/// (<c>:694-695</c>) and <c>GetSemanticTokens</c> (<c>:795-796</c>) all apply it. So a <c>]</c> or
	/// <c>}</c> that closes nothing is literal text everywhere except here, where it stays a closer
	/// token. <c>SoftcodeLayoutEquivalenceTests</c> lexes its corpus through <c>Tokenize</c> for exactly
	/// this reason.
	/// </para>
	/// <para>
	/// The divergence has <b>two</b> channels, and only the first is conservative. <b>Channel 1:</b>
	/// where the rewrite's flat depth count and <c>SoftcodeLayout</c>'s stack agree that a closer closes
	/// nothing, the only consequence is that <c>Layout</c>'s trailing-closer scan walks past it, lowering
	/// <c>lastContent</c>. Every break condition is <c>… &lt; lastContent</c>, so this yields strictly
	/// <b>fewer</b> breaks — safe, and reachable on input that parses (<c>strcat(aaaa…,])</c> breaks once
	/// here and would break twice on the evaluator's stream). <b>Channel 2:</b> the two counters can
	/// <em>desynchronise</em>, because the rewrite counts <c>[</c>/<c>]</c> globally while
	/// <c>BuildGroupTree</c> ignores a closer whose opener is not on top of its stack. A closer the flat
	/// count consumes but the stack does not leaves a later one orphaned for the evaluator and live here,
	/// so this engine pops a group the evaluator leaves open and a following comma becomes an argument
	/// separator — <b>more</b> breaks, in the unsafe direction. What keeps that channel out of reach is
	/// that it needs a <c>]</c> in a position the grammar, itself a stack machine
	/// (<c>bracketPattern</c> wants a complete <c>evaluationString</c> before its <c>CBRACK</c>), cannot
	/// accept — which is a syntax error. Verified: <c>strcat([strcat(a],b)],c)</c> takes that path here
	/// and is a <c>#-1 PARSER FAILURE</c> in the evaluator.
	/// </para>
	/// <para>
	/// <b>How firm that last step is.</b> It is a grammar argument shown on worked examples, <b>not</b>
	/// an exhaustive proof, and it deserves reading as "unreachable so far" rather than "impossible".
	/// The strongest evidence is external: a brute-force sweep of 531,441 inputs found every observed
	/// divergence to be channel 1 — conservative — and none in the unsafe direction. Anyone widening
	/// what the layout engine does with closer-typed tokens, or relying on channel 2 staying
	/// unreachable, should re-derive it rather than inherit it from here.
	/// </para>
	/// </summary>
	private static MString ApplyBreaks(MString colored, IReadOnlyList<TokenInfo> tokens, IReadOnlyList<SoftcodeBreak> breaks)
	{
		if (tokens.Count == 0)
		{
			return colored;
		}

		var indentByTokenIndex = breaks.ToDictionary(b => b.TokenIndex, b => b.Indent);
		var parts = new List<MString>(tokens.Count + breaks.Count);

		for (var i = 0; i < tokens.Count; i++)
		{
			var token = tokens[i];

			if (!indentByTokenIndex.TryGetValue(i, out var indent))
			{
				parts.Add(MModule.substring(token.StartIndex, token.Length, colored));
				continue;
			}

			var trimmedLength = token.Text.TrimEnd().Length;
			if (trimmedLength > 0)
			{
				parts.Add(MModule.substring(token.StartIndex, trimmedLength, colored));
			}

			parts.Add(MModule.single("\n" + new string(' ', indent)));
		}

		// ConcatMany (a single StringBuilder pass) via MModule.multiple — never MModule.concat in a
		// loop, which is O(n) per call and quadratic over a token list.
		return MModule.multiple(parts);
	}

	/// <summary>
	/// Appends a single newline followed by one line per error beneath the laid-out code — not a blank
	/// line: the summary starts on the very next line, each error rendered by
	/// <see cref="ParseError.ToMushFailureString"/> — the existing MUSH-facing formatter, reused rather
	/// than reinvented — and joined to the next by a further <c>\n</c>.
	/// </summary>
	private static MString AppendErrorSummary(MString laidOut, IReadOnlyList<ParseError> errors)
	{
		var summary = string.Join('\n', errors.Select(error => error.ToMushFailureString()));
		return MModule.multiple([laidOut, MModule.single("\n" + summary)]);
	}
}
