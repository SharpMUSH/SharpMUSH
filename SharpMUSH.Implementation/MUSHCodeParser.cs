using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Misc;
using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Parsing;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Implementation.Visitors;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace SharpMUSH.Implementation;

/// <summary>
/// Provides the parser for MUSH commands and functions.
/// Each call is Synchronous and Stateful at this time.
/// 
/// <para><b>Performance Optimizations:</b></para>
/// <list type="bullet">
/// <item><description>Services are resolved once at construction and cached to avoid repeated DI lookups</description></item>
/// <item><description>CommandTrie provides O(m) prefix matching where m is the length of the search string; one trie is shared per command library</description></item>
/// <item><description><see cref="SoftcodeParsePipeline"/> is the one lexer/parser setup, shared with the tooling half (<see cref="SoftcodeSyntaxAnalyzer"/>)</description></item>
/// <item><description>Custom span-based streams and token factory (BufferedTokenSpanStream, StringSpanInputStream, OptimizedTokenFactory) minimize allocations</description></item>
/// <item><description>Prediction mode can be configured (SLL vs LL) for performance vs accuracy tradeoff</description></item>
/// </list>
/// 
/// </summary>
public record MUSHCodeParser(ILogger<MUSHCodeParser> Logger,
	LibraryService<string, FunctionDefinition> FunctionLibrary,
	LibraryService<string, CommandDefinition> CommandLibrary,
	IOptionsWrapper<SharpMUSHOptions> Configuration,
	IServiceProvider ServiceProvider) : IMUSHCodeParser
{
	/// <summary>
	/// The services evaluation uses, resolved once here and shared by every state this parser pushes
	/// and every visitor it builds; see <see cref="EvaluationServices"/>.
	/// </summary>
	private readonly EvaluationServices _services = EvaluationServices.From(ServiceProvider);

	/// <summary>
	/// <see cref="_services"/>, locating its optional services in this parser's current provider when a
	/// copy of the parser was given another one.
	/// </summary>
	private EvaluationServices Services => _services.For(ServiceProvider);

	/// <summary>
	/// The command trie for prefix lookups. Shared by every parser derived from the same command
	/// library and rebuilt only when that library changes (see <see cref="CommandTrie.Invalidate"/>).
	/// </summary>
	public CommandTrie CommandTrie => CommandTrie.For(CommandLibrary);

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

	/// <summary>
	/// A parser over a single fresh state. A record copy, not a new construction: the services this
	/// record resolves in its field initialisers are singletons, and re-resolving them for every
	/// command a player types bought nothing.
	/// </summary>
	/// <remarks>
	/// A state standing alone is a new queue entry or a fresh root, so it does not keep the
	/// <c>%c</c>/<c>%u</c> of the list that captured it: PennMUSH gives a queued entry a new
	/// <c>pe_info</c>, and a captured state still points at the submitting list's text.
	/// </remarks>
	public IMUSHCodeParser FromState(ParserState state)
		=> this with { State = ImmutableStack.Create(state with { CommandText = null }) };

	public Option<ParserState> StateHistory(uint index)
		=> State.Take((int)index).LastOrDefault() is { } state ? state : new None();

	public IMUSHCodeParser Empty() => this with { State = ImmutableStack<ParserState>.Empty };

	public IMUSHCodeParser Push(ParserState state) => this with { State = State.Push(state) };

	public MUSHCodeParser(ILogger<MUSHCodeParser> logger,
		LibraryService<string, FunctionDefinition> functionLibrary,
		LibraryService<string, CommandDefinition> commandLibrary,
		IOptionsWrapper<SharpMUSHOptions> config,
		IServiceProvider serviceProvider,
		ParserState state) : this(logger, functionLibrary, commandLibrary, config, serviceProvider)
		=> State = [state];

	private static bool ContainsRestrictedEntryPoint(BufferedTokenSpanStream tokens,
		IReadOnlyDictionary<string, (FunctionDefinition LibraryInformation, bool IsSystem)> functions)
	{
		for (var index = 0; index < tokens.tokens.Count; index++)
		{
			ExecutionBudget.Current?.ThrowIfExceeded();
			var token = tokens.tokens[index];
			if (token.Type != SharpMUSHLexer.FUNCHAR) continue;
			var name = token.Text.TrimEnd()[..^1];
			if (!functions.TryGetValue(name, out var definition)) continue;
			if (definition.LibraryInformation.RestrictedOperation == "restrictedexpr") return true;
			if (definition.LibraryInformation.RestrictedOperation != "fn") continue;

			// Resolve only literal target names, using the same audited operation identities as
			// dispatch. Unknown or dynamic targets remain conservative; no evaluation or object
			// lookup is permitted before deciding whether the input may be traced.
			var uncertain = false;
			IEnumerable<string> LiteralTargets()
			{
				for (var targetIndex = index + 1; targetIndex < tokens.tokens.Count; targetIndex += 2)
				{
					ExecutionBudget.Current?.ThrowIfExceeded();
					var target = tokens.tokens[targetIndex];
					var nextType = targetIndex + 1 < tokens.tokens.Count ? tokens.tokens[targetIndex + 1].Type : TokenConstants.EOF;
					if (target.Type != SharpMUSHLexer.OTHER
						|| nextType is not (SharpMUSHLexer.COMMAWS or SharpMUSHLexer.CPAREN or TokenConstants.EOF)
						|| !functions.ContainsKey(target.Text))
					{
						uncertain = true;
						yield break;
					}
					yield return target.Text;
					if (nextType != SharpMUSHLexer.COMMAWS) break;
				}
				uncertain = true;
			}
			if (EvaluationRestrictions.BeginsRestrictedEvaluation(definition.LibraryInformation, LiteralTargets(), functions)
				|| uncertain) return true;
		}
		return false;
	}

	/// <summary>
	/// Parses <paramref name="entryPoint"/> over an already-lexed token stream, applying the
	/// configured prediction strategy.
	/// <para>
	/// Under <see cref="ParserPredictionMode.TwoStage"/> (the default) it first parses with SLL and a
	/// <see cref="BailErrorStrategy"/> that aborts on the first error instead of recovering. If
	/// that succeeds with no syntax error the result stands — ANTLR guarantees SLL then matches LL.
	/// Only if SLL errors is the token stream rewound and re-parsed with LL, which is authoritative;
	/// its result and its (strict- or lenient-) recovered tree are what the caller sees. On
	/// error-free input, the common case, this is a single SLL pass. The SLL and LL settings force
	/// one mode for diagnostics.
	/// </para>
	/// A fresh parser is built per attempt, and the token stream is sought back to the start between
	/// attempts.
	/// </summary>
	private (TContext Context, ParserErrorListener Errors) ParseTwoStage<TContext>(
		BufferedTokenSpanStream tokens,
		Func<SharpMUSHParser, TContext> entryPoint,
		string inputText,
		bool lenient,
		IReadOnlyDictionary<string, (FunctionDefinition LibraryInformation, bool IsSystem)>? functions = null)
		where TContext : class, SharpMUSHParser.ISoftcodeContext
	{
		// Token inspection precedes ANTLR tracing, including malformed input with no visitor.
		// Literal indirect chains use the same restricted-entry classification as dispatch.
		var debug = Configuration.CurrentValue.Debug.DebugSharpParser && EvaluationRestrictions.Current is null
			&& !ContainsRestrictedEntryPoint(tokens, functions ?? FunctionLibrary);

		(SharpMUSHParser Parser, ParserErrorListener Errors) Build(PredictionMode mode, IAntlrErrorStrategy strategy)
		{
			tokens.Seek(0);
			var parser = SoftcodeParsePipeline.CreateParser(tokens, Configuration.CurrentValue.Compatibility.ParenGroups,
				mode, trace: debug);
			parser.ErrorHandler = strategy;
			var errors = new ParserErrorListener(inputText);
			parser.AddErrorListener(errors);
			if (debug)
			{
				parser.AddErrorListener(new DiagnosticErrorListener(false));
			}

			return (parser, errors);
		}

		// The strategy the authoritative pass uses: recover-and-report for command arguments
		// (lenient), plain recovery whose errors the caller turns into a failure string otherwise.
		IAntlrErrorStrategy AuthoritativeStrategy() =>
			lenient ? new LenientErrorStrategy() : new DefaultErrorStrategy();

		if (Configuration.CurrentValue.Debug.ParserPredictionMode == ParserPredictionMode.TwoStage)
		{
			var (sllParser, sllErrors) = Build(PredictionMode.SLL, new BailErrorStrategy());
			try
			{
				var sllContext = entryPoint(sllParser);
				if (!sllErrors.HasErrors)
				{
					return (sllContext, sllErrors);
				}
			}
			catch (ParseCanceledException)
			{
				// SLL could not parse this input; the LL pass below decides whether that is a real
				// syntax error or only SLL's weaker analysis giving up.
			}

			if (debug)
			{
				Logger.LogDebug("SLL parse fell back to LL for input of length {Length}", inputText.Length);
			}

			var (llParser, llErrors) = Build(PredictionMode.LL, AuthoritativeStrategy());
			return (entryPoint(llParser), llErrors);
		}

		var (singleParser, singleErrors) = Build(SoftcodeSyntaxAnalyzer.SinglePassPredictionMode(Configuration.CurrentValue), AuthoritativeStrategy());
		return (entryPoint(singleParser), singleErrors);
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
		where TContext : class, SharpMUSHParser.ISoftcodeContext
	{
		var (result, _) = await ParseInternalCore(text, entryPoint, methodName, parser, lenient);
		return result;
	}

	/// <summary>
	/// Core parse implementation that returns both the result and visitor metadata.
	/// </summary>
	private async ValueTask<(CallState? Result, bool SuppressSubstitutionOnlyDebugTrace)> ParseInternalCore<TContext>(
		MString text,
		Func<SharpMUSHParser, TContext> entryPoint,
		string methodName,
		IMUSHCodeParser? parser = null,
		bool lenient = false)
		where TContext : class, SharpMUSHParser.ISoftcodeContext
	{
		parser ??= this;
		using var precisionScope = Configurable.UseFloatPrecisionOf(Configuration);
		using var restrictionScope = parser.State.IsEmpty ? null : parser.CurrentState.Restrictions?.Enter();
		using var ceilingScope = parser.State.IsEmpty ? null : OutputCeiling.Enter(parser.CurrentState);
		if (EvaluationRestrictions.Current is not null && methodName != nameof(FunctionParse))
			return (new CallState(EvaluationRestrictions.Error) { HadErrors = true }, true);
		using var ownedBudget = ExecutionBudget.Current is null && (parser.State.IsEmpty || parser.CurrentState.ExecutionBudget is null)
		 ? ExecutionBudget.FromMilliseconds(Configuration.CurrentValue.Limit.QueueEntryCpuTime) : null;
		var budget = ExecutionBudget.Current ?? (parser.State.IsEmpty ? null : parser.CurrentState.ExecutionBudget) ?? ownedBudget!;
		using var budgetScope = budget.Enter();
		if (budget.IsExpired) return (new CallState(ExecutionBudget.Error) { HadErrors = true }, true);
		budget.ThrowIfExceeded();
		if (!parser.State.IsEmpty) parser = parser.Push(parser.CurrentState with { ExecutionBudget = budget });

		var plainText = text.ToPlainText();

		// The same text under the same settings parses to the same tree, and code run once per item
		// (u(), filter(), iter()) is the same text every time. Only evaluated code and queued action
		// lists are kept: a typed command line is rarely repeated, and some carry a password (CONNECT,
		// @password) that must not outlive the command. A tracing parse is never shared.
		var options = Configuration.CurrentValue;
		var cache = options.Debug.DebugSharpParser || plainText.Length > SoftcodeParseCache.MaxTextLength
			|| methodName is not (nameof(FunctionParse) or nameof(CommandListParse))
			? null
			: Services.ParseCache;
		var key = new SoftcodeParseCache.Key(plainText, methodName, lenient,
			options.Compatibility.ParenGroups, options.Debug.ParserPredictionMode);

		TContext context;
		ParserErrorListener errorListener;
		if (cache is not null && cache.TryGet(key, out var parsed))
		{
			context = (TContext)parsed.Context;
			errorListener = parsed.Errors;
		}
		else
		{
			var bufferedTokenSpanStream = SoftcodeParsePipeline.Lex(plainText, methodName);

			// Refuse pathologically nested input before the recursive-descent parser overflows the
			// stack (see SoftcodeParsePipeline.MaxParseNestingDepth). Reported as the call-limit error,
			// matching PennMUSH's call_limit, which is the same guard against the same crash.
			if (SoftcodeParsePipeline.ExceedsNestingLimit(bufferedTokenSpanStream, SoftcodeParsePipeline.MaxParseNestingDepth,
				options.Compatibility.ParenGroups, out _))
			{
				return (new CallState(MarkupText.Plain(ErrorMessages.Returns.Call)) { HadErrors = true }, true);
			}

			// Two-stage SLL/LL prediction with strict/lenient recovery. The error listener is the one
			// from whichever pass produced the returned tree, and lenient parses run LenientErrorStrategy
			// so recovery tokens carry empty text at the real input boundary rather than "<missing X>".
			try
			{
				(context, errorListener) = ParseTwoStage(
					bufferedTokenSpanStream, entryPoint, plainText, lenient, parser.FunctionLibrary);
			}
			catch (OperationCanceledException) when (budget.IsExpired)
			{
				// Parsing and diagnostic classification share the visitor's deadline contract.
				return (new CallState(ExecutionBudget.Error) { HadErrors = true }, true);
			}

			cache?.Add(key, new SoftcodeParseCache.Entry(context, errorListener));
		}

		// In strict mode (default for function evaluation), surface any syntax error
		// immediately as a MUSH failure string without visiting the recovery tree.
		// No visitor can classify private wrapper inputs on this path, so never forward raw failure text.
		// In lenient mode (command argument parsing), proceed to visit ANTLR's
		// error-recovery tree so the best-effort split is returned to the caller.
		if (errorListener.HasErrors && !lenient)
		{
			return (new CallState(MarkupText.Plain(errorListener.Errors[0].ToMushFailureString())) { HadErrors = true, IsParseFailure = true }, true);
		}

		SharpMUSHParserVisitor visitor = new(Logger, parser, Configuration, Services, text);

		CallState? result;
		try { result = await visitor.Visit(context); }
		catch (RestrictedExpressionException ex)
		{ return (new CallState(ex.Message) { HadErrors = true }, visitor.SuppressSubstitutionOnlyDebugTrace); }
		catch (OperationCanceledException) when (budget.IsExpired)
		{ return (new CallState(ExecutionBudget.Error) { HadErrors = true }, visitor.SuppressSubstitutionOnlyDebugTrace); }
		if (budget.IsExpired) return (new CallState(ExecutionBudget.Error) { HadErrors = true }, visitor.SuppressSubstitutionOnlyDebugTrace);
		budget.ThrowIfExceeded();

		// A lenient parse can reach here having still hit a syntax error: LenientErrorStrategy
		// recovers and lets the visitor walk a best-effort tree instead of throwing, so
		// errorListener.HasErrors can be true even though `result` is non-null. Tag it on the
		// returned CallState (mirroring ArgumentContexts riding along the same way) so a caller
		// that walks the retained tree directly instead of re-parsing — see
		// CommandArgumentSplitter.EvaluateArgumentSubtree — knows this tree is only a recovered
		// best-effort parse and cannot be trusted as a substitute for a strict re-parse.
		if (errorListener.HasErrors && result is not null)
		{
			result = result with { HadErrors = true };
		}

		return (result, visitor.SuppressSubstitutionOnlyDebugTrace);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// It pushes a fresh state only at a genuinely top-level entry point: <see cref="State"/> is empty,
	/// or the top frame predates any tracking counters. <see cref="FunctionParse(MString)"/>,
	/// <see cref="FunctionParse(MString, bool)"/> and <c>CommandArgumentSplitter.EvaluateArgumentSubtree</c>
	/// (which evaluates a retained subtree without re-lexing it) share this one decision.
	/// <para>
	/// Executor/Enactor/Caller are always carried over from <see cref="CurrentState"/>. The no-debug
	/// <see cref="FunctionParse(MString)"/> overload used to drop them, so a top-level parse entered
	/// with actors but without tracking counters evaluated every function against a null executor —
	/// <c>FunctionInvocationPipeline</c>'s permission gate then threw out of <c>KnownExecutorObject</c> on every
	/// single call, was caught, logged with a full stack trace, and returned an empty result. The
	/// nightly benchmark run that flushed this out logged two million of those stack traces.
	/// </para>
	/// </remarks>
	public IMUSHCodeParser ForTrackedEvaluation()
	{
		var needsTracking = State.IsEmpty || CurrentState.TotalInvocations == null;
		if (!needsTracking)
		{
			return this;
		}

		// CurrentState => State.Peek() throws on an empty stack, so only read the actors when there
		// IS a frame to read them from.
		return Push(ParserState.ForTrackedEvaluation(State.IsEmpty ? null : CurrentState));
	}

	/// <inheritdoc/>
	public bool LocatesOptionalServices => true;

	public async ValueTask<CallState?> FunctionParse(MString text)
	{
		// Short-circuit: empty input (e.g. trailing comma in allof(1,2,3,)) → empty result.
		// startPlainString requires a non-empty evaluationString; passing "" would trigger PARSER FAILURE.
		if (string.IsNullOrEmpty(text.ToPlainText()))
			return CallState.Empty;

		var parser = ForTrackedEvaluation();

		var (result, _) = await ParseInternalCore(text, p => p.StartPlainString(), nameof(FunctionParse), parser);

		return result;
	}

	public async ValueTask<CallState?> FunctionParse(MString text, bool emitSubstDebug)
	{
		if (!emitSubstDebug)
			return await FunctionParse(text);

		if (string.IsNullOrEmpty(text.ToPlainText()))
			return CallState.Empty;

		// Completion diagnostics belong to the same operation as parsing. Keep its original
		// deadline alive through the final awaited read/notification, including standalone calls.
		using var ownedBudget = ExecutionBudget.Current is null && (State.IsEmpty || CurrentState.ExecutionBudget is null)
			? ExecutionBudget.FromMilliseconds(Configuration.CurrentValue.Limit.QueueEntryCpuTime) : null;
		var budget = ExecutionBudget.Current ?? (State.IsEmpty ? null : CurrentState.ExecutionBudget) ?? ownedBudget!;
		using var budgetScope = budget.Enter();
		try
		{
			budget.ThrowIfExceeded();
			var parser = ForTrackedEvaluation();
			var rawText = text.ToPlainText();
			var (result, suppressSubstitutionDebug) = await ParseInternalCore(text, p => p.StartPlainString(), nameof(FunctionParse), parser);
			budget.ThrowIfExceeded();

			await EvaluationDiagnostics.EmitSubstitutionOnlyDebugTraceAsync(_services.Mediator, _services.NotifyService, State.IsEmpty ? parser.CurrentState : CurrentState,
				rawText, result?.Message, suppressSubstitutionDebug);
			budget.ThrowIfExceeded();
			return result;
		}
		catch (OperationCanceledException) when (budget.IsExpired)
		{
			return new CallState(ExecutionBudget.Error) { HadErrors = true };
		}
	}

	public ValueTask<CallState?> CommandListParse(MString text)
	{
		// Push a fresh CommandHistory so @retry can track previous commands in this parse session.
		// Also clear DirectInput: a CommandListParse is always a queue/callback context, never
		// direct player input (equivalent to PennMUSH dropping the QUEUE_NOLIST flag here).
		// A list run from inside a command shares its %c/%u (PE_INFO_SHARE); a queued one arrives
		// through FromState without any, and starts its own.
		var depth = 0;
		if (!State.IsEmpty)
		{
			if (NestedInplaceDepth(CurrentState) is not { } nested) return ValueTask.FromResult<CallState?>(CallState.Empty);
			depth = nested;
		}
		var freshParser = State.IsEmpty ? this : Push(CurrentState with
		{
			CommandHistory = new ConcurrentStack<(Func<IMUSHCodeParser, ValueTask<Option<CallState>>> Invoker, Dictionary<string, CallState> Args)>(),
			Flags = CurrentState.Flags & ~ParserStateFlags.DirectInput,
			CommandText = CurrentState.CommandText ?? CurrentState.NewCommandText(),
			InplaceDepth = depth
		});
		return ParseInternal(text, p => p.StartCommandString(), nameof(CommandListParse), freshParser);
	}

	/// <summary>
	/// The <see cref="ParserState.InplaceDepth"/> a list run from <paramref name="state"/> has, or
	/// nothing when it would nest deeper than <see cref="ParserState.MaxInplaceDepth"/> and is dropped.
	/// A state that has no <c>%c</c>/<c>%u</c> yet is a queue entry's own list, not one in place.
	/// </summary>
	private static int? NestedInplaceDepth(ParserState state)
	{
		if (state.CommandText is null) return 0;
		var depth = state.InplaceDepth + 1;
		return depth > ParserState.MaxInplaceDepth ? null : depth;
	}

	public Func<ValueTask<CallState?>> CommandListParseVisitor(MString text)
	{
		// The same in-place nesting bound as CommandListParse, checked before any lexing.
		var depth = 0;
		if (!State.IsEmpty)
		{
			if (NestedInplaceDepth(CurrentState) is not { } nested) return () => ValueTask.FromResult<CallState?>(CallState.Empty);
			depth = nested;
		}

		var plaintext = text.ToPlainText();
		var bufferedTokenSpanStream = SoftcodeParsePipeline.Lex(plaintext, nameof(CommandListParseVisitor));

		if (SoftcodeParsePipeline.ExceedsNestingLimit(bufferedTokenSpanStream, SoftcodeParsePipeline.MaxParseNestingDepth,
			Configuration.CurrentValue.Compatibility.ParenGroups, out _))
		{
			return () => ValueTask.FromResult<CallState?>(new CallState(MarkupText.Plain(ErrorMessages.Returns.Call)) { HadErrors = true });
		}

		SharpMUSHParser.IStartCommandStringContext chatContext;
		ParserErrorListener errorListener;
		try
		{
			(chatContext, errorListener) = ParseTwoStage(
				bufferedTokenSpanStream, p => p.StartCommandString(), plaintext, lenient: false);
		}
		catch (OperationCanceledException) when (ExecutionBudget.Current?.IsExpired == true)
		{
			return () => ValueTask.FromResult<CallState?>(new CallState(ExecutionBudget.Error) { HadErrors = true });
		}

		if (errorListener.HasErrors)
		{
			var failureText = MarkupText.Plain(errorListener.Errors[0].ToMushFailureString());
			return () => ValueTask.FromResult<CallState?>(new CallState(failureText) { HadErrors = true });
		}

		// Clear DirectInput for the same reason as CommandListParse: this visitor is always
		// used in a queue/callback context (e.g., @force, @trigger), never for direct player input.
		var parserForList = State.IsEmpty ? this : Push(CurrentState with
		{
			Flags = CurrentState.Flags & ~ParserStateFlags.DirectInput,
			CommandText = CurrentState.CommandText ?? CurrentState.NewCommandText(),
			InplaceDepth = depth
		});

		SharpMUSHParserVisitor visitor = new(Logger, parserForList, Configuration, Services, text);

		return async () =>
		{
			using var precisionScope = Configurable.UseFloatPrecisionOf(Configuration);
			return await visitor.Visit(chatContext);
		};
	}

	/// <summary>A handle not yet logged in has no player, and gets the full ceiling.</summary>
	private async ValueTask<int> OutputLimitForAsync(DBRef? player)
		=> await FunctionLimits.OutputLimitForAsync(
			player is { } dbref && await _services.Mediator.Send(new GetObjectNodeQuery(dbref)) is AnySharpObject actor ? actor : null,
			Configuration.CurrentValue.Limit.GuestOutputLimit);

	/// <summary>
	/// This is the main entry point for commands run by a player.
	/// </summary>
	/// <param name="handle">The handle that identifies the connection.</param>
	/// <param name="text">The text to parse.</param>
	/// <returns>A completed task.</returns>
	public async ValueTask<CallState> CommandParse(long handle, IConnectionService connectionService, MString text)
	{
		var handleId = connectionService.Get(handle);
		var expectedSession = State.IsEmpty ? null : CurrentState.ConnectionSessionId;
		if (!string.IsNullOrEmpty(expectedSession) &&
			handleId?.Metadata.GetValueOrDefault("SessionId") != expectedSession) return CallState.Empty;
		var player = handleId?.Ref;
		var session = handleId?.Metadata.GetValueOrDefault("SessionId");
		var outputLimit = await OutputLimitForAsync(player);
		// The lookup awaited. A login, logout or reconnect on the handle meanwhile makes this someone
		// else's command, which must not run with the player read above.
		var current = connectionService.Get(handle);
		if (current?.Ref != player || current?.Metadata.GetValueOrDefault("SessionId") != session) return CallState.Empty;
		var newParser = Push(ParserState.ForTypedLine(player, handle, expectedSession, outputLimit));

		var result = await ParseInternal(text, p => p.StartSingleCommandString(), nameof(CommandParse), newParser);
		// Nothing in a line that does not parse ran, so nothing else answers it: say why, as Huh? would be
		// said, rather than nothing. Before login there is only the connection to tell.
		if (result is { IsParseFailure: true, Message: { } failure })
		{
			await (player is { } typist
				? _services.NotifyService.Notify(typist, failure)
				: _services.NotifyService.Notify(handle, failure));
		}

		return result ?? CallState.Empty;
	}

	/// <summary>
	/// A line typed by <paramref name="player"/> in the web portal, which has no connection to type it at.
	/// It runs as one typed at a connection does — <see cref="ParserStateFlags.DirectInput"/>, so it is
	/// not split on semicolons and a <c>$</c>-command it matches runs in place — with no handle, so
	/// nothing answers a descriptor that does not exist.
	/// </summary>
	public async ValueTask<CallState> CommandParse(DBRef player, MString text)
	{
		var outputLimit = await OutputLimitForAsync(player);
		var newParser = Push(ParserState.ForTypedLine(player, handle: null, session: null, outputLimit));
		var result = await ParseInternal(text, p => p.StartSingleCommandString(), nameof(CommandParse), newParser);
		if (result is { IsParseFailure: true, Message: { } failure })
		{
			await _services.NotifyService.Notify(player, failure);
		}

		return result ?? CallState.Empty;
	}

	/// <summary>
	/// This is the main entry point for commands run by a player.
	/// </summary>
	/// <param name="text">The text to parse.</param>
	/// <returns>A completed task.</returns>
	public async ValueTask<CallState> CommandParse(MString text)
	{
		// A command redispatched from another one (`]`, `~`, a speech token, WITH, TEACH) keeps the
		// origin it inherited: DirectInput only if the running command itself came from a connection.
		// A Handle alone is no proof — queued work keeps the connection it was started from — so
		// re-deriving the flag from it would run a queued list's $-matches in place (#1132). No Handle
		// means a programmatic or queue context where the RHS of & should be evaluated.
		var baseFlags = State.IsEmpty ? ParserStateFlags.None : CurrentState.Flags;
		var handle = State.IsEmpty ? null : CurrentState.Handle;
		var derivedFlags = handle.HasValue
			? baseFlags
			: baseFlags & ~ParserStateFlags.DirectInput;

		var parserToUse = State.IsEmpty ? this : Push(CurrentState with
		{
			Flags = derivedFlags,
			CommandText = CurrentState.CommandText ?? CurrentState.NewCommandText()
		});
		var result = await ParseInternal(text, p => p.StartSingleCommandString(), nameof(CommandParse), parserToUse);
		return result ?? CallState.Empty;
	}

	// Enter through the EOF-anchored rule, as the diagnostic and semantic-token paths already do.
	// commaCommandArgs on its own can stop early and report success on a prefix, silently dropping
	// whatever followed instead of surfacing it to the lenient recovery path.
	public ValueTask<CallState?> CommandCommaArgsParse(MString text)
		=> ParseInternal(text, p => p.StartPlainCommaCommandArgs(), nameof(CommandCommaArgsParse),
			lenient: !CurrentState.Flags.HasFlag(ParserStateFlags.StrictParse));

	public ValueTask<CallState?> CommandSingleArgParse(MString text)
		=> ParseInternal(text, p => p.StartPlainSingleCommandArg(), nameof(CommandSingleArgParse),
			lenient: !CurrentState.Flags.HasFlag(ParserStateFlags.StrictParse));

	public ValueTask<CallState?> CommandEqSplitArgsParse(MString text)
		=> ParseInternal(text, p => p.StartEqSplitCommandArgs(), nameof(CommandEqSplitArgsParse),
			lenient: !CurrentState.Flags.HasFlag(ParserStateFlags.StrictParse));

	public ValueTask<CallState?> CommandEqSplitParse(MString text)
		=> ParseInternal(text, p => p.StartEqSplitCommand(), nameof(CommandEqSplitParse),
			lenient: !CurrentState.Flags.HasFlag(ParserStateFlags.StrictParse));

	/// <summary>The tooling half, over this parser's current function library and options.</summary>
	private SoftcodeSyntaxAnalyzer Syntax => new(FunctionLibrary, Configuration);

	/// <inheritdoc cref="SoftcodeSyntaxAnalyzer.Tokenize"/>
	public IReadOnlyList<TokenInfo> Tokenize(MString text) => Syntax.Tokenize(text);

	/// <inheritdoc cref="SoftcodeSyntaxAnalyzer.ValidateAndGetErrors"/>
	public IReadOnlyList<ParseError> ValidateAndGetErrors(MString text, ParseType parseType = ParseType.Function)
		=> Syntax.ValidateAndGetErrors(text, parseType);

	/// <inheritdoc cref="SoftcodeSyntaxAnalyzer.GetDiagnostics"/>
	public IReadOnlyList<Diagnostic> GetDiagnostics(MString text, ParseType parseType = ParseType.Function)
		=> Syntax.GetDiagnostics(text, parseType);

	/// <inheritdoc cref="SoftcodeSyntaxAnalyzer.GetSemanticTokens"/>
	public IReadOnlyList<SemanticToken> GetSemanticTokens(MString text, ParseType parseType = ParseType.Function)
		=> Syntax.GetSemanticTokens(text, parseType);

	/// <inheritdoc cref="SoftcodeSyntaxAnalyzer.GetSemanticTokensData"/>
	public SemanticTokensData GetSemanticTokensData(MString text, ParseType parseType = ParseType.Function)
		=> Syntax.GetSemanticTokensData(text, parseType);
}
