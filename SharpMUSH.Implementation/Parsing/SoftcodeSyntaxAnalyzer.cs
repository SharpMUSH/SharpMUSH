using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using LspRange = SharpMUSH.Library.Models.Range;

namespace SharpMUSH.Implementation.Parsing;

/// <summary>
/// The tooling half of the parser: tokens, syntax errors, diagnostics and semantic tokens for
/// highlighting, the language server and the MCP analyzer. It parses but never evaluates, so it
/// needs no parser state and no service beyond the function library (to tell a built-in from a
/// user function) and the options that shape the grammar.
/// </summary>
/// <remarks>
/// <see cref="MUSHCodeParser"/> exposes these through <see cref="IMUSHCodeParser"/> and forwards
/// to a fresh analyzer over its current library and options, so a parser copied with another
/// library classifies against that library.
/// </remarks>
/// <param name="functionLibrary">Functions known by name, for classifying calls.</param>
/// <param name="configuration">Options read per call: <c>ParenGroups</c> and the prediction mode.</param>
public sealed class SoftcodeSyntaxAnalyzer(
	LibraryService<string, FunctionDefinition> functionLibrary,
	IOptionsWrapper<SharpMUSHOptions> configuration)
{
	// Lexer vocabulary is static and immutable — cached once to avoid allocating a new lexer on every fallback classification
	private static readonly IVocabulary LexerVocabulary =
		new SharpMUSHLexer(new StringSpanInputStream(string.Empty, string.Empty)).Vocabulary;

	/// <summary>
	/// The single ANTLR prediction mode for a pass that reports what it finds — the tooling paths
	/// (validation, semantic tokens), after <see cref="TryCleanParse"/> — and for every pass under
	/// the SLL and LL settings. TwoStage resolves to LL here so the result is the authoritative one.
	/// </summary>
	internal static PredictionMode SinglePassPredictionMode(SharpMUSHOptions options)
		=> options.Debug.ParserPredictionMode switch
		{
			ParserPredictionMode.SLL => PredictionMode.SLL,
			_ => PredictionMode.LL
		};

	private SharpMUSHParser CreateParser(BufferedTokenSpanStream tokens)
	{
		var options = configuration.CurrentValue;
		return SoftcodeParsePipeline.CreateParser(tokens, options.Compatibility.ParenGroups,
			SinglePassPredictionMode(options));
	}

	/// <summary>
	/// Under TwoStage, the SLL pass the evaluator runs first (<see cref="SoftcodeParsePipeline.ParseClean"/>):
	/// a tree from it is the one LL would build, so tooling takes it and runs LL only on input SLL
	/// cannot parse. <see langword="null"/> under the SLL and LL settings, or when SLL failed; the
	/// tokens are sought back to the start either way.
	/// </summary>
	private SharpMUSHParser.ISoftcodeContext? TryCleanParse(BufferedTokenSpanStream tokens, string plaintext, ParseType parseType)
	{
		var options = configuration.CurrentValue;
		if (options.Debug.ParserPredictionMode != ParserPredictionMode.TwoStage)
		{
			return null;
		}

		var parser = SoftcodeParsePipeline.CreateParser(tokens, options.Compatibility.ParenGroups, PredictionMode.SLL);
		var context = SoftcodeParsePipeline.ParseClean(parser, p => SoftcodeParsePipeline.Enter(p, parseType),
			new ParserErrorListener(plaintext));
		tokens.Seek(0);

		return context;
	}

	/// <summary>
	/// Tokenizes the input text and returns token information for syntax highlighting.
	/// </summary>
	public IReadOnlyList<TokenInfo> Tokenize(MString text)
	{
		var bufferedTokenSpanStream = SoftcodeParsePipeline.Lex(text.ToPlainText(), nameof(Tokenize),
			rewriteOrphanedClosers: false);

		var tokenArray = bufferedTokenSpanStream.tokens;
		if (tokenArray.Count <= 1)
		{
			return [];
		}

		var tokens = new List<TokenInfo>(tokenArray.Count - 1);
		for (var i = 0; i < tokenArray.Count - 1; i++)
		{
			var token = tokenArray[i];
			var tokenInfo = new TokenInfo
			{
				Type = LexerVocabulary.GetSymbolicName(token.Type) ?? $"Token{token.Type}",
				StartIndex = token.StartIndex,
				EndIndex = token.StopIndex,
				Text = token.Text ?? string.Empty,
				Line = token.Line,
				Column = token.Column,
				Channel = token.Channel
			};

			tokens.Add(tokenInfo);
		}

		return tokens;
	}

	/// <summary>
	/// Parses the input text and returns any errors encountered.
	/// Uses the configured prediction mode (SLL or LL) for parsing.
	/// </summary>
	public IReadOnlyList<ParseError> ValidateAndGetErrors(MString text, ParseType parseType = ParseType.Function)
	{
		var plaintext = text.ToPlainText();
		var bufferedTokenSpanStream = SoftcodeParsePipeline.Lex(plaintext, nameof(ValidateAndGetErrors));

		// Report over-deep nesting as a diagnostic rather than parsing it and overflowing the
		// stack — this path feeds the LSP/MCP analyzer, which must survive hostile documents.
		if (SoftcodeParsePipeline.ExceedsNestingLimit(bufferedTokenSpanStream, SoftcodeParsePipeline.MaxParseNestingDepth, out var offending))
		{
			return
			[
				new ParseError
				{
					Line = offending?.Line ?? 1,
					Column = offending?.Column ?? 0,
					OffendingToken = offending?.Text,
					Message = $"Expression nests brackets, braces or function calls more than {SoftcodeParsePipeline.MaxParseNestingDepth} levels deep.",
					InputText = plaintext,
				}
			];
		}

		if (TryCleanParse(bufferedTokenSpanStream, plaintext, parseType) is not null)
		{
			return [];
		}

		var sharpParser = CreateParser(bufferedTokenSpanStream);
		var errorListener = new ParserErrorListener(plaintext);
		sharpParser.AddErrorListener(errorListener);

		try
		{
			_ = SoftcodeParsePipeline.Enter(sharpParser, parseType);
		}
		catch (RecognitionException)
		{
		}

		return errorListener.Errors;
	}

	/// <summary>
	/// Parses the input text and returns diagnostics (LSP-compatible errors/warnings).
	/// </summary>
	public IReadOnlyList<Diagnostic> GetDiagnostics(MString text, ParseType parseType = ParseType.Function)
	{
		var errors = ValidateAndGetErrors(text, parseType);
		if (errors.Count == 0)
		{
			return [];
		}

		var diagnostics = new List<Diagnostic>(errors.Count);
		for (var i = 0; i < errors.Count; i++)
		{
			diagnostics.Add(errors[i].ToDiagnostic());
		}

		return diagnostics;
	}

	/// <summary>
	/// Performs semantic analysis on the input text and returns semantic tokens.
	/// </summary>
	public IReadOnlyList<SemanticToken> GetSemanticTokens(MString text, ParseType parseType = ParseType.Function)
	{
		var plaintext = text.ToPlainText();
		var bufferedTokenSpanStream = SoftcodeParsePipeline.Lex(plaintext, nameof(GetSemanticTokens));

		// Too deep to parse safely: fall back to the flat lexer-token classification, the same
		// degraded result the catch below produces for a syntax error.
		// NOTE: this re-lexes via Tokenize, which is the one lexing site here that does NOT apply the
		// orphaned-closer rewrite above — so an orphaned ']' or '}' is classified as a closer on this
		// path and as literal text on the normal one. Inconsistent, but left alone deliberately:
		// changing Tokenize affects every caller and needs its own task.
		if (SoftcodeParsePipeline.ExceedsNestingLimit(bufferedTokenSpanStream, SoftcodeParsePipeline.MaxParseNestingDepth, out _))
		{
			return ConvertSyntacticToSemanticTokens(Tokenize(text));
		}

		if (TryCleanParse(bufferedTokenSpanStream, plaintext, parseType) is { } clean)
		{
			return AnalyzeSemanticTokens(clean, bufferedTokenSpanStream, plaintext);
		}

		var sharpParser = CreateParser(bufferedTokenSpanStream);

		try
		{
			var context = SoftcodeParsePipeline.Enter(sharpParser, parseType);
			return AnalyzeSemanticTokens(context, bufferedTokenSpanStream, plaintext);
		}
		catch (RecognitionException)
		{
			// Same Tokenize inconsistency as the nesting-limit fallback above.
			return ConvertSyntacticToSemanticTokens(Tokenize(text));
		}
	}

	/// <summary>
	/// Performs semantic analysis and returns tokens in LSP delta-encoded format.
	/// </summary>
	public SemanticTokensData GetSemanticTokensData(MString text, ParseType parseType = ParseType.Function)
	{
		var tokens = GetSemanticTokens(text, parseType);
		return SemanticTokensData.FromTokens(tokens);
	}

	/// <summary>
	/// Analyzes the parse tree to extract semantic tokens.
	/// </summary>
	private IReadOnlyList<SemanticToken> AnalyzeSemanticTokens(
		SharpMUSHParser.ISoftcodeContext context,
		BufferedTokenSpanStream tokenStream,
		string sourceText)
	{
		var tokenArray = tokenStream.tokens;
		var tokenCount = Math.Max(tokenArray.Count - 1, 0);

		// Single tree walk: classify every terminal by its immediate parse-tree parent context.
		// This is the canonical correct approach — it handles all tokens that appear in multiple
		// grammatical roles (CCARET, EQUALS, COMMAWS, SEMICOLON, FUNCHAR, …) without per-symbol
		// special-case pre-walks.
		var classifications = new Dictionary<int, (SemanticTokenType Type, SemanticTokenModifier Mod)>(tokenCount);
		CollectTerminalClassifications(context, classifications, sourceText);

		var semanticTokens = new List<SemanticToken>(tokenCount);

		// Iterate the stream's token list directly rather than through a LINQ filter. EOF is
		// always its last element, so iterate all-but-last.
		if (tokenArray.Count > 0)
		{
			for (var i = 0; i < tokenArray.Count - 1; i++)
			{
				var token = tokenArray[i];
				if (!classifications.TryGetValue(token.TokenIndex, out var info))
					info = (SemanticTokenType.Text, SemanticTokenModifier.None);

				var text = token.Text;
				semanticTokens.Add(new SemanticToken
				{
					Range = new LspRange
					{
						Start = new Position(token.Line - 1, token.Column),
						End = new Position(token.Line - 1, token.Column + text.Length)
					},
					TokenType = info.Type,
					Modifiers = info.Mod,
					Text = text
				});
			}
		}

		return semanticTokens;
	}

	/// <summary>
	/// Walks the parse tree and records the semantic classification for every terminal node.
	/// Each terminal is classified by its immediate parent rule context, not by token type alone.
	/// This is the single authoritative classification pass — no pre-walks or per-symbol workarounds.
	/// </summary>
	private void CollectTerminalClassifications(
		Antlr4.Runtime.Tree.IParseTree tree,
		Dictionary<int, (SemanticTokenType Type, SemanticTokenModifier Mod)> map,
		string sourceText)
	{
		if (tree is Antlr4.Runtime.Tree.ITerminalNode terminal)
		{
			var token = terminal.Symbol;
			if (token.Type == TokenConstants.EOF) return;

			var type = ClassifyTerminalInContext(token, terminal.Parent, sourceText);
			var mod = GetTokenModifiers(token, type);
			map[token.TokenIndex] = (type, mod);
			return;
		}

		for (var i = 0; i < tree.ChildCount; i++)
			CollectTerminalClassifications(tree.GetChild(i), map, sourceText);
	}

	/// <summary>
	/// Derives the semantic type for a terminal token from its immediate parse-tree parent.
	/// Covers every grammatical role a token may play — structural text, operator, substitution, etc.
	/// Falls back to <see cref="ClassifyByTokenType"/> only for tokens whose meaning is
	/// context-independent (e.g., <c>OBRACK</c>, <c>ESCAPE</c>, <c>OANSI</c>).
	/// </summary>
	private SemanticTokenType ClassifyTerminalInContext(IToken token, Antlr4.Runtime.Tree.IParseTree parentCtx, string sourceText)
	{
		return parentCtx switch
		{
			// CCARET (>), EQUALS (=), COMMAWS (,), SEMICOLON (;), CPAREN ()) appear here when
			// they are NOT serving as argument separators, delimiters or register-close markers.
			// OTHER inside beginGenericText still needs content-based classification
			// (e.g. #1234 is ObjectReference, "42" is Number).
			SharpMUSHParser.IBeginGenericTextContext when token.Type != SharpMUSHParser.OTHER
				=> SemanticTokenType.Text,
			SharpMUSHParser.IBeginGenericTextContext
				=> ClassifyOther(token.Text, sourceText),

			// A FUNCHAR appearing in genericText (not inside a function call) is plain text.
			SharpMUSHParser.IGenericTextContext
				=> SemanticTokenType.Text,

			// FUNCHAR is the open-paren+name; COMMAWS and CPAREN inside the function are operators.
			SharpMUSHParser.IFunctionContext when token.Type == SharpMUSHParser.FUNCHAR
				=> ClassifyFunction(token.Text),
			SharpMUSHParser.IFunctionContext
				=> SemanticTokenType.Operator,

			SharpMUSHParser.IBracketPatternContext
				=> SemanticTokenType.BracketSubstitution,

			SharpMUSHParser.IBracePatternContext
				=> SemanticTokenType.BraceGroup,

			SharpMUSHParser.AnsiContext
				=> SemanticTokenType.AnsiCode,

			SharpMUSHParser.EscapedTextContext
				=> SemanticTokenType.EscapeSequence,

			// %q<register> — opening token (q<) and closing > are both Register
			SharpMUSHParser.IComplexSubstitutionSymbolContext
				=> SemanticTokenType.Register,

			// $0-$9 and $<name> read regexp captures; $< and its > are Register too.
			SharpMUSHParser.IRegexpCaptureContext
				=> SemanticTokenType.Register,

			// EQUALS here means %=; DBREF means %#; CALLED_DBREF means %@ — all Substitution.
			SharpMUSHParser.SubstitutionSymbolContext
				=> SemanticTokenType.Substitution,

			// PERCENT is the only direct terminal child of IExplicitEvaluationStringContext.
			SharpMUSHParser.IExplicitEvaluationStringContext
			or SharpMUSHParser.IBraceExplicitEvaluationStringContext
				=> SemanticTokenType.Substitution,

			SharpMUSHParser.IStartEqSplitCommandContext
			or SharpMUSHParser.IStartEqSplitCommandArgsContext
				=> SemanticTokenType.Operator,

			SharpMUSHParser.ICommaCommandArgsContext
				=> SemanticTokenType.Operator,

			SharpMUSHParser.ICommandListContext
				=> SemanticTokenType.Operator,

			_ => ClassifyByTokenType(token, sourceText)
		};
	}

	/// <summary>
	/// Classifies tokens whose semantic meaning does not depend on parse-tree context.
	/// Called only as a fallback from <see cref="ClassifyTerminalInContext"/>.
	/// </summary>
	private SemanticTokenType ClassifyByTokenType(IToken token, string sourceText)
	{
		return LexerVocabulary.GetSymbolicName(token.Type) switch
		{
			"ARG_NUM" or "VWX" or "REG_NUM" or "REG_ALPHA" or "REG_STARTCARET" => SemanticTokenType.Register,
			"ENACTOR_NAME" or "CAP_ENACTOR_NAME" or "ACCENT_NAME" or "MONIKER_NAME" => SemanticTokenType.Substitution,
			"SUB_PRONOUN" or "OBJ_PRONOUN" or "POS_PRONOUN" or "ABS_POS_PRONOUN" => SemanticTokenType.Substitution,
			"CALLED_DBREF" or "EXECUTOR_DBREF" or "LOCATION_DBREF" or "DBREF" => SemanticTokenType.Substitution,
			"OBRACK" or "CBRACK" => SemanticTokenType.BracketSubstitution,
			"OBRACE" or "CBRACE" => SemanticTokenType.BraceGroup,
			"ESCAPE" => SemanticTokenType.EscapeSequence,
			"OANSI" or "CANSI" or "ANSICHARACTER" => SemanticTokenType.AnsiCode,
			"PERCENT" => SemanticTokenType.Substitution,
			"FUNCHAR" => ClassifyFunction(token.Text),
			"OTHER" => ClassifyOther(token.Text, sourceText),
			_ => SemanticTokenType.Text
		};
	}

	/// <summary>
	/// Classifies a function name token.
	/// </summary>
	private SemanticTokenType ClassifyFunction(string functionText)
	{
		var functionName = functionText.TrimEnd('(', ' ', '\t', '\r', '\n', '\f');

		if (functionLibrary.TryGetValue(functionName, out var functionInfo)
			|| functionLibrary.TryGetValue(functionName.ToLowerInvariant(), out functionInfo))
		{
			return functionInfo.IsSystem
				? SemanticTokenType.Function
				: SemanticTokenType.UserFunction;
		}

		return SemanticTokenType.Function;
	}

	/// <summary>
	/// Classifies an OTHER token to determine if it's a number, object reference, etc.
	/// </summary>
	private static SemanticTokenType ClassifyOther(string text, string sourceText)
	{
		if (int.TryParse(text, out _) || double.TryParse(text, out _))
		{
			return SemanticTokenType.Number;
		}

		if (text.StartsWith('#') && text.Length > 1)
		{
			return SemanticTokenType.ObjectReference;
		}

		return SemanticTokenType.Text;
	}

	/// <summary>
	/// Gets modifiers for a token based on its type.
	/// </summary>
	private SemanticTokenModifier GetTokenModifiers(IToken token, SemanticTokenType semanticType)
	{
		var modifiers = SemanticTokenModifier.None;

		if (semanticType == SemanticTokenType.Function ||
				semanticType == SemanticTokenType.Substitution ||
				semanticType == SemanticTokenType.Register)
		{
			modifiers |= SemanticTokenModifier.DefaultLibrary;
		}

		return modifiers;
	}

	/// <summary>
	/// Converts syntactic tokens to semantic tokens as a fallback.
	/// </summary>
	private static IReadOnlyList<SemanticToken> ConvertSyntacticToSemanticTokens(IReadOnlyList<TokenInfo> tokens)
	{
		return tokens.Select(t => new SemanticToken
		{
			Range = new LspRange
			{
				Start = new Position(t.Line - 1, t.Column),
				End = new Position(t.Line - 1, t.Column + t.Length)
			},
			TokenType = t.Type switch
			{
				"FUNCHAR" => SemanticTokenType.Function,
				"PERCENT" => SemanticTokenType.Substitution,
				"OBRACK" or "CBRACK" => SemanticTokenType.BracketSubstitution,
				"OBRACE" or "CBRACE" => SemanticTokenType.BraceGroup,
				"ESCAPE" => SemanticTokenType.EscapeSequence,
				"COMMAWS" or "EQUALS" or "SEMICOLON" => SemanticTokenType.Operator,
				_ => SemanticTokenType.Text
			},
			Modifiers = SemanticTokenModifier.None,
			Text = t.Text
		}).ToList();
	}
}
