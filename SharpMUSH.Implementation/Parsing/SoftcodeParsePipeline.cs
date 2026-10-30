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
/// Every member is static. Each thread keeps one lexer and one parser per paren_groups setting and
/// hands them each new input, since building them cost more than parsing most inputs; a parse
/// allocates only the input stream, the buffered token stream and the tree.
/// </remarks>
internal static class SoftcodeParsePipeline
{
	// Taken out of the slot while in use, so a parse that somehow started inside another one gets
	// fresh instances rather than sharing these.
	[ThreadStatic] private static SharpMUSHLexer? t_lexer;
	[ThreadStatic] private static SharpMUSHParser? t_parser;
	[ThreadStatic] private static SharpMUSHParser? t_parenGroupsParser;

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
	/// <param name="heads">For a command or command list: the built-ins whose heads are marked (see
	/// <see cref="MarkCommandHeads"/>). Null leaves every command a plain string.</param>
	/// <param name="commandList">Whether a <c>;</c> outside any bracket, brace, call or name starts another
	/// command, as it does in a command list.</param>
	/// <param name="plainText">The text to lex, already stripped of markup.</param>
	/// <param name="sourceName">The name the input stream reports, which is the calling entry point.</param>
	/// <param name="rewriteOrphanedClosers">
	/// Whether to turn closers with no opener into literal text (see
	/// <see cref="RewriteOrphanedBracketClosers"/>). Every parse does; only <c>Tokenize</c>, which
	/// reports raw lexer tokens, does not.
	/// </param>
	public static BufferedTokenSpanStream Lex(string plainText, string sourceName, bool rewriteOrphanedClosers = true,
		ICommandHeads? heads = null, bool commandList = false)
	{
		ExecutionBudget.Current?.ThrowIfExceeded();
		StringSpanInputStream inputStream = new(plainText, sourceName);
		var lexer = t_lexer;
		t_lexer = null;
		BufferedTokenSpanStream tokens;
		try
		{
			// A token keeps the input it came from, so the lexer is free for the next input once the
			// stream is filled to EOF.
			if (lexer is null)
			{
				lexer = CreateLexer(inputStream);
			}
			else
			{
				lexer.SetInputStream(inputStream);
			}

			tokens = new BufferedTokenSpanStream(lexer);
			tokens.Fill();

			if (rewriteOrphanedClosers)
			{
				RewriteOrphanedBracketClosers(tokens);
				RewriteOrphanedBraceClosers(tokens);
			}

			if (heads is not null)
			{
				MarkCommandHeads(tokens, lexer, inputStream, heads, commandList);
			}
		}
		finally
		{
			t_lexer = lexer;
		}

		return tokens;
	}

	/// <summary>
	/// Puts a <c>CMD_*</c> token in place of the name, switches and following spaces of each command
	/// whose name is a built-in <paramref name="heads"/> knows, so the command grammar reads its
	/// arguments the way that built-in splits them, in the same parse as the line.
	/// <para>
	/// A command is marked only when its name is the start of an <c>OTHER</c> run followed by a space,
	/// or the whole run when nothing follows it; anything else (<c>addcom=x</c>, <c>think[x]</c>, a
	/// name that starts with a token of its own) is left a plain string, which dispatch splits from its
	/// text as before. <c>OTHER</c> runs through spaces, so the text from the first argument character
	/// is lexed again, up to where its tokens meet the old ones.
	/// </para>
	/// <para>
	/// A <c>;</c> is taken as a separator when no bracket, brace, call or name is open. A wrong guess
	/// costs only a reparse: a marked parse that fails is parsed again unmarked.
	/// </para>
	/// </summary>
	private static void MarkCommandHeads(BufferedTokenSpanStream stream, SharpMUSHLexer lexer,
		StringSpanInputStream input, ICommandHeads heads, bool commandList)
	{
		var tokens = stream.tokens;
		var marked = false;
		var depth = 0;
		Stack<int>? open = null;
		var commandStart = true;

		for (var i = 0; i < tokens.Count; i++)
		{
			if (commandStart)
			{
				commandStart = false;
				if (MarkCommandHead(tokens, i, lexer, input, heads, commandList))
				{
					marked = true;
					continue;
				}
			}

			var type = tokens[i].Type;
			switch (type)
			{
				case SharpMUSHLexer.OBRACK or SharpMUSHLexer.OBRACE or SharpMUSHLexer.FUNCHAR
					or SharpMUSHLexer.REG_STARTCARET or SharpMUSHLexer.ARG_STARTCARET or SharpMUSHLexer.ATTR_STARTCARET
					or SharpMUSHLexer.DELIM_STARTCARET or SharpMUSHLexer.REGEXP_STARTCARET:
					(open ??= new Stack<int>()).Push(Closer(type));
					depth++;
					break;
				case SharpMUSHLexer.CBRACK or SharpMUSHLexer.CBRACE or SharpMUSHLexer.CPAREN or SharpMUSHLexer.CCARET
					when depth > 0 && open!.Peek() == type:
					open.Pop();
					depth--;
					break;
				case SharpMUSHLexer.SEMICOLON when commandList && depth == 0:
					commandStart = true;
					break;
			}
		}

		if (marked)
		{
			for (var i = 0; i < tokens.Count; i++)
			{
				if (tokens[i] is IWritableToken writable) writable.TokenIndex = i;
			}
		}

		static int Closer(int opener) => opener switch
		{
			SharpMUSHLexer.OBRACK => SharpMUSHLexer.CBRACK,
			SharpMUSHLexer.OBRACE => SharpMUSHLexer.CBRACE,
			SharpMUSHLexer.FUNCHAR => SharpMUSHLexer.CPAREN,
			_ => SharpMUSHLexer.CCARET
		};
	}

	/// <summary>
	/// Marks the command whose first token is at <paramref name="index"/>, when its head names a built-in;
	/// see <see cref="MarkCommandHeads"/>.
	/// </summary>
	private static bool MarkCommandHead(List<IToken> tokens, int index, SharpMUSHLexer lexer,
		StringSpanInputStream input, ICommandHeads heads, bool commandList)
	{
		var first = tokens[index];
		if (first.Type != SharpMUSHLexer.OTHER) return false;

		var text = first.Text;
		var nameStart = 0;
		while (nameStart < text.Length && text[nameStart] == ' ') nameStart++;
		var nameEnd = text.IndexOf(' ', nameStart);
		if (nameStart == text.Length || text.AsSpan(0, nameStart).ContainsAnyExcept(' ')) return false;

		if (nameEnd == -1)
		{
			// A name with nothing after it, or with an argument that starts without a space: only the first
			// can be told from the tokens.
			var next = tokens[index + 1].Type;
			if (!(next == TokenConstants.EOF || commandList && next == SharpMUSHLexer.SEMICOLON)) return false;
			nameEnd = text.Length;
		}

		var split = heads.SplitToken(text[nameStart..nameEnd]);
		if (split == 0) return false;

		var argumentStart = nameEnd;
		while (argumentStart < text.Length && text[argumentStart] == ' ') argumentStart++;
		var source = Tuple.Create((ITokenSource)lexer, (ICharStream)input);
		var head = new CommonToken(source, split, TokenConstants.DefaultChannel, first.StartIndex,
			first.StartIndex + argumentStart - 1);

		if (argumentStart == text.Length)
		{
			tokens[index] = head;
			return true;
		}

		// Lex again from the first argument character until a token ends where an old one did, with the
		// lexer back in its default mode on both sides; from there the old tokens are the same.
		lexer.SetInputStream(input);
		input.Seek(first.StartIndex + argumentStart);
		var relexed = new List<IToken> { head };
		var old = index;
		while (true)
		{
			var token = lexer.NextToken();
			relexed.Add(token);
			while (old < tokens.Count - 1 && tokens[old].StopIndex < token.StopIndex) old++;
			if (token.Type == TokenConstants.EOF) { old = tokens.Count - 1; break; }
			if (tokens[old].StopIndex == token.StopIndex && lexer.ModeStack.Count == 0
				&& tokens[old].Type is not (SharpMUSHLexer.ESCAPE or SharpMUSHLexer.PERCENT or SharpMUSHLexer.OANSI
					or SharpMUSHLexer.ANSICHARACTER))
				break;
		}

		tokens.RemoveRange(index, old - index + 1);
		tokens.InsertRange(index, relexed);
		return true;
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
		var lexer = new SharpMUSHLexer(inputStream);
		lexer.RemoveErrorListeners();

		return lexer;
	}

	/// <summary>
	/// A parser over <paramref name="tokens"/> with no error listeners. Callers attach the listener
	/// and error strategy their pass needs.
	/// </summary>
	/// <summary>
	/// <see cref="CreateParser"/>, reusing this thread's parser for that paren_groups setting when it
	/// is free. Give it back with <see cref="ReturnParser"/> once the tree is built; the tree does not
	/// need the parser.
	/// </summary>
	public static SharpMUSHParser RentParser(BufferedTokenSpanStream tokens, bool parenGroups, PredictionMode mode,
		bool trace = false)
	{
		ref var slot = ref parenGroups ? ref t_parenGroupsParser : ref t_parser;
		if (slot is not { } parser)
		{
			return CreateParser(tokens, parenGroups, mode, trace);
		}

		slot = null;
		parser.TokenStream = tokens;
		parser.Trace = trace;
		parser.Interpreter.PredictionMode = mode;
		parser.RemoveErrorListeners();

		return parser;
	}

	/// <summary>Makes a parser from <see cref="RentParser"/> available to the next parse on this thread.</summary>
	public static void ReturnParser(SharpMUSHParser parser)
	{
		if (parser.ParenGroups)
		{
			t_parenGroupsParser = parser;
		}
		else
		{
			t_parser = parser;
		}
	}

	public static SharpMUSHParser CreateParser(BufferedTokenSpanStream tokens, bool parenGroups, PredictionMode mode,
		bool trace = false)
	{
		var parser = new SharpMUSHParser(tokens)
		{
			ParenGroups = parenGroups,
			Trace = trace
		};
		parser.Interpreter.PredictionMode = mode;
		parser.RemoveErrorListeners();

		return parser;
	}

	/// <summary>
	/// The SLL pass of two-stage prediction, for a <paramref name="parser"/> built with SLL: runs
	/// <paramref name="entryPoint"/> under a <see cref="BailErrorStrategy"/> and returns the tree only
	/// if nothing went wrong. A tree from here is the one LL would build, so it stands; on
	/// <see langword="null"/>, the caller seeks the tokens back to the start and parses again with LL,
	/// which decides whether the input really has a syntax error and reports it.
	/// </summary>
	public static TContext? ParseClean<TContext>(SharpMUSHParser parser, Func<SharpMUSHParser, TContext> entryPoint,
		ParserErrorListener errors) where TContext : class, SharpMUSHParser.ISoftcodeContext
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
	public static SharpMUSHParser.ISoftcodeContext Enter(SharpMUSHParser parser, ParseType parseType) => parseType switch
	{
		ParseType.Command => parser.StartSingleCommandString(),
		ParseType.CommandList => parser.StartCommandString(),
		ParseType.CommandSingleArg => parser.StartPlainSingleCommandArg(),
		ParseType.CommandCommaArgs => parser.StartPlainCommaCommandArgs(),
		ParseType.CommandEqSplitArgs => parser.StartEqSplitCommandArgs(),
		ParseType.CommandEqSplit => parser.StartEqSplitCommand(),
		_ => parser.StartPlainString()
	};

	/// <summary>
	/// Whether the token stream nests recursion-causing delimiters — <c>[</c>, <c>{</c>, a
	/// function-call <c>name(</c>, and with <paramref name="parenGroups"/> a bare <c>(</c> — deeper
	/// than <paramref name="limit"/>. These are exactly the constructs whose parser rules recurse
	/// (<c>bracketPattern</c>, <c>bracePattern</c>, <c>function</c>, and the paren-group rules).
	/// Without paren_groups a bare <c>(</c> is plain text and opens no rule, so it is tracked only to
	/// match its closing <c>)</c>. Escaped delimiters never reach here as openers — the lexer emits
	/// <c>ESCAPE</c> + <c>ANY</c> for <c>\[</c> — so they add no depth, matching what the parser
	/// would have done.
	/// </summary>
	public static bool ExceedsNestingLimit(BufferedTokenSpanStream tokenStream, int limit, bool parenGroups,
		out IToken? offendingToken)
	{
		offendingToken = null;
		// Most lines open nothing at all, so the matching stack exists only once one does.
		Stack<char>? open = null;
		var depth = 0;

		var checkedTokens = 0;
		foreach (var token in CollectionsMarshal.AsSpan(tokenStream.tokens))
		{
			if ((checkedTokens++ & 1023) == 0) ExecutionBudget.Current?.ThrowIfExceeded();
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
				case SharpMUSHLexer.OPAREN when parenGroups:
					(open ??= new Stack<char>()).Push('g');
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
					if (open is not null && open.TryPeek(out var p) && p is '(' or 'g' or 'o')
					{
						if (p is '(' or 'g') depth--;
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
	/// so no bracketPattern opens. The matching ] still becomes CBRACK
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
