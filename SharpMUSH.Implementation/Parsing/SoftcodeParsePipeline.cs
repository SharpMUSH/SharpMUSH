using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Misc;
using SharpMUSH.Library.ParserInterfaces;
using System.Runtime.InteropServices;

namespace SharpMUSH.Implementation.Parsing;

/// <summary>
/// The one lexer → token stream → parser setup every parse goes through, evaluation and tooling
/// alike, and the one mapping from a <see cref="ParseType"/> to the grammar rule that starts it.
/// </summary>
/// <remarks>
/// Every member is static and allocates only what a parse needs: the input stream, the lexer, the
/// buffered token stream and the parser.
/// </remarks>
internal static class SoftcodeParsePipeline
{
	/// <summary>
	/// Deepest nesting of <c>[]</c>, <c>{}</c>, and function-call <c>()</c> the parser will
	/// attempt. The generated parser is recursive descent, so each level becomes a native stack
	/// frame with no depth check of its own; a deeply enough nested input overflows the stack and
	/// takes the whole process down with an uncatchable <see cref="StackOverflowException"/> —
	/// during parsing, before any evaluation-time limit (CallLimit, FunctionInvocationLimit) can
	/// act. Direct player input is not length-capped (the telnet line buffer is megabytes), so a
	/// single line of brackets is a remote denial of service against every connected player.
	/// <para>
	/// Refusing to parse past this depth is the structural analogue of PennMUSH's call_limit,
	/// which likewise stops descending into <c>{</c>/<c>[</c>/<c>(</c> to protect the C stack
	/// (src/parse.c). Fixed rather than configurable so an operator cannot raise it back into the
	/// crash range. Wide margin: the observed overflow is above ~11000 levels, real softcode nests
	/// a few dozen deep, and PennMUSH ships call_limit at 100.
	/// </para>
	/// </summary>
	public const int MaxParseNestingDepth = 1000;

	/// <summary>
	/// Lexes <paramref name="plainText"/> into a filled token stream.
	/// </summary>
	/// <param name="plainText">The text to lex, already stripped of markup.</param>
	/// <param name="sourceName">The name the input stream reports, which is the calling entry point.</param>
	/// <param name="rewriteOrphanedClosers">
	/// Whether to turn closers with no opener into literal text (see
	/// <see cref="RewriteOrphanedBracketClosers"/>). Every parse does; only <c>Tokenize</c>, which
	/// reports raw lexer tokens, does not.
	/// </param>
	public static BufferedTokenSpanStream Lex(string plainText, string sourceName, bool rewriteOrphanedClosers = true)
	{
		StringSpanInputStream inputStream = new(plainText, sourceName);
		var lexer = CreateLexer(inputStream);
		BufferedTokenSpanStream tokens = new(lexer);
		tokens.Fill();
		if (rewriteOrphanedClosers)
		{
			RewriteOrphanedBracketClosers(tokens);
			RewriteOrphanedBraceClosers(tokens);
		}

		return tokens;
	}

	/// <summary>
	/// Builds the lexer for one parse. Recognizers are constructed with ANTLR's
	/// <c>ConsoleErrorListener</c> attached; the parser's is swapped for a collecting listener at
	/// each call site, but the lexer's was left in place, so anything it disliked printed to the
	/// server's stdout instead of reaching the player. Nothing consumes lexer diagnostics — the
	/// grammar's catch-all rules make token recognition total — so the listener is simply removed.
	/// </summary>
	private static SharpMUSHLexer CreateLexer(StringSpanInputStream inputStream)
	{
		var lexer = new SharpMUSHLexer(inputStream)
		{
			TokenFactory = OptimizedTokenFactory.Default
		};
		lexer.RemoveErrorListeners();

		return lexer;
	}

	/// <summary>
	/// A parser over <paramref name="tokens"/> with no error listeners. Callers attach the listener
	/// and error strategy their pass needs.
	/// </summary>
	/// <param name="resolvePredicates">
	/// Predict with <see cref="PredicateResolvingSimulator"/>, which settles the grammar's predicates
	/// before the lookahead instead of after it. Without it, a large bracketed expression in a later
	/// argument (<c>if(c,A,[...])</c>) takes time exponential in the calls inside the bracket. The
	/// SLL pass of two-stage prediction uses it; the LL pass that reports a syntax error does not,
	/// so its errors and recovery stay ANTLR's own.
	/// </param>
	public static SharpMUSHParser CreateParser(BufferedTokenSpanStream tokens, bool parenGroups, PredictionMode mode,
		bool trace = false, bool resolvePredicates = false)
	{
		var parser = new SharpMUSHParser(tokens)
		{
			parenGroups = parenGroups,
			Trace = trace
		};
		if (resolvePredicates)
		{
			parser.ResolvePredicatesAtDecisionStart();
		}

		parser.Interpreter.PredictionMode = mode;
		parser.RemoveErrorListeners();

		return parser;
	}

	/// <summary>
	/// The SLL pass of two-stage prediction, for a <paramref name="parser"/> built with SLL and
	/// <c>resolvePredicates</c>: runs <paramref name="entryPoint"/> under a
	/// <see cref="BailErrorStrategy"/> and returns the tree only if nothing went wrong. A tree from
	/// here is the one LL would build, so it stands; on <see langword="null"/>, the caller seeks the
	/// tokens back to the start and parses again with LL, which decides whether the input really has
	/// a syntax error and reports it.
	/// </summary>
	public static TContext? ParseClean<TContext>(SharpMUSHParser parser, Func<SharpMUSHParser, TContext> entryPoint,
		ParserErrorListener errors) where TContext : ParserRuleContext
	{
		parser.ErrorHandler = new BailErrorStrategy();
		parser.AddErrorListener(errors);
		try
		{
			var context = entryPoint(parser);
			return errors.HasErrors ? null : context;
		}
		catch (ParseCanceledException)
		{
			return null;
		}
	}

	/// <summary>
	/// Runs the grammar rule that starts a parse of <paramref name="parseType"/>. Every one of these
	/// rules is anchored at EOF, so a successful parse consumed the whole input.
	/// </summary>
	public static ParserRuleContext Enter(SharpMUSHParser parser, ParseType parseType) => parseType switch
	{
		ParseType.Command => parser.startSingleCommandString(),
		ParseType.CommandList => parser.startCommandString(),
		ParseType.CommandSingleArg => parser.startPlainSingleCommandArg(),
		ParseType.CommandCommaArgs => parser.startPlainCommaCommandArgs(),
		ParseType.CommandEqSplitArgs => parser.startEqSplitCommandArgs(),
		ParseType.CommandEqSplit => parser.startEqSplitCommand(),
		_ => parser.startPlainString()
	};

	/// <summary>
	/// Whether the token stream nests recursion-causing delimiters — <c>[</c>, <c>{</c>, and a
	/// function-call <c>name(</c> — deeper than <paramref name="limit"/>. These are exactly the
	/// three constructs whose parser rules recurse (<c>bracketPattern</c>, <c>bracePattern</c>,
	/// <c>function</c>); a bare <c>(</c> is plain text and does not open a rule, so it is tracked
	/// only to match its closing <c>)</c> and never counts toward the depth. Escaped delimiters
	/// never reach here as openers — the lexer emits <c>ESCAPE</c> + <c>ANY</c> for <c>\[</c> — so
	/// they add no depth, matching what the parser would have done.
	/// </summary>
	public static bool ExceedsNestingLimit(BufferedTokenSpanStream tokenStream, int limit, out IToken? offendingToken)
	{
		offendingToken = null;
		// Most lines open nothing at all, so the matching stack exists only once one does.
		Stack<char>? open = null;
		var depth = 0;

		foreach (var token in CollectionsMarshal.AsSpan(tokenStream.tokens))
		{
			switch (token.Type)
			{
				case SharpMUSHLexer.OBRACK:
					(open ??= new Stack<char>()).Push('[');
					if (++depth > limit) { offendingToken = token; return true; }
					break;
				case SharpMUSHLexer.OBRACE:
					(open ??= new Stack<char>()).Push('{');
					if (++depth > limit) { offendingToken = token; return true; }
					break;
				case SharpMUSHLexer.FUNCHAR:
					(open ??= new Stack<char>()).Push('(');
					if (++depth > limit) { offendingToken = token; return true; }
					break;
				case SharpMUSHLexer.OPAREN:
					(open ??= new Stack<char>()).Push('o');
					break;
				case SharpMUSHLexer.CBRACK:
					if (open is not null && open.TryPeek(out var b) && b == '[') { open.Pop(); depth--; }
					break;
				case SharpMUSHLexer.CBRACE:
					if (open is not null && open.TryPeek(out var c) && c == '{') { open.Pop(); depth--; }
					break;
				case SharpMUSHLexer.CPAREN:
					if (open is not null && open.TryPeek(out var p) && p is '(' or 'o')
					{
						if (p == '(') depth--;
						open.Pop();
					}
					break;
			}
		}

		return false;
	}

	/// <summary>
	/// Scans the token stream for escaped bracket openers (\[) and converts
	/// their matching orphaned CBRACK closers to OTHER tokens, preventing
	/// parser errors on unmatched brackets.
	///
	/// When the lexer encounters \[, it produces ESCAPE + ANY (not OBRACK),
	/// so inBracketDepth never increments. The matching ] still becomes CBRACK
	/// with no open bracketPattern to close, causing a syntax error.
	/// This method fixes that by converting orphaned CBRACKs to OTHER.
	///
	/// The algorithm tracks real bracket depth to avoid converting CBRACKs
	/// that close real bracket patterns. An escaped bracket inside a real
	/// bracket (e.g., [reglattr(%!/\[0-9\]+)]) is correctly ignored.
	/// </summary>
	public static void RewriteOrphanedBracketClosers(BufferedTokenSpanStream tokenStream)
	{
		var tokens = tokenStream.tokens;
		var depth = 0;

		for (var i = 0; i < tokens.Count; i++)
		{
			var token = tokens[i];

			if (token.Type == SharpMUSHLexer.OBRACK)
			{
				depth++;
			}
			else if (token.Type == SharpMUSHLexer.CBRACK)
			{
				if (depth > 0)
				{
					depth--;
				}
				else if (token is IWritableToken writable)
				{
					// Orphaned CBRACK at depth 0 — treat as literal ']'
					writable.Type = SharpMUSHLexer.OTHER;
				}
			}
		}
	}

	public static void RewriteOrphanedBraceClosers(BufferedTokenSpanStream tokenStream)
	{
		var tokens = tokenStream.tokens;
		var depth = 0;

		for (var i = 0; i < tokens.Count; i++)
		{
			var token = tokens[i];

			if (token.Type == SharpMUSHLexer.OBRACE)
			{
				depth++;
			}
			else if (token.Type == SharpMUSHLexer.CBRACE)
			{
				if (depth > 0)
				{
					depth--;
				}
				else if (token is IWritableToken writable)
				{
					// Orphaned CBRACE at depth 0 — treat as literal '}'
					writable.Type = SharpMUSHLexer.OTHER;
				}
			}
		}
	}
}
