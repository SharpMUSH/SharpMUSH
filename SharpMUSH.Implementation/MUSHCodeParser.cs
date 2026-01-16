using System.Collections.Immutable;
using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
	
	// Libraries are retrieved from scoped services on-demand to avoid DI lifetime issues
	public LibraryService<string, FunctionDefinition> FunctionLibrary => 
		ServiceProvider.GetRequiredService<LibraryService<string, FunctionDefinition>>();
	
	public LibraryService<string, CommandDefinition> CommandLibrary => 
		ServiceProvider.GetRequiredService<LibraryService<string, CommandDefinition>>();
	
	// Command trie is built lazily on first access since it depends on scoped CommandLibrary
	private CommandTrie? _commandTrie;
	
	/// <summary>
	/// Gets the command trie for efficient prefix-based command lookups.
	/// </summary>
	public CommandTrie CommandTrie => _commandTrie ??= BuildCommandTrie(CommandLibrary);
	
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

	public IMUSHCodeParser FromState(ParserState state) => new MUSHCodeParser(Logger, Configuration, ServiceProvider, state);
	
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
		IOptionsWrapper<SharpMUSHOptions> config,
		IServiceProvider serviceProvider,
		ParserState state) : this(logger, config, serviceProvider)
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
			_ => PredictionMode.LL // Default to LL
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
		=> ParseInternal(text, p => p.startCommandString(), nameof(CommandListParse));

	public Func<ValueTask<CallState?>> CommandListParseVisitor(MString text)
	{
		var plaintext = MModule.plainText(text);
		AntlrInputStreamSpan inputStream = new(plaintext.AsMemory(), nameof(CommandListParseVisitor));
		SharpMUSHLexer sharpLexer = new(inputStream);
		BufferedTokenSpanStream bufferedTokenSpanStream = new(sharpLexer);
		bufferedTokenSpanStream.Fill();
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
		
		SharpMUSHParserVisitor visitor = new(Logger, this, 
			Configuration,
			_mediator,
			_notifyService,
			_connectionService,
			_locateService,
			_commandDiscoveryService,
			_attributeService,
			_hookService,text);

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
		// Set current Commands/Functions instances for this scope
		// This enables static command/function methods to access the scoped instances
		var commands = ServiceProvider.GetRequiredService<ILibraryProvider<CommandDefinition>>() as Commands.Commands;
		var functions = ServiceProvider.GetRequiredService<ILibraryProvider<FunctionDefinition>>() as Functions.Functions;
		
		if (commands != null) Commands.Commands.SetCurrentInstance(commands);
		if (functions != null) Functions.Functions.SetCurrentInstance(functions);
		
		var handleId = connectionService.Get(handle);
		var newParser = Push(new ParserState(
			Registers: new([[]]),
			IterationRegisters: [],
			RegexRegisters: [],
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
			LimitExceeded: new LimitExceededFlag()));

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
		// Set current Commands/Functions instances for this scope
		// This enables static command/function methods to access the scoped instances
		var commands = ServiceProvider.GetRequiredService<ILibraryProvider<CommandDefinition>>() as Commands.Commands;
		var functions = ServiceProvider.GetRequiredService<ILibraryProvider<FunctionDefinition>>() as Functions.Functions;
		
		if (commands != null) Commands.Commands.SetCurrentInstance(commands);
		if (functions != null) Functions.Functions.SetCurrentInstance(functions);
		
		var result = await ParseInternal(text, p => p.startSingleCommandString(), nameof(CommandParse));

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
		var semanticTokens = new List<SemanticToken>();
		
		// Access the internal token list from BufferedTokenSpanStream
		var tokenList = tokenStream.tokens;

		foreach (var token in tokenList.Where(t => t.Type != TokenConstants.EOF))
		{
			var semanticType = ClassifyToken(token, context, sourceText);
			var modifiers = GetTokenModifiers(token, semanticType);

			var range = new LspRange
			{
				Start = new Position(token.Line - 1, token.Column),
				End = new Position(token.Line - 1, token.Column + token.Text.Length)
			};

			semanticTokens.Add(new SemanticToken
			{
				Range = range,
				TokenType = semanticType,
				Modifiers = modifiers,
				Text = token.Text
			});
		}

		return semanticTokens;
	}

	/// <summary>
	/// Classifies a token to determine its semantic type.
	/// </summary>
	private SemanticTokenType ClassifyToken(IToken token, ParserRuleContext context, string sourceText)
	{
		var tokenType = token.Type;
		var vocabulary = new SharpMUSHLexer(new AntlrInputStreamSpan(ReadOnlyMemory<char>.Empty, "")).Vocabulary;
		var symbolicName = vocabulary.GetSymbolicName(tokenType);

		return symbolicName switch
		{
			"FUNCHAR" => ClassifyFunction(token.Text),
			"PERCENT" => SemanticTokenType.Substitution,
			"ARG_NUM" or "VWX" or "REG_NUM" or "REG_STARTCARET" => SemanticTokenType.Register,
			"ENACTOR_NAME" or "CAP_ENACTOR_NAME" or "ACCENT_NAME" or "MONIKER_NAME" => SemanticTokenType.Substitution,
			"SUB_PRONOUN" or "OBJ_PRONOUN" or "POS_PRONOUN" or "ABS_POS_PRONOUN" => SemanticTokenType.Substitution,
			"CALLED_DBREF" or "EXECUTOR_DBREF" or "LOCATION_DBREF" or "DBREF" => SemanticTokenType.ObjectReference,
			"OBRACK" or "CBRACK" => SemanticTokenType.BracketSubstitution,
			"OBRACE" or "CBRACE" => SemanticTokenType.BraceGroup,
			"ESCAPE" => SemanticTokenType.EscapeSequence,
			"OANSI" or "CANSI" or "ANSICHARACTER" => SemanticTokenType.AnsiCode,
			"EQUALS" or "COMMAWS" or "SEMICOLON" or "CCARET" => SemanticTokenType.Operator,
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
}