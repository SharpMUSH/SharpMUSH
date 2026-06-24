using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OneOf.Types;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Implementation.Visitors;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
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
/// <item><description>Custom span-based streams and token factory (BufferedTokenSpanStream, StringSpanInputStream, OptimizedTokenFactory) minimize allocations</description></item>
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
	private readonly IMediator _mediator = ServiceProvider.GetRequiredService<IMediator>();
	private readonly INotifyService _notifyService = ServiceProvider.GetRequiredService<INotifyService>();
	private readonly IConnectionService _connectionService = ServiceProvider.GetRequiredService<IConnectionService>();
	private readonly ILocateService _locateService = ServiceProvider.GetRequiredService<ILocateService>();
	private readonly ICommandDiscoveryService _commandDiscoveryService = ServiceProvider.GetRequiredService<ICommandDiscoveryService>();
	private readonly IAttributeService _attributeService = ServiceProvider.GetRequiredService<IAttributeService>();
	private readonly IHookService _hookService = ServiceProvider.GetRequiredService<IHookService>();

	// Lexer vocabulary is static and immutable — cached once to avoid allocating a new lexer on every fallback classification
	private static readonly IVocabulary LexerVocabulary =
		new SharpMUSHLexer(new StringSpanInputStream(string.Empty, string.Empty)).Vocabulary;

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
			if (commandInfo.IsSystem)
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
			_ => PredictionMode.LL // Default to LL for correct predicate evaluation
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
	private async ValueTask<CallState?> ParseInternal<TContext>(
		MString text,
		Func<SharpMUSHParser, TContext> entryPoint,
		string methodName,
		IMUSHCodeParser? parser = null,
		bool lenient = false)
		where TContext : ParserRuleContext
	{
		var (result, _) = await ParseInternalCore(text, entryPoint, methodName, parser, lenient);
		return result;
	}

	/// <summary>
	/// Core parse implementation that returns both the result and visitor metadata.
	/// </summary>
	private async ValueTask<(CallState? Result, bool DidEmitFunctionDebug)> ParseInternalCore<TContext>(
		MString text,
		Func<SharpMUSHParser, TContext> entryPoint,
		string methodName,
		IMUSHCodeParser? parser = null,
		bool lenient = false)
		where TContext : ParserRuleContext
	{
		parser ??= this;

		StringSpanInputStream inputStream = new(MModule.plainText(text), methodName);
		SharpMUSHLexer sharpLexer = new(inputStream)
		{
			TokenFactory = OptimizedTokenFactory.Default
		};
		BufferedTokenSpanStream bufferedTokenSpanStream = new(sharpLexer);
		bufferedTokenSpanStream.Fill();
		RewriteOrphanedBracketClosers(bufferedTokenSpanStream);
		RewriteOrphanedBraceClosers(bufferedTokenSpanStream);

		SharpMUSHParser sharpParser = new(bufferedTokenSpanStream)
		{
			Interpreter = { PredictionMode = GetPredictionMode() },
			Trace = Configuration.CurrentValue.Debug.DebugSharpParser
		};

		// Always collect syntax errors. Remove the default ConsoleErrorListener so ANTLR
		// does not print noise to stdout, then add our collecting listener.
		sharpParser.RemoveErrorListeners();
		var errorListener = new ParserErrorListener(MModule.plainText(text).ToString());
		sharpParser.AddErrorListener(errorListener);

		if (Configuration.CurrentValue.Debug.DebugSharpParser)
		{
			sharpParser.AddErrorListener(new DiagnosticErrorListener(false));
		}

		// In lenient mode, swap to LenientErrorStrategy so that synthetic recovery
		// tokens have empty text and sit at the real input boundary. This prevents
		// ANTLR's "<missing X>" annotations from polluting the stored attribute value.
		if (lenient)
		{
			sharpParser.ErrorHandler = new LenientErrorStrategy();
		}

		var context = entryPoint(sharpParser);

		// In strict mode (default for function evaluation), surface any syntax error
		// immediately as a MUSH failure string without visiting the recovery tree.
		// In lenient mode (command argument parsing), proceed to visit ANTLR's
		// error-recovery tree so the best-effort split is returned to the caller.
		if (errorListener.HasErrors && !lenient)
		{
			return (new CallState(MModule.single(errorListener.Errors[0].ToMushFailureString())), false);
		}

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

		var result = await visitor.Visit(context);
		return (result, visitor.DidEmitFunctionDebug);
	}

	public async ValueTask<CallState?> FunctionParse(MString text)
	{
		// Short-circuit: empty input (e.g. trailing comma in allof(1,2,3,)) → empty result.
		// startPlainString requires a non-empty evaluationString; passing "" would trigger PARSER FAILURE.
		if (string.IsNullOrEmpty(MModule.plainText(text)))
			return CallState.Empty;

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

		var (result, _) = await ParseInternalCore(text, p => p.startPlainString(), nameof(FunctionParse), parser);

		return result;
	}

	public async ValueTask<CallState?> FunctionParse(MString text, bool emitSubstDebug)
	{
		if (!emitSubstDebug)
			return await FunctionParse(text);

		if (string.IsNullOrEmpty(MModule.plainText(text).ToString()))
			return CallState.Empty;

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
				Executor: CurrentState.Executor,
				Enactor: CurrentState.Enactor,
				Caller: CurrentState.Caller,
				Handle: null,
				ParseMode: ParseMode.Default,
				HttpResponse: null,
				CallDepth: new InvocationCounter(),
				FunctionRecursionDepths: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
				TotalInvocations: new InvocationCounter(),
				LimitExceeded: new LimitExceededFlag()))
			: this;

		// Capture raw text BEFORE evaluation for substitution-only debug
		var rawText = MModule.plainText(text).ToString();
		var (result, didEmitFunctionDebug) = await ParseInternalCore(text, p => p.startPlainString(), nameof(FunctionParse), parser);

		// PennMUSH substitution-only debug: when the argument contains only substitutions
		// (no function calls), emit a single-line debug trace: "#dbref! raw => evaluated"
		// Only fires when function-level debug did NOT already emit for this parse.
		if (!didEmitFunctionDebug && result?.Message is not null)
		{
			var evaluatedText = result.Message.ToPlainText();
			if (rawText != evaluatedText)
			{
				var shouldDebug = false;
				AnySharpObject? executorObj = null;

				var executor = await CurrentState.ExecutorObject(_mediator);
				if (!executor.IsNone)
				{
					executorObj = executor.Known;
					var stateFlags = CurrentState.Flags;
					if (stateFlags.HasFlag(ParserStateFlags.NoDebug))
						shouldDebug = false;
					else if (stateFlags.HasFlag(ParserStateFlags.Debug))
						shouldDebug = true;
					else
						shouldDebug = await executorObj.HasFlag("DEBUG");
				}

				if (shouldDebug && executorObj is not null)
				{
					var dbrefNumber = executorObj.Object().DBRef.Number;
					var owner = await executorObj.Object().Owner.WithCancellation(CancellationToken.None);
					await _notifyService.Notify(owner, MModule.single($"#{dbrefNumber}! {rawText} => {evaluatedText}"));
				}
			}
		}

		return result;
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
		StringSpanInputStream inputStream = new(plaintext, nameof(CommandListParseVisitor));
		SharpMUSHLexer sharpLexer = new(inputStream)
		{
			TokenFactory = OptimizedTokenFactory.Default
		};
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

		sharpParser.RemoveErrorListeners();
		var errorListener = new ParserErrorListener(plaintext.ToString());
		sharpParser.AddErrorListener(errorListener);

		if (Configuration.CurrentValue.Debug.DebugSharpParser)
		{
			sharpParser.AddErrorListener(new DiagnosticErrorListener(false));
		}

		var chatContext = sharpParser.startCommandString();

		if (errorListener.HasErrors)
		{
			var failureText = MModule.single(errorListener.Errors[0].ToMushFailureString());
			return () => ValueTask.FromResult<CallState?>(new CallState(failureText));
		}

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
		=> ParseInternal(text, p => p.commaCommandArgs(), nameof(CommandCommaArgsParse),
			lenient: !CurrentState.Flags.HasFlag(ParserStateFlags.StrictParse));

	public ValueTask<CallState?> CommandSingleArgParse(MString text)
		=> ParseInternal(text, p => p.startPlainSingleCommandArg(), nameof(CommandSingleArgParse),
			lenient: !CurrentState.Flags.HasFlag(ParserStateFlags.StrictParse));

	public ValueTask<CallState?> CommandEqSplitArgsParse(MString text)
		=> ParseInternal(text, p => p.startEqSplitCommandArgs(), nameof(CommandEqSplitArgsParse),
			lenient: !CurrentState.Flags.HasFlag(ParserStateFlags.StrictParse));

	public ValueTask<CallState?> CommandEqSplitParse(MString text)
		=> ParseInternal(text, p => p.startEqSplitCommand(), nameof(CommandEqSplitParse),
			lenient: !CurrentState.Flags.HasFlag(ParserStateFlags.StrictParse));

	/// <summary>
	/// Tokenizes the input text and returns token information for syntax highlighting.
	/// </summary>
	public IReadOnlyList<TokenInfo> Tokenize(MString text)
	{
		var plaintext = MModule.plainText(text);
		StringSpanInputStream inputStream = new(plaintext, nameof(Tokenize));
		SharpMUSHLexer sharpLexer = new(inputStream)
		{
			TokenFactory = OptimizedTokenFactory.Default
		};
		BufferedTokenSpanStream bufferedTokenSpanStream = new(sharpLexer);
		bufferedTokenSpanStream.Fill();

		var tokenArray = bufferedTokenSpanStream.TokenArray;
		if (tokenArray is null || tokenArray.Length <= 1)
		{
			return [];
		}

		var tokens = new List<TokenInfo>(tokenArray.Length - 1);
		for (var i = 0; i < tokenArray.Length - 1; i++)
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
		var plaintext = MModule.plainText(text);
		StringSpanInputStream inputStream = new(plaintext, nameof(ValidateAndGetErrors));
		SharpMUSHLexer sharpLexer = new(inputStream)
		{
			TokenFactory = OptimizedTokenFactory.Default
		};
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

		var errorListener = new ParserErrorListener(plaintext.ToString());

		sharpParser.RemoveErrorListeners();
		sharpParser.AddErrorListener(errorListener);

		try
		{
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
		var plaintext = MModule.plainText(text);
		StringSpanInputStream inputStream = new(plaintext, nameof(GetSemanticTokens));
		SharpMUSHLexer sharpLexer = new(inputStream)
		{
			TokenFactory = OptimizedTokenFactory.Default
		};
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

		sharpParser.RemoveErrorListeners();

		try
		{
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

			return AnalyzeSemanticTokens(context, bufferedTokenSpanStream, plaintext.ToString());
		}
		catch (RecognitionException)
		{
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
		var tokenArray = tokenStream.TokenArray;
		var tokenCount = tokenArray is not null ? tokenArray.Length - 1 : 0;

		// Single tree walk: classify every terminal by its immediate parse-tree parent context.
		// This is the canonical correct approach — it handles all tokens that appear in multiple
		// grammatical roles (CCARET, EQUALS, COMMAWS, SEMICOLON, FUNCHAR, …) without per-symbol
		// special-case pre-walks.
		var classifications = new Dictionary<int, (SemanticTokenType Type, SemanticTokenModifier Mod)>(tokenCount);
		CollectTerminalClassifications(context, classifications, sourceText);

		var semanticTokens = new List<SemanticToken>(tokenCount);

		// Use the pre-built TokenArray (set when Fill() reaches EOF) to avoid the LINQ
		// enumerator allocation from tokenStream.tokens.Where(...). EOF is always the
		// last element in TokenArray, so iterate all-but-last.
		if (tokenArray is not null)
		{
			for (var i = 0; i < tokenArray.Length - 1; i++)
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
			SharpMUSHParser.BeginGenericTextContext when token.Type != SharpMUSHParser.OTHER
				=> SemanticTokenType.Text,
			SharpMUSHParser.BeginGenericTextContext
				=> ClassifyOther(token.Text, sourceText),

			// A FUNCHAR appearing in genericText (not inside a function call) is plain text.
			SharpMUSHParser.GenericTextContext
				=> SemanticTokenType.Text,

			// FUNCHAR is the open-paren+name; COMMAWS and CPAREN inside the function are operators.
			SharpMUSHParser.FunctionContext when token.Type == SharpMUSHParser.FUNCHAR
				=> ClassifyFunction(token.Text),
			SharpMUSHParser.FunctionContext
				=> SemanticTokenType.Operator,

			SharpMUSHParser.BracketPatternContext
				=> SemanticTokenType.BracketSubstitution,

			SharpMUSHParser.BracePatternContext
				=> SemanticTokenType.BraceGroup,

			SharpMUSHParser.AnsiContext
				=> SemanticTokenType.AnsiCode,

			SharpMUSHParser.EscapedTextContext
				=> SemanticTokenType.EscapeSequence,

			// %q<register> — opening token (q<) and closing > are both Register
			SharpMUSHParser.ComplexSubstitutionSymbolContext
				=> SemanticTokenType.Register,

			// EQUALS here means %=; DBREF means %#; CALLED_DBREF means %@ — all Substitution.
			SharpMUSHParser.SubstitutionSymbolContext
				=> SemanticTokenType.Substitution,

			// PERCENT is the only direct terminal child of ExplicitEvaluationStringContext.
			SharpMUSHParser.ExplicitEvaluationStringContext
			or SharpMUSHParser.BraceExplicitEvaluationStringContext
				=> SemanticTokenType.Substitution,

			SharpMUSHParser.StartEqSplitCommandContext
			or SharpMUSHParser.StartEqSplitCommandArgsContext
				=> SemanticTokenType.Operator,

			SharpMUSHParser.CommaCommandArgsContext
				=> SemanticTokenType.Operator,

			SharpMUSHParser.CommandListContext
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

		if (FunctionLibrary.TryGetValue(functionName, out var functionInfo)
			|| FunctionLibrary.TryGetValue(functionName.ToLowerInvariant(), out functionInfo))
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
					depth--;
				}
				else
				{
					// Orphaned CBRACK at depth 0 — treat as literal ']'
					if (token is IWritableToken writable)
					{
						writable.Type = SharpMUSHLexer.OTHER;
					}
					if (pendingEscapedOpeners > 0)
						pendingEscapedOpeners--;
				}
			}
			else if (depth == 0
				&& token.Type == SharpMUSHLexer.ESCAPE
				&& i + 1 < tokens.Count
				&& tokens[i + 1].Type == SharpMUSHLexer.ANY
				&& tokens[i + 1].Text == "[")
			{
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
					depth--;
				}
				else
				{
					// Orphaned CBRACE at depth 0 — treat as literal '}'
					if (token is IWritableToken writable)
					{
						writable.Type = SharpMUSHLexer.OTHER;
					}
					if (pendingEscapedOpeners > 0)
						pendingEscapedOpeners--;
				}
			}
			else if (depth == 0
				&& token.Type == SharpMUSHLexer.ESCAPE
				&& i + 1 < tokens.Count
				&& tokens[i + 1].Type == SharpMUSHLexer.ANY
				&& tokens[i + 1].Text == "{")
			{
				pendingEscapedOpeners++;
			}
		}
	}
}
