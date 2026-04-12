using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OneOf.Types;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Implementation.Visitors;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using LspRange = SharpMUSH.Library.Models.Range;

namespace SharpMUSH.Implementation;

/// <summary>
/// Provides the parser for MUSH commands and functions.
/// Each call is Synchronous and Stateful at this time.
/// 
/// <para><b>Performance Optimizations:</b></para>
/// <list type="bullet">
/// <item><description>Services are resolved once at construction and cached to avoid repeated DI lookups</description></item>
/// <item><description>CommandTrie provides O(m) prefix matching where m is the length of the search string</description></item>
/// <item><description>ParseInternal() consolidates parser/lexer creation to reduce code duplication</description></item>
/// <item><description>Custom span-based streams (BufferedTokenSpanStream, AntlrInputStreamSpan) minimize allocations</description></item>
/// <item><description>Prediction mode can be configured (SLL vs LL) for performance vs accuracy tradeoff</description></item>
/// </list>
/// 
/// <para>For detailed optimization analysis, see PARSER_OPTIMIZATION_ANALYSIS.md</para>
/// </summary>
public record MUSHCodeParser(ILogger<MUSHCodeParser> Logger,
	LibraryService<string, FunctionDefinition> FunctionLibrary,
	LibraryService<string, CommandDefinition> CommandLibrary,
	IOptionsWrapper<SharpMUSHOptions> Configuration,
	IServiceProvider ServiceProvider) : IMUSHCodeParser
{
	// Cached service instances to avoid repeated DI resolution on every parse operation
	private readonly IMediator _mediator = ServiceProvider.GetRequiredService<IMediator>();
	private readonly INotifyService _notifyService = ServiceProvider.GetRequiredService<INotifyService>();
	private readonly IConnectionService _connectionService = ServiceProvider.GetRequiredService<IConnectionService>();
	private readonly ILocateService _locateService = ServiceProvider.GetRequiredService<ILocateService>();
	private readonly ICommandDiscoveryService _commandDiscoveryService = ServiceProvider.GetRequiredService<ICommandDiscoveryService>();
	private readonly IAttributeService _attributeService = ServiceProvider.GetRequiredService<IAttributeService>();
	private readonly IHookService _hookService = ServiceProvider.GetRequiredService<IHookService>();

	// Command trie for efficient prefix-based command lookup
	private readonly CommandTrie _commandTrie = BuildCommandTrie(CommandLibrary);

	/// <summary>
	/// Gets the command trie for efficient prefix-based command lookups.
	/// </summary>
	public CommandTrie CommandTrie => _commandTrie;

	/// <summary>
	/// Builds a trie from the command library for efficient prefix matching.
	/// </summary>
	private static CommandTrie BuildCommandTrie(LibraryService<string, CommandDefinition> commandLibrary)
	{
		var trie = new CommandTrie();

		foreach (var (commandName, commandInfo) in commandLibrary)
		{
			if (commandInfo.IsSystem) // Only add system commands to trie
			{
				trie.Add(commandName, commandInfo.LibraryInformation);
			}
		}

		return trie;
	}

	public ParserState CurrentState => State.Peek();

	/// <summary>
	/// Stack may not be needed if we can bring ParserState into the custom Visitors.
	/// 
	/// Stack should be good enough, since we parse left-to-right when we consider the Visitors.
	/// However, we may run into issues when it comes to function-depth calculations.
	/// 
	/// Time to start drawing a tree to make sure we put things in the right spots.
	/// </summary>
	public IImmutableStack<ParserState> State { get; private init; } = ImmutableStack<ParserState>.Empty;

	public IMUSHCodeParser FromState(ParserState state) => new MUSHCodeParser(Logger, FunctionLibrary, CommandLibrary, Configuration, ServiceProvider, state);

	public Option<ParserState> StateHistory(uint index)
	{
		try
		{
			var a = State.Take((int)index);
			return a.Last();
		}
		catch
		{
			return new None();
		}
	}

	public IMUSHCodeParser Empty() => this with { State = ImmutableStack<ParserState>.Empty };

	public IMUSHCodeParser Push(ParserState state) => this with { State = State.Push(state) };

	public MUSHCodeParser(ILogger<MUSHCodeParser> logger,
		LibraryService<string, FunctionDefinition> functionLibrary,
		LibraryService<string, CommandDefinition> commandLibrary,
		IOptionsWrapper<SharpMUSHOptions> config,
		IServiceProvider serviceProvider,
		ParserState state) : this(logger, functionLibrary, commandLibrary, config, serviceProvider)
		=> State = [state];

	/// <summary>
	/// Gets the configured ANTLR prediction mode based on configuration.
	/// </summary>
	private PredictionMode GetPredictionMode()
	{
		return Configuration.CurrentValue.Debug.ParserPredictionMode switch
		{
			ParserPredictionMode.SLL => PredictionMode.SLL,
			ParserPredictionMode.LL => PredictionMode.LL,
			_ => PredictionMode.SLL // Default to SLL
		};
	}

	/// <summary>
	/// Common internal parsing method that handles lexer, parser, and visitor creation.
	/// This reduces code duplication across the various Parse methods.
	/// </summary>
	/// <typeparam name="TContext">The parser rule context type</typeparam>
	/// <param name="text">The text to parse</param>
	/// <param name="entryPoint">Function to get the parser context from the parser</param>
	/// <param name="methodName">Name of the calling method for debugging</param>
	/// <param name="parser">Optional parser instance to use. When null, defaults to 'this'.
	/// Pass a different parser when you need custom parser state (e.g., CommandParse with handle info).</param>
	/// <returns>The result of visiting the parse tree</returns>
	private ValueTask<CallState?> ParseInternal<TContext>(
		MString text,
		Func<SharpMUSHParser, TContext> entryPoint,
		string methodName,
		IMUSHCodeParser? parser = null)
		where TContext : ParserRuleContext
	{
		// Use provided parser or default to this instance
		parser ??= this;

		AntlrInputStreamSpan inputStream = new(MModule.plainText(text).AsMemory(), methodName);
		SharpMUSHLexer sharpLexer = new(inputStream);
		BufferedTokenSpanStream bufferedTokenSpanStream = new(sharpLexer);
		bufferedTokenSpanStream.Fill();
		RewriteOrphanedBracketClosers(bufferedTokenSpanStream);
		RewriteOrphanedBraceClosers(bufferedTokenSpanStream);

		SharpMUSHParser sharpParser = new(bufferedTokenSpanStream)
		{
			Interpreter = { PredictionMode = GetPredictionMode() },
			Trace = Configuration.CurrentValue.Debug.DebugSharpParser
		};

		if (Configuration.CurrentValue.Debug.DebugSharpParser)
		{
			sharpParser.AddErrorListener(new DiagnosticErrorListener(false));
		}

		var context = entryPoint(sharpParser);

		SharpMUSHParserVisitor visitor = new(Logger, parser,
			Configuration,
			_mediator,
			_notifyService,
			_connectionService,
			_locateService,
			_commandDiscoveryService,
			_attributeService,
			_hookService,
			text);

		return visitor.Visit(context);
	}

	public ValueTask<CallState?> FunctionParse(MString text)
	{
		// Ensure we have invocation tracking for standalone function parsing
		// Check if tracking is already initialized - if not, create a new parser with tracking
		var needsTracking = State.IsEmpty || CurrentState.TotalInvocations == null;

		var parser = needsTracking
			? Push(new ParserState(
				Registers: new([[]]),
				IterationRegisters: [],
				RegexRegisters: [],
				SwitchStack: [],
				EnvironmentRegisters: [],
				CurrentEvaluation: null,
				ExecutionStack: [],
				ParserFunctionDepth: 0,
				Function: null,
				Command: null,
				CommandInvoker: _ => ValueTask.FromResult(new Option<CallState>(new None())),
				Switches: [],
				Arguments: [],
				Executor: null,
				Enactor: null,
				Caller: null,
				Handle: null,
				ParseMode: ParseMode.Default,
				HttpResponse: null,
				CallDepth: new InvocationCounter(),
				FunctionRecursionDepths: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
				TotalInvocations: new InvocationCounter(),
				LimitExceeded: new LimitExceededFlag()))
			: this;

		return ParseInternal(text, p => p.startPlainString(), nameof(FunctionParse), parser);
	}

	public ValueTask<CallState?> CommandListParse(MString text)
	{
		// Push a fresh CommandHistory so @retry can track previous commands in this parse session.
		// Also clear DirectInput: a CommandListParse is always a queue/callback context, never
		// direct player input (equivalent to PennMUSH dropping the QUEUE_NOLIST flag here).
		var freshParser = State.IsEmpty ? this : Push(CurrentState with
		{
			CommandHistory = new ConcurrentStack<(Func<IMUSHCodeParser, ValueTask<Option<CallState>>> Invoker, Dictionary<string, CallState> Args)>(),
			Flags = CurrentState.Flags & ~ParserStateFlags.DirectInput
		});
		return ParseInternal(text, p => p.startCommandString(), nameof(CommandListParse), freshParser);
	}

	public Func<ValueTask<CallState?>> CommandListParseVisitor(MString text)
	{
		var plaintext = MModule.plainText(text);
		AntlrInputStreamSpan inputStream = new(plaintext.AsMemory(), nameof(CommandListParseVisitor));
		SharpMUSHLexer sharpLexer = new(inputStream);
		BufferedTokenSpanStream bufferedTokenSpanStream = new(sharpLexer);
		bufferedTokenSpanStream.Fill();
		RewriteOrphanedBracketClosers(bufferedTokenSpanStream);
		RewriteOrphanedBraceClosers(bufferedTokenSpanStream);
		SharpMUSHParser sharpParser = new(bufferedTokenSpanStream)
		{
			Interpreter =
			{
				PredictionMode = GetPredictionMode()
			},
			Trace = Configuration.CurrentValue.Debug.DebugSharpParser
		};
		if (Configuration.CurrentValue.Debug.DebugSharpParser)
		{
			sharpParser.AddErrorListener(new DiagnosticErrorListener(false));
		}

		var chatContext = sharpParser.startCommandString();

		// Clear DirectInput for the same reason as CommandListParse: this visitor is always
		// used in a queue/callback context (e.g., @force, @trigger), never for direct player input.
		var parserForList = State.IsEmpty ? this : Push(CurrentState with { Flags = CurrentState.Flags & ~ParserStateFlags.DirectInput });

		SharpMUSHParserVisitor visitor = new(Logger, parserForList,
			Configuration,
			_mediator,
			_notifyService,
			_connectionService,
			_locateService,
			_commandDiscoveryService,
			_attributeService,
			_hookService, text);

		return () => visitor.Visit(chatContext);
	}

	/// <summary>
	/// This is the main entry point for commands run by a player.
	/// </summary>
	/// <param name="handle">The handle that identifies the connection.</param>
	/// <param name="text">The text to parse.</param>
	/// <returns>A completed task.</returns>
	public async ValueTask<CallState> CommandParse(long handle, IConnectionService connectionService, MString text)
	{
		var handleId = connectionService.Get(handle);
		var newParser = Push(new ParserState(
			Registers: new([[]]),
			IterationRegisters: [],
			RegexRegisters: [],
			SwitchStack: [],
			EnvironmentRegisters: [],
			CurrentEvaluation: null,
			ExecutionStack: [],
			ParserFunctionDepth: 0,
			Function: null,
			Command: MModule.plainText(text),
			CommandInvoker: _ => ValueTask.FromResult(new Option<CallState>(new None())),
			Switches: [],
			Arguments: [],
			Executor: handleId?.Ref,
			Enactor: handleId?.Ref,
			Caller: handleId?.Ref,
			Handle: handle,
			ParseMode: ParseMode.Default,
			HttpResponse: null,
			CallDepth: new InvocationCounter(),
			FunctionRecursionDepths: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
			TotalInvocations: new InvocationCounter(),
			LimitExceeded: new LimitExceededFlag(),
			Flags: ParserStateFlags.DirectInput));

		var result = await ParseInternal(text, p => p.startSingleCommandString(), nameof(CommandParse), newParser);

		return result ?? CallState.Empty;
	}

	/// <summary>
	/// This is the main entry point for commands run by a player.
	/// </summary>
	/// <param name="text">The text to parse.</param>
	/// <returns>A completed task.</returns>
	public async ValueTask<CallState> CommandParse(MString text)
	{
		// Derive DirectInput from the presence of a Handle: a live Handle means this is direct
		// player input (equivalent to PennMUSH's QUEUE_NOLIST). No Handle means it is running
		// in a programmatic or queue context where the RHS of & should be evaluated.
		var baseFlags = State.IsEmpty ? ParserStateFlags.None : CurrentState.Flags;
		var handle = State.IsEmpty ? null : CurrentState.Handle;
		var derivedFlags = handle.HasValue
			? baseFlags | ParserStateFlags.DirectInput
			: baseFlags & ~ParserStateFlags.DirectInput;

		var parserToUse = State.IsEmpty ? this : Push(CurrentState with { Flags = derivedFlags });
		var result = await ParseInternal(text, p => p.startSingleCommandString(), nameof(CommandParse), parserToUse);
		return result ?? CallState.Empty;
	}

	public ValueTask<CallState?> CommandCommaArgsParse(MString text)
		=> ParseInternal(text, p => p.commaCommandArgs(), nameof(CommandCommaArgsParse));

	public ValueTask<CallState?> CommandSingleArgParse(MString text)
		=> ParseInternal(text, p => p.startPlainSingleCommandArg(), nameof(CommandSingleArgParse));

	public ValueTask<CallState?> CommandEqSplitArgsParse(MString text)
		=> ParseInternal(text, p => p.startEqSplitCommandArgs(), nameof(CommandEqSplitArgsParse));

	public ValueTask<CallState?> CommandEqSplitParse(MString text)
		=> ParseInternal(text, p => p.startEqSplitCommand(), nameof(CommandEqSplitParse));

	/// <summary>
	/// Tokenizes the input text and returns token information for syntax highlighting.
	/// </summary>
	public IReadOnlyList<TokenInfo> Tokenize(MString text)
	{
		var plaintext = MModule.plainText(text);
		AntlrInputStreamSpan inputStream = new(plaintext.AsMemory(), nameof(Tokenize));
		SharpMUSHLexer sharpLexer = new(inputStream);

		var tokens = new List<TokenInfo>();
		IToken token;

		while ((token = sharpLexer.NextToken()).Type != TokenConstants.EOF)
		{
			var tokenInfo = new TokenInfo
			{
				Type = sharpLexer.Vocabulary.GetSymbolicName(token.Type) ?? $"Token{token.Type}",
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
		var plaintext = MModule.plainText(text);
		AntlrInputStreamSpan inputStream = new(plaintext.AsMemory(), nameof(ValidateAndGetErrors));
		SharpMUSHLexer sharpLexer = new(inputStream);
		BufferedTokenSpanStream bufferedTokenSpanStream = new(sharpLexer);
		bufferedTokenSpanStream.Fill();
		RewriteOrphanedBracketClosers(bufferedTokenSpanStream);
		RewriteOrphanedBraceClosers(bufferedTokenSpanStream);

		SharpMUSHParser sharpParser = new(bufferedTokenSpanStream)
		{
			Interpreter =
			{
				PredictionMode = GetPredictionMode()
			},
			Trace = false // Don't trace during validation
		};

		// Create custom error listener to collect errors
		var errorListener = new ParserErrorListener(plaintext.ToString());

		// Remove default error listeners and add our custom one
		sharpParser.RemoveErrorListeners();
		sharpParser.AddErrorListener(errorListener);

		try
		{
			// Parse based on the specified parse type
			// We just need to trigger the parsing - we don't use the result
			switch (parseType)
			{
				case ParseType.Function:
					_ = sharpParser.startPlainString();
					break;
				case ParseType.Command:
					_ = sharpParser.startSingleCommandString();
					break;
				case ParseType.CommandList:
					_ = sharpParser.startCommandString();
					break;
				case ParseType.CommandSingleArg:
					_ = sharpParser.startPlainSingleCommandArg();
					break;
				case ParseType.CommandCommaArgs:
					_ = sharpParser.startPlainCommaCommandArgs();
					break;
				case ParseType.CommandEqSplitArgs:
					_ = sharpParser.startEqSplitCommandArgs();
					break;
				case ParseType.CommandEqSplit:
					_ = sharpParser.startEqSplitCommand();
					break;
				default:
					_ = sharpParser.startPlainString();
					break;
			}
		}
		catch (RecognitionException)
		{
			// Errors are collected by the error listener
			// RecognitionException is expected during error recovery
		}

		return errorListener.Errors;
	}

	/// <summary>
	/// Parses the input text and returns diagnostics (LSP-compatible errors/warnings).
	/// </summary>
	public IReadOnlyList<Diagnostic> GetDiagnostics(MString text, ParseType parseType = ParseType.Function)
	{
		var errors = ValidateAndGetErrors(text, parseType);
		return errors.Select(e => e.ToDiagnostic()).ToList();
	}

	/// <summary>
	/// Performs semantic analysis on the input text and returns semantic tokens.
	/// </summary>
	public IReadOnlyList<SemanticToken> GetSemanticTokens(MString text, ParseType parseType = ParseType.Function)
	{
		var plaintext = MModule.plainText(text);
		AntlrInputStreamSpan inputStream = new(plaintext.AsMemory(), nameof(GetSemanticTokens));
		SharpMUSHLexer sharpLexer = new(inputStream);
		BufferedTokenSpanStream bufferedTokenSpanStream = new(sharpLexer);
		bufferedTokenSpanStream.Fill();
		RewriteOrphanedBracketClosers(bufferedTokenSpanStream);
		RewriteOrphanedBraceClosers(bufferedTokenSpanStream);

		SharpMUSHParser sharpParser = new(bufferedTokenSpanStream)
		{
			Interpreter =
			{
				PredictionMode = GetPredictionMode()
			},
			Trace = false
		};

		// Remove error listeners to avoid noise during analysis
		sharpParser.RemoveErrorListeners();

		try
		{
			// Parse to get the parse tree
			ParserRuleContext context;
			switch (parseType)
			{
				case ParseType.Function:
					context = sharpParser.startPlainString();
					break;
				case ParseType.Command:
					context = sharpParser.startSingleCommandString();
					break;
				case ParseType.CommandList:
					context = sharpParser.startCommandString();
					break;
				case ParseType.CommandSingleArg:
					context = sharpParser.startPlainSingleCommandArg();
					break;
				case ParseType.CommandCommaArgs:
					context = sharpParser.startPlainCommaCommandArgs();
					break;
				case ParseType.CommandEqSplitArgs:
					context = sharpParser.startEqSplitCommandArgs();
					break;
				case ParseType.CommandEqSplit:
					context = sharpParser.startEqSplitCommand();
					break;
				default:
					context = sharpParser.startPlainString();
					break;
			}

			// Analyze the parse tree for semantic information
			return AnalyzeSemanticTokens(context, bufferedTokenSpanStream, plaintext.ToString());
		}
		catch (RecognitionException)
		{
			// If parsing fails, fall back to syntactic tokens
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
		ParserRuleContext context,
		BufferedTokenSpanStream tokenStream,
		string sourceText)
	{
		// Single tree walk: classify every terminal by its immediate parse-tree parent context.
		// This is the canonical correct approach — it handles all tokens that appear in multiple
		// grammatical roles (CCARET, EQUALS, COMMAWS, SEMICOLON, FUNCHAR, …) without per-symbol
		// special-case pre-walks.
		var classifications = new Dictionary<int, (SemanticTokenType Type, SemanticTokenModifier Mod)>();
		CollectTerminalClassifications(context, classifications, sourceText);

		var semanticTokens = new List<SemanticToken>();
		foreach (var token in tokenStream.tokens.Where(t => t.Type != TokenConstants.EOF))
		{
			if (!classifications.TryGetValue(token.TokenIndex, out var info))
				info = (SemanticTokenType.Text, SemanticTokenModifier.None);

			semanticTokens.Add(new SemanticToken
			{
				Range = new LspRange
				{
					Start = new Position(token.Line - 1, token.Column),
					End = new Position(token.Line - 1, token.Column + token.Text.Length)
				},
				TokenType = info.Type,
				Modifiers = info.Mod,
				Text = token.Text
			});
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
			// ── Structural characters used as *literal text* (not as operators) ──────────────
			// CCARET (>), EQUALS (=), COMMAWS (,), SEMICOLON (;), CPAREN ()) appear here when
			// they are NOT serving as argument separators, delimiters or register-close markers.
			// OTHER inside beginGenericText still needs content-based classification
			// (e.g. #1234 is ObjectReference, "42" is Number).
			SharpMUSHParser.BeginGenericTextContext when token.Type != SharpMUSHParser.OTHER
				=> SemanticTokenType.Text,
			SharpMUSHParser.BeginGenericTextContext
				=> ClassifyOther(token.Text, sourceText),

			// A FUNCHAR appearing in genericText (not inside a function call) is plain text.
			SharpMUSHParser.GenericTextContext
				=> SemanticTokenType.Text,

			// ── Function ──────────────────────────────────────────────────────────────────────
			// FUNCHAR is the open-paren+name; COMMAWS and CPAREN inside the function are operators.
			SharpMUSHParser.FunctionContext when token.Type == SharpMUSHParser.FUNCHAR
				=> ClassifyFunction(token.Text),
			SharpMUSHParser.FunctionContext
				=> SemanticTokenType.Operator,

			// ── Bracket [ ] substitution ──────────────────────────────────────────────────────
			SharpMUSHParser.BracketPatternContext
				=> SemanticTokenType.BracketSubstitution,

			// ── Brace { } group ───────────────────────────────────────────────────────────────
			SharpMUSHParser.BracePatternContext
				=> SemanticTokenType.BraceGroup,

			// ── ANSI escape codes ─────────────────────────────────────────────────────────────
			SharpMUSHParser.AnsiContext
				=> SemanticTokenType.AnsiCode,

			// ── Backslash escape sequences ────────────────────────────────────────────────────
			SharpMUSHParser.EscapedTextContext
				=> SemanticTokenType.EscapeSequence,

			// ── %q<register> — opening token (q<) and closing > are both Register ────────────
			SharpMUSHParser.ComplexSubstitutionSymbolContext
				=> SemanticTokenType.Register,

			// ── %x substitution codes — all single-char codes including %=, %# etc. ──────────
			// EQUALS here means %=; DBREF means %#; CALLED_DBREF means %@ — all Substitution.
			SharpMUSHParser.SubstitutionSymbolContext
				=> SemanticTokenType.Substitution,

			// ── The leading % of any %x substitution ─────────────────────────────────────────
			// PERCENT is the only direct terminal child of ExplicitEvaluationStringContext.
			SharpMUSHParser.ExplicitEvaluationStringContext
			or SharpMUSHParser.BraceExplicitEvaluationStringContext
				=> SemanticTokenType.Substitution,

			// ── Structural operators — separators that act at command/argument scope ─────────
			SharpMUSHParser.StartEqSplitCommandContext
			or SharpMUSHParser.StartEqSplitCommandArgsContext
				=> SemanticTokenType.Operator,   // EQUALS acting as command split

			SharpMUSHParser.CommaCommandArgsContext
				=> SemanticTokenType.Operator,   // COMMAWS acting as argument separator

			SharpMUSHParser.CommandListContext
				=> SemanticTokenType.Operator,   // SEMICOLON acting as command separator

			// ── Fallback for context-independent tokens ───────────────────────────────────────
			_ => ClassifyByTokenType(token, sourceText)
		};
	}

	/// <summary>
	/// Classifies tokens whose semantic meaning does not depend on parse-tree context.
	/// Called only as a fallback from <see cref="ClassifyTerminalInContext"/>.
	/// </summary>
	private SemanticTokenType ClassifyByTokenType(IToken token, string sourceText)
	{
		var vocabulary = new SharpMUSHLexer(new AntlrInputStreamSpan(ReadOnlyMemory<char>.Empty, "")).Vocabulary;
		return vocabulary.GetSymbolicName(token.Type) switch
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
		// Remove the opening parenthesis to get the function name
		var functionName = functionText.TrimEnd('(', ' ', '\t', '\r', '\n', '\f');

		// Check if it's a built-in function
		if (FunctionLibrary.TryGetValue(functionName.ToLower(), out var functionInfo))
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
		// Check if it's a number
		if (int.TryParse(text, out _) || double.TryParse(text, out _))
		{
			return SemanticTokenType.Number;
		}

		// Check if it's an object reference (dbref)
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

		// Mark built-in functions and substitutions as default library
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
	internal static void RewriteOrphanedBracketClosers(BufferedTokenSpanStream tokenStream)
	{
		var tokens = tokenStream.tokens;
		var depth = 0;
		var pendingEscapedOpeners = 0;

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
					// Closes a real bracket — decrement depth
					depth--;
				}
				else if (pendingEscapedOpeners > 0)
				{
					// Orphaned CBRACK at depth 0 matching an escaped opener
					if (token is IWritableToken writable)
					{
						writable.Type = SharpMUSHLexer.OTHER;
					}
					pendingEscapedOpeners--;
				}
			}
			else if (depth == 0
				&& token.Type == SharpMUSHLexer.ESCAPE
				&& i + 1 < tokens.Count
				&& tokens[i + 1].Type == SharpMUSHLexer.ANY
				&& tokens[i + 1].Text == "[")
			{
				// Escaped bracket opener at depth 0
				pendingEscapedOpeners++;
			}
		}
	}

	internal static void RewriteOrphanedBraceClosers(BufferedTokenSpanStream tokenStream)
	{
		var tokens = tokenStream.tokens;
		var depth = 0;
		var pendingEscapedOpeners = 0;

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
					// Closes a real brace — decrement depth
					depth--;
				}
				else if (pendingEscapedOpeners > 0)
				{
					// Orphaned CBRACE at depth 0 matching an escaped opener
					if (token is IWritableToken writable)
					{
						writable.Type = SharpMUSHLexer.OTHER;
					}
					pendingEscapedOpeners--;
				}
			}
			else if (depth == 0
				&& token.Type == SharpMUSHLexer.ESCAPE
				&& i + 1 < tokens.Count
				&& tokens[i + 1].Type == SharpMUSHLexer.ANY
				&& tokens[i + 1].Text == "{")
			{
				// Escaped brace opener at depth 0
				pendingEscapedOpeners++;
			}
		}
	}
}