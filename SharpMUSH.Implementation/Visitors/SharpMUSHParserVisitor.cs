using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static SharpMUSHParser;
using SharpMUSH.Library.Markup;

namespace SharpMUSH.Implementation.Visitors;

/// <summary>
/// This class implements the SharpMUSHParserBaseVisitor from the Generated code.
/// If additional pieces of the parse-tree are added, the Generated project must be re-generated 
/// and new Visitors may need to be added.
/// 
/// <para>The visitor walks the tree and keeps the state of one walk — the source text, debug nesting,
/// brace depth, function-recognition suppression. What a node <em>does</em> lives in the pipelines on
/// <see cref="EvaluationServices"/>, resolved once per parser:</para>
/// <list type="bullet">
/// <item><description><see cref="FunctionInvocationPipeline"/> — a call, from resolution to telemetry</description></item>
/// <item><description><see cref="CommandDispatcher"/> — what a command is</description></item>
/// <item><description><see cref="CommandInvocationPipeline"/> — running it, hooks and all</description></item>
/// <item><description><see cref="CommandArgumentSplitter"/> — its arguments</description></item>
/// <item><description><see cref="EvaluationDiagnostics"/> — DEBUG and VERBOSE output</description></item>
/// </list>
/// <para><b>Performance:</b> no service is located per visit; argument and result merging run as plain
/// loops over spans; literal text is sliced from the markup-carrying source.</para>
/// </summary>
public class SharpMUSHParserVisitor : SharpMUSHParserBaseVisitor<ValueTask<CallState?>>
{
	private readonly IMUSHCodeParser parser;
	private readonly MString source;
	private readonly EvaluationServices _services;

	private int _debugNestDepth;
	private bool _didEmitFunctionDebug;
	private bool _containsRestrictedWrapper;
	private readonly Dictionary<FunctionContext, bool> _restrictedScanResults = new();
	internal bool SuppressSubstitutionOnlyDebugTrace => _didEmitFunctionDebug || _containsRestrictedWrapper;
	private int _braceDepthCounter;
	private int _suppressFunctionEval;

	/// <summary>
	/// A visitor over services given one by one. The parser builds its visitors from services it
	/// resolved once instead; this remains for callers that construct one directly.
	/// </summary>
	/// <param name="parser">The Parser, so that inner functions can force a parser-call.</param>
	/// <param name="source">The original MarkupString. A plain GetText is not good enough to get the proper value back.</param>
	public SharpMUSHParserVisitor(
		ILogger logger,
		IMUSHCodeParser parser,
		IOptionsWrapper<SharpMUSHOptions> Configuration,
		IMediator Mediator,
		INotifyService NotifyService,
		IConnectionService ConnectionService,
		ILocateService LocateService,
		ICommandDiscoveryService CommandDiscoveryService,
		IAttributeService AttributeService,
		IHookService HookService,
		ILockService LockService,
		MString source)
		: this(logger, parser, Configuration,
			new EvaluationServices(parser.ServiceProvider, Mediator, NotifyService, ConnectionService, LocateService,
				CommandDiscoveryService, AttributeService, HookService, LockService,
				locateOptional: parser.LocatesOptionalServices),
			source)
	{
	}

	/// <param name="logger">Where evaluation failures are logged.</param>
	/// <param name="parser">The Parser, so that inner functions can force a parser-call.</param>
	/// <param name="configuration">The options this evaluation runs under.</param>
	/// <param name="services">The services and pipelines the parser resolved once.</param>
	/// <param name="source">The original MarkupString. A plain GetText is not good enough to get the proper value back.</param>
	internal SharpMUSHParserVisitor(ILogger logger, IMUSHCodeParser parser,
		IOptionsWrapper<SharpMUSHOptions> configuration, EvaluationServices services, MString source)
	{
		Logger = logger;
		this.parser = parser;
		Configuration = configuration;
		_services = services;
		this.source = source;
	}

	/// <summary>The parser this visitor evaluates for.</summary>
	internal IMUSHCodeParser Parser => parser;

	/// <summary>The text the tree was parsed from, with its markup.</summary>
	internal MString Source => source;

	/// <summary>The options this evaluation runs under.</summary>
	internal IOptionsWrapper<SharpMUSHOptions> Configuration { get; }

	/// <summary>Where evaluation failures are logged.</summary>
	internal ILogger Logger { get; }

	private IMediator Mediator => _services.Mediator;
	private IAttributeService AttributeService => _services.AttributeService;

	protected override ValueTask<CallState?> DefaultResult => ValueTask.FromResult<CallState?>(null);

	public override async ValueTask<CallState?> Visit(IParseTree tree) => await tree.Accept(this);

	/// <summary>
	/// Determines if a bracePattern is inside a function's argument list.
	/// PennMUSH has two brace modes: command braces (full evaluation) and function-arg
	/// braces (suppress function recognition). This detects function-arg braces by
	/// checking if the parse tree has a FunctionContext ancestor.
	/// </summary>
	private static bool IsInsideFunctionArg(ParserRuleContext context)
	{
		var parent = context.Parent;
		while (parent is not null)
		{
			if (parent is FunctionContext)
				return true;
			parent = parent.Parent;
		}

		return false;
	}

	/// <summary>
	/// Whether a name that resolves to no function should be reported as an error rather than
	/// left as literal text.
	/// <para>
	/// PennMUSH reports it only when PE_FUNCTION_MANDATORY is set, which <c>[...]</c> adds
	/// (src/parse.c) — so <c>think foo(bar)</c> prints <c>foo(bar)</c> while
	/// <c>think [foo(bar)]</c> errors. Arguments of a real call are evaluated with that flag
	/// stripped, so an unknown name inside them stays literal too: <c>[strcat(foo(1))]</c>
	/// yields <c>foo(1)</c>.
	/// </para>
	/// Walking outward, whichever context appears first decides: a bracket means the call site
	/// demands a function, an enclosing call means we are inside its argument list and it does not.
	/// </summary>
	internal static bool IsUnknownFunctionAnError(ParserRuleContext context)
	{
		for (var parent = context.Parent; parent is not null; parent = parent.Parent)
		{
			switch (parent)
			{
				case BracketPatternContext:
					return true;
				case FunctionContext:
					return false;
			}
		}

		return false;
	}

	/// <summary>
	/// Reproduces a <c>name(...)</c> that is not a function call as text, the way PennMUSH does:
	/// the parentheses and separators are copied through, and the contents are still evaluated
	/// with function recognition switched off (PE_EVALUATE stays on, PE_FUNCTION_CHECK is cleared).
	/// So <c>foo(add(1,2))</c> stays <c>foo(add(1,2))</c>, while <c>foo([add(1,2)])</c> becomes
	/// <c>foo(3)</c> — a bracket re-enables function recognition inside.
	/// Literal spans are sliced from the source rather than taken from <c>GetText()</c> so that
	/// markup survives.
	/// </summary>
	internal async ValueTask<CallState> LiteralFunctionCall(FunctionContext context)
	{
		var visitor = this;
		var parts = new MString[context.ChildCount];
		var hadErrors = false;
		async ValueTask<MString> EvaluateArgument(EvaluationStringContext argument)
		{
			var result = await visitor.Visit(argument);
			hadErrors |= result?.HadErrors == true;
			return result?.Message ?? MarkupText.Empty;
		}
		using var retainedText = RestrictedTextRetention.Enter(parser.CurrentState);

		visitor._suppressFunctionEval++;
		try
		{
			for (var i = 0; i < context.ChildCount; i++)
			{
				var part = context.GetChild(i) switch
				{
					EvaluationStringContext argument => await EvaluateArgument(argument),
					ITerminalNode terminal => SliceSource(terminal.Symbol),
					_ => MarkupText.Empty
				};
				retainedText?.Add(part.Length);
				parts[i] = part;
			}
		}
		finally
		{
			visitor._suppressFunctionEval--;
		}

		return new CallState(MarkupText.Concat(parts), context.Depth()) { HadErrors = hadErrors };
	}

	/// <summary>
	/// Everything between a call's parentheses, taken verbatim from the source so that markup
	/// survives. Used for <c>FunctionFlags.Literal</c> (PennMUSH's <c>lit()</c>), where the
	/// content is one raw argument: no comma splitting, no substitution.
	/// <para>
	/// The opening parenthesis lives inside the FUNCHAR token along with the name and any
	/// whitespace the lexer folded in after it, so the content starts just past that parenthesis
	/// — not at the end of the token, which would swallow spaces the caller wrote.
	/// </para>
	/// </summary>
	internal MString LiteralArgumentText(FunctionContext context)
	{
		var funChar = context.FUNCHAR()?.Symbol;
		var closeParen = context.CPAREN()?.Symbol;
		if (funChar is null || closeParen is null)
		{
			return MarkupText.Empty;
		}

		var openParenOffset = funChar.Text.IndexOf('(');
		if (openParenOffset < 0)
		{
			return MarkupText.Empty;
		}

		var start = funChar.StartIndex + openParenOffset + 1;
		var length = closeParen.StartIndex - start;

		return length > 0 ? source.Substring(start, length) : MarkupText.Empty;
	}

	/// <summary>
	/// Extracts a token's own text from the original markup-carrying source. A token the error
	/// strategy synthesises for missing input is not part of the lexed stream (<see cref="IToken.TokenIndex"/>
	/// is negative) and covers no real source span — <see cref="LenientErrorStrategy"/> parks it at
	/// the previous token's <c>StopIndex</c> so that <c>StopIndex</c>-based length maths in the
	/// surrounding contexts stay correct, which means slicing the synthetic token on its own would
	/// return a spurious one-character copy of that last real character. Such tokens must contribute
	/// nothing here. (In practice recovery ends the enclosing rule at EOF without adding a closer
	/// child, so this guard is belt-and-suspenders against a future recovery path that inserts one.)
	/// </summary>
	private MString SliceSource(IToken token)
	{
		if (token.TokenIndex < 0 || token.StartIndex < 0)
		{
			return MarkupText.Empty;
		}

		var length = token.StopIndex - token.StartIndex + 1;
		return length > 0 ? source.Substring(token.StartIndex, length) : MarkupText.Empty;
	}

	/// <summary>
	/// Sends debug or verbose output to owner and DEBUGFORWARDLIST recipients.
	/// </summary>
	private ValueTask SendDebugOrVerboseOutput(AnySharpObject executor, string message)
		=> _services.Diagnostics.SendDebugOrVerboseOutput(parser, executor, message);

	public override async ValueTask<CallState?> VisitChildren(IRuleNode? node)
	{
		ExecutionBudget.Current?.ThrowIfExceeded();
		if (node is null) return null;

		var childCount = node.ChildCount;
		switch (childCount)
		{
			case 0:
				return null;
			case 1:
				{
					var only = node.GetChild(0);
					return only is null ? null : await only.Accept(this);
				}
		}

		var results = new List<CallState>(childCount);
		using var retainedText = RestrictedTextRetention.Enter(parser.CurrentState);

		for (var i = 0; i < childCount; i++)
		{
			ExecutionBudget.Current?.ThrowIfExceeded();
			var child = node.GetChild(i);
			var childResult = child is null ? null : await child.Accept(this);
			if (childResult is not null)
			{
				retainedText?.Add(childResult.Message?.Length ?? 0);
				results.Add(childResult);
			}

			if (parser.CurrentState.LimitExceeded?.IsExceeded == true) break;
		}

		return results.Count switch
		{
			0 => null,
			1 => results[0],
			_ => BatchMergeResults(CollectionsMarshal.AsSpan(results))
		};
	}

	public async ValueTask<CallState?> VisitChildrenOrBreak(IRuleNode node, Func<bool> haltPredicate)
	{
		var childCount = node.ChildCount;
		List<CallState>? results = null;

		for (var i = 0; i < childCount; i++)
		{
			if (haltPredicate()) break;
			ExecutionBudget.Current?.ThrowIfExceeded();
			var child = node.GetChild(i);
			if (child is not null)
			{
				var childResult = await child.Accept(this);
				if (childResult is not null)
				{
					results ??= new List<CallState>(childCount);
					results.Add(childResult);
				}
			}
		}

		if (results is null || results.Count == 0)
			return null;

		if (results.Count == 1)
			return results[0];

		return BatchMergeResults(CollectionsMarshal.AsSpan(results));
	}

	/// <summary>
	/// Merges the child CallState results of one node in a single pass: one array copy for
	/// arguments, one <see cref="MarkupText.Concat(MString[])"/> for messages. This runs for every
	/// multi-child node of every evaluation, so it is written as plain loops over the span.
	/// </summary>
	private static CallState BatchMergeResults(ReadOnlySpan<CallState> results)
	{
		CallState? argumentSource = null;
		var totalArgs = 0;
		var preserveSpaces = false;
		var hadErrors = false;
		foreach (var result in results)
		{
			if (result.Arguments is { } args)
			{
				argumentSource ??= result;
				totalArgs += args.Length;
			}

			preserveSpaces |= result.PreserveSpaces;
			hadErrors |= result.HadErrors;
		}

		if (argumentSource is not null)
		{
			var merged = new MString[totalArgs];
			var offset = 0;
			foreach (var result in results)
			{
				if (result.Arguments is { Length: > 0 } args)
				{
					args.CopyTo(merged, offset);
					offset += args.Length;
				}
			}

			return argumentSource with { Arguments = merged, HadErrors = hadErrors };
		}

		var messages = new MString[results.Length];
		for (var i = 0; i < results.Length; i++)
		{
			messages[i] = results[i].Message ?? MarkupText.Empty;
		}

		var combined = MarkupText.Concat(messages);
		return new CallState(combined, results[0].Depth, null,
			() => ValueTask.FromResult<MString?>(combined))
		{
			PreserveSpaces = preserveSpaces,
			HadErrors = hadErrors
		};
	}

	/// <summary>
	/// Extracts text from a parser context using the source string.
	/// This helper reduces code duplication and centralizes the substring extraction logic.
	/// </summary>
	/// <param name="context">The parser rule context to extract text from</param>
	/// <returns>The text content as an MString</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal MString GetContextText(ParserRuleContext context)
	{
		var length = context.Stop?.StopIndex is null
			? 0
			: context.Stop.StopIndex - context.Start.StartIndex + 1;

		return source.Substring(context.Start.StartIndex, length);
	}

	/// <summary>
	/// Creates a deferred evaluation function for a parser context.
	/// This is used for lazy evaluation in functions with NoParse flags.
	/// Instead of creating inline lambdas, this centralizes the pattern and reduces allocations.
	/// </summary>
	/// <param name="context">The evaluation string context to evaluate later</param>
	/// <param name="visitor">The visitor to use for evaluation</param>
	/// <param name="stripAnsi">Whether to strip ANSI codes from the result</param>
	/// <returns>A function that evaluates the context when called</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static Func<ValueTask<CallState?>> CreateDeferredEvaluation(
		EvaluationStringContext context,
		SharpMUSHParserVisitor visitor,
		bool stripAnsi) => async () =>
	{
		var result = await visitor.VisitChildren(context) ?? CallState.Empty;
		var message = result.Message ?? MarkupText.Empty;
		return result with { Message = stripAnsi ? MarkupText.Plain(message.ToPlainText()) : message };
	};

	internal bool BeginsRestrictedEvaluation(FunctionContext context)
	{
		var name = FunctionNameOf(context);
		if (!parser.FunctionLibrary.TryGetValue(name, out var definition)) return false;
		return definition.LibraryInformation.RestrictedOperation == "restrictedexpr"
			|| definition.LibraryInformation.RestrictedOperation == "fn"
			&& EvaluationRestrictions.BeginsRestrictedEvaluation(definition.LibraryInformation,
				RestrictedTargetNames(context), parser.FunctionLibrary);
	}

	private IEnumerable<string> RestrictedTargetNames(FunctionContext context)
	{
		for (var index = 0; index < context.ChildCount; index++)
		{
			ExecutionBudget.CurrentToken.ThrowIfCancellationRequested();
			if (context.GetChild(index) is EvaluationStringContext argument)
				yield return GetContextText(argument).ToPlainText();
		}
	}

	private static void DemandBoundedArguments(FunctionContext context)
	{
		var count = 1;
		for (var index = 0; index < context.ChildCount; index++)
		{
			ExecutionBudget.CurrentToken.ThrowIfCancellationRequested();
			if (context.GetChild(index) is ITerminalNode terminal && terminal.Symbol.Type == COMMAWS
				&& ++count > EvaluationRestrictions.MaximumArguments)
				throw new RestrictedExpressionException();
		}
	}

	private bool ContainsRestrictedEvaluation(IParseTree context)
	{
		// Keep traversal storage proportional to depth, not the number of comma tokens.
		var pending = new Stack<(IParseTree Node, int NextChild)>();
		pending.Push((context, 0));
		while (pending.TryPeek(out var frame))
		{
			ExecutionBudget.Current?.ThrowIfExceeded();
			if (frame.Node is FunctionContext function && frame.NextChild == 0)
			{
				var found = _restrictedScanResults.TryGetValue(function, out var cached)
					? cached : BeginsRestrictedEvaluation(function);
				if (found)
				{
					foreach (var ancestor in pending)
					{
						ExecutionBudget.Current?.ThrowIfExceeded();
						if (ancestor.Node is FunctionContext ancestorFunction) _restrictedScanResults[ancestorFunction] = true;
					}
					return true;
				}
				if (_restrictedScanResults.ContainsKey(function)) { pending.Pop(); continue; }
			}
			if (frame.NextChild >= frame.Node.ChildCount)
			{
				if (frame.Node is FunctionContext completed) _restrictedScanResults[completed] = false;
				pending.Pop();
				continue;
			}
			pending.Pop();
			pending.Push((frame.Node, frame.NextChild + 1));
			pending.Push((frame.Node.GetChild(frame.NextChild), 0));
		}
		return false;
	}

	/// <summary>
	/// The lower-cased name of the function a call invokes. The FUNCHAR token is the name, any
	/// whitespace the lexer folded in, and the opening parenthesis; this slices the name out of it
	/// and lower-cases it in one allocation, which is what the library and the telemetry key on.
	/// </summary>
	private static string FunctionNameOf(FunctionContext context)
	{
		var funChar = context.FUNCHAR().GetText();
		var length = funChar.AsSpan().TrimEnd().Length - 1;
		return string.Create(length, funChar,
			static (span, text) => text.AsSpan(0, span.Length).ToLowerInvariant(span));
	}

	/// <summary>
	/// The positional arguments a call or command binds to <c>%0</c>, <c>%1</c>, … — the dictionary a
	/// <see cref="ParserState.Arguments"/> expects, sized once.
	/// </summary>
	internal static Dictionary<string, CallState> NumberedArguments(List<CallState> values)
	{
		var arguments = new Dictionary<string, CallState>(values.Count);
		foreach (var (i, value) in values.Index())
		{
			arguments[i.ToString()] = value;
		}

		return arguments;
	}

	public override async ValueTask<CallState?> VisitFunction([NotNull] FunctionContext context)
	{
		if (parser.CurrentState.ParseMode is ParseMode.NoParse or ParseMode.NoEval)
		{
			return new CallState(GetContextText(context));
		}

		// PennMUSH: Inside function-arg braces, PE_FUNCTION_CHECK is removed.
		// Functions are not recognized — return literal text of the function call.
		if (_suppressFunctionEval > 0)
		{
			// Function recognition is off here, but evaluation is not: PennMUSH clears
			// PE_FUNCTION_CHECK while PE_EVALUATE stays on, so the call is copied through as text
			// and its contents are still evaluated. Returning the raw source instead would swallow
			// substitutions — notafunction(strlen(%#)) has to yield notafunction(strlen(#1)).
			return await LiteralFunctionCall(context);
		}

		var functionName = FunctionNameOf(context);
		// Reject oversized calls before ANTLR child-array and argument-map materialization.
		var restricted = EvaluationRestrictions.Current is not null || parser.CurrentState.Restrictions is not null;
		if (restricted) DemandBoundedArguments(context);
		var restrictedWrapper = BeginsRestrictedEvaluation(context);
		if (!restricted && restrictedWrapper) DemandBoundedArguments(context);
		var evalStrings = context.evaluationString();
		var commas = context.COMMAWS();

		EvaluationStringContext?[] arguments;
		if (evalStrings is null || evalStrings is [] && commas is [])
		{
			// PennMUSH treats func() as having 1 empty arg, not 0 args.
			arguments = [null];
		}
		else
		{
			var argCount = commas.Length + 1;
			arguments = new EvaluationStringContext?[argCount];
			var evalIdx = 0;
			for (var i = 0; i < argCount; i++)
			{
				if (evalIdx < evalStrings.Length
						&& (i >= commas.Length || evalStrings[evalIdx].Start.StartIndex < commas[i].Symbol.StartIndex))
				{
					arguments[i] = evalStrings[evalIdx++];
				}
			}
		}

		// Recognize the wrapper before its implementation enters the operation scope.
		_containsRestrictedWrapper |= restrictedWrapper;
		// Restricted evaluation must not read DEBUG flags or forwarding attributes.
		var executor = restrictedWrapper || EvaluationRestrictions.Current is not null || parser.CurrentState.Restrictions is not null
			? new AnyOptionalSharpObject(new None())
			: await parser.CurrentState.ExecutorObject(Mediator);
		var shouldDebug = false;
		AnySharpObject? executorObj = null;
		string? indent = null;
		var dbrefNumber = 0;

		if (executor is AnySharpObject knownExecutor)
		{
			executorObj = knownExecutor;

			// QUEUE_NODEBUG / QUEUE_DEBUG on the attribute decide; with neither, the executor's DEBUG flag does.
			shouldDebug = EvaluationDiagnostics.DebugOverride(parser.CurrentState.Flags)
				?? await executorObj.HasFlag("DEBUG");

			if (shouldDebug)
			{
				indent = new string(' ', _debugNestDepth);
				dbrefNumber = executorObj.Object().DBRef.Number;
			}
		}

		// An ancestor trace also contains the wrapper's literal inputs. This extra traversal
		// is needed only while debug output is enabled; ordinary evaluation stays a single walk.
		if (shouldDebug && ContainsRestrictedEvaluation(context))
		{
			_containsRestrictedWrapper = true;
			shouldDebug = false;
		}

		if (shouldDebug && executorObj != null && indent != null)
		{
			// PennMUSH wraps the function expression in [...] when the function was invoked
			// inside bracket evaluation (e.g. think [add(1,2)]), but not for nested calls
			// within function arguments (e.g. iter(a b, strlen(##)) — strlen is bare).
			var isBracketed = context.Parent?.Parent is BracketPatternContext;
			var debugExpr = isBracketed ? $"[{context.GetText()}]" : context.GetText();
			await SendDebugOrVerboseOutput(executorObj, $"#{dbrefNumber}! {indent}{debugExpr} :");
			_didEmitFunctionDebug = true;
		}

		if (shouldDebug) _debugNestDepth++;

		var result = await _services.Functions.InvokeAsync(this, functionName, context, arguments);

		if (shouldDebug) _debugNestDepth--;

		if (!shouldDebug || executorObj is null || indent is null)
		{
			return result;
		}

		var isBracketedPost = context.Parent?.Parent is BracketPatternContext;
		var debugExprPost = isBracketedPost ? $"[{context.GetText()}]" : context.GetText();
		await SendDebugOrVerboseOutput(executorObj,
			$"#{dbrefNumber}! {indent}{debugExprPost} => {result.Message?.ToPlainText() ?? ""}");

		return result;
	}

	public override async ValueTask<CallState?> VisitEvaluationString(
		[NotNull] EvaluationStringContext context) => await VisitChildren(context) ?? new CallState(
		GetContextText(context),
		context.Depth());

	public override async ValueTask<CallState?> VisitExplicitEvaluationString(
		[NotNull] ExplicitEvaluationStringContext context)
	{
		var result = await VisitChildren(context)
								 ?? new CallState(GetContextText(context), context.Depth());

		// PE_COMPRESS_SPACES: strip trailing literal-text spaces at evaluation boundary.
		// Only fires when the last child is literal text (genericText/beginGenericText).
		// Bracket/substitution output trailing spaces are preserved — PennMUSH's had_space
		// flag only triggers on literal space characters, not function/substitution output.
		// The lexer's COMMAWS/CPAREN leading WS does NOT eat trailing spaces before delimiters
		// (OTHER greedily consumes them), so this is the layer that handles trailing.
		if (parser.CurrentState.ParseMode is ParseMode.Default && result.Message is not null)
		{
			// Only strip if the last child is literal text (genericText/beginGenericText)
			// Function output (brackets) trailing spaces should be preserved within the expression
			var children = context.children;
			var lastChild = children[^1];
			if (lastChild is GenericTextContext or BeginGenericTextContext)
				result = result with { Message = result.Message.Trim(global::MarkupString.TrimType.TrimEnd, " ") };
		}

		return result;
	}

	public override async ValueTask<CallState?> VisitBraceExplicitEvaluationString(
		[NotNull] BraceExplicitEvaluationStringContext context) =>
		await VisitChildren(context)
		?? new CallState(GetContextText(context),
			context.Depth());

	/// <summary>
	/// <c>$&lt;digit&gt;</c> or <c>$&lt;name&gt;</c>: PennMUSH's evaluator case for <c>'$'</c>
	/// (<c>src/parse.c</c>). While a regexp context is visible, the capture from the innermost one;
	/// otherwise a literal <c>$</c> followed by the rest, with a name still evaluated. A capture is
	/// output, never source: nothing here is parsed again.
	/// </summary>
	public override async ValueTask<CallState?> VisitRegexpCapture([NotNull] RegexpCaptureContext context)
	{
		if (parser.CurrentState.ParseMode is ParseMode.NoParse or ParseMode.NoEval)
		{
			return new CallState(GetContextText(context), context.Depth());
		}

		var state = parser.CurrentState;
		if (context.REGEXP_NUM() is { } number)
		{
			return new CallState(state.HasRegexpContext
				? state.RegexpCapture(number.GetText()[1..])
				: MarkupText.Plain(number.GetText()), context.Depth());
		}

		var nameContext = context.explicitEvaluationString();
		var named = nameContext is null
			? CallState.Empty
			: await Visit(nameContext) ?? new CallState(GetContextText(nameContext), nameContext.Depth());
		var name = named.Message ?? MarkupText.Empty;

		if (state.HasRegexpContext)
		{
			return named with { Message = state.RegexpCapture(name.ToPlainText()) };
		}

		MString[] literal = context.CCARET() is null
			? [MarkupText.Plain("$<"), name]
			: [MarkupText.Plain("$<"), name, MarkupText.Plain(">")];
		return named with { Message = MarkupText.Concat(literal) };
	}

	public override async ValueTask<CallState?> VisitBracePattern(
		[NotNull] BracePatternContext context)
	{
		_braceDepthCounter++;

		// PennMUSH has two brace modes:
		// 1. Command braces: full evaluation (functions work normally)
		// 2. Function-arg braces: suppress function recognition (but % subs and [...] still work)
		// Detect function-arg braces by checking if this brace has a FunctionContext ancestor.
		var isFunctionArgBrace = IsInsideFunctionArg(context);
		if (isFunctionArgBrace)
			_suppressFunctionEval++;

		// A child that throws is caught by the enclosing function call, and the walk goes on: the
		// counters must come back down either way, or every later call reads as literal text.
		try
		{
			var vc = await VisitChildren(context);

			// Normal evaluation strips the outermost braces (PennMUSH PE_STRIP_BRACES strips all brace
			// levels during evaluation). Nested braces (depth > 1), or the PreserveBraces flag, keep them.
			// PreserveBraces is set for:
			// - RSBrace commands (@wait, @force, @halt): handler strips them at execution
			//   time via StripOuterBraces (PennMUSH PE_COMMAND_BRACES equivalent).
			// - NoParse commands (&): braces are preserved literally in the stored value
			//   (PennMUSH QUEUE_NOLIST/noeval — value never enters process_expression).
			var stripsBraces = _braceDepthCounter <= 1
				&& !parser.CurrentState.Flags.HasFlag(ParserStateFlags.PreserveBraces);
			var result = stripsBraces
				? vc ?? new CallState(GetContextText(context), context.Depth())
				: vc is not null
					? vc with
					{
						Message = MarkupText.Concat([
							MarkupText.Plain("{"),
							vc.Message ?? MarkupText.Empty,
							MarkupText.Plain("}")
						])
					}
					: new CallState(GetContextText(context), context.Depth());

			return result;
		}
		finally
		{
			if (isFunctionArgBrace) _suppressFunctionEval--;
			_braceDepthCounter--;
		}
	}

	public override async ValueTask<CallState?> VisitBracketPattern(
		[NotNull] BracketPatternContext context)
	{
		// PennMUSH: [...] bracket evaluation inside braces RE-ENABLES PE_FUNCTION_CHECK.
		// So {[add(1,2)]} evaluates add() to 3, even inside function-arg braces.
		// Save and clear suppression so functions work inside brackets.
		var savedSuppress = _suppressFunctionEval;
		_suppressFunctionEval = 0;
		var evaluates = parser.CurrentState.ParseMode is not ParseMode.NoParse and not ParseMode.NoEval;

		CallState? result;
		try
		{
			result = await VisitChildren(context);
		}
		finally
		{
			_suppressFunctionEval = savedSuppress;
		}

		if (evaluates)
		{
			return result ?? new CallState(GetContextText(context), context.Depth());
		}

		if (result is null)
		{
			return new CallState(GetContextText(context), context.Depth());
		}

		return result with
		{
			Message = MarkupText.Concat([
				MarkupText.Plain("["),
				result.Message ?? MarkupText.Empty,
				MarkupText.Plain("]")
			])
		};
	}

	public override async ValueTask<CallState?> VisitGenericText([NotNull] GenericTextContext context)
	{
		var result = await VisitChildren(context)
								 ?? new CallState(GetContextText(context), context.Depth());
		// PE_COMPRESS_SPACES: only compress here for the FUNCHAR alternative
		// (terminal token — VisitChildren returns null, so GetContextText has raw text).
		// The beginGenericText alternative is already compressed by VisitBeginGenericText.
		if (context.beginGenericText() is null
				&& parser.CurrentState.ParseMode is ParseMode.Default
				&& result.Message is not null)
			return result with { Message = MushText.CompressSpaces(result.Message) };
		return result;
	}

	public override async ValueTask<CallState?> VisitBeginGenericText(
		[NotNull] BeginGenericTextContext context)
	{
		var result = await VisitChildren(context)
								 ?? new CallState(GetContextText(context), context.Depth());
		// PE_COMPRESS_SPACES: compress literal space runs to single space.
		// The lexer already eats leading spaces on function args (FUNCHAR WS, COMMAWS WS),
		// but internal runs within OTHER tokens and top-level/command-arg leading spaces remain.
		if (parser.CurrentState.ParseMode is ParseMode.Default && result.Message is not null)
		{
			var compressed = MushText.CompressSpaces(result.Message);
			// Strip leading when this is the FIRST text node in an evaluation string
			// (direct child of explicitEvaluationString). When reached via genericText
			// (text between brackets), leading spaces are meaningful separators.
			//
			// Being that first element is necessary but NOT sufficient, because an
			// explicitEvaluationString is not always the start of the string it belongs to:
			// `evaluationString: function explicitEvaluationString?` (SharpMUSHParser.g4:65-67)
			// parses the text *after* a leading call as a second explicitEvaluationString, whose
			// first element is exactly such a node. `add(1,2) x` used to lose that space and yield
			// "3x"; the space separates the call's result from what follows it and is no more
			// leading than the "3" is. So a tail-of-a-call node is excluded here.
			if (context.Parent is ExplicitEvaluationStringContext or BraceExplicitEvaluationStringContext
					&& !FollowsACallInTheSameEvaluationString(context.Parent))
				compressed = compressed.Trim(global::MarkupString.TrimType.TrimStart, " ");
			return result with { Message = compressed };
		}

		return result;
	}

	/// <summary>
	/// Whether <paramref name="explicitEvaluationString"/> is the trailing half of
	/// <c>evaluationString: function explicitEvaluationString?</c> rather than a string's own
	/// beginning &mdash; i.e. whether a <c>function</c> was already emitted to its left.
	/// <para>
	/// Only that one production puts an <c>explicitEvaluationString</c> after something that
	/// produces output. Everywhere else it is genuinely first, and its leading whitespace is the
	/// string's own leading whitespace.
	/// </para>
	/// </summary>
	private static bool FollowsACallInTheSameEvaluationString(Antlr4.Runtime.Tree.IParseTree explicitEvaluationString)
		=> explicitEvaluationString.Parent is EvaluationStringContext evaluationString
			 && evaluationString.function() is not null;

	public override async ValueTask<CallState?> VisitValidSubstitution(
		[NotNull] ValidSubstitutionContext context)
	{
		if (parser.CurrentState.ParseMode is ParseMode.NoParse or ParseMode.NoEval)
		{
			return new CallState("%" + context.GetText());
		}

		EvaluationRestrictions.DemandSubstitution(context.GetText(), parser.CurrentState.Restrictions);
		var complexSubstitutionSymbol = context.complexSubstitutionSymbol();
		var simpleSubstitutionSymbol = context.substitutionSymbol();

		CallState? result;
		if (complexSubstitutionSymbol is not null)
		{
			var state = await VisitChildren(context);
			result = await Substitutions.Substitutions.ParseComplexSubstitution(state, parser, AttributeService, Mediator,
				complexSubstitutionSymbol);
		}
		else if (simpleSubstitutionSymbol is not null)
		{
			result = await Substitutions.Substitutions.ParseSimpleSubstitution(
				simpleSubstitutionSymbol.GetText(),
				parser,
				Mediator,
				AttributeService,
				Configuration,
				simpleSubstitutionSymbol);
		}
		else
		{
			result = await VisitChildren(context) ?? new CallState(MarkupText.Plain(context.GetText()), context.Depth());
		}

		return CapitalizeForUpperSelector(context, result);
	}

	/// <summary>
	/// PennMUSH capitalizes the first character of a substitution's output when the selector letter
	/// is uppercase — <c>%Q0</c> vs <c>%q0</c>, <c>%N</c> vs <c>%n</c>, <c>%I0</c> vs <c>%i0</c>, and
	/// so on (src/parse.c). The rule keys on the character immediately after <c>%</c>, so digit and
	/// symbol substitutions (<c>%0</c>, <c>%#</c>, <c>%!</c>) are never affected, and it is a no-op
	/// when the first output character is not a letter. Applying it here, once, covers every
	/// substitution kind; it is idempotent for any output that is already capitalized.
	/// </summary>
	private static CallState? CapitalizeForUpperSelector(ValidSubstitutionContext context, CallState? result)
	{
		if (result?.Message is null || result.Message.Length < 1)
		{
			return result;
		}

		// The selector is the first character of the substitution's first token; only a rule with at
		// least one token has one.
		var start = context.Start;
		var stop = context.Stop;
		if (start is null || stop is null || stop.TokenIndex < start.TokenIndex)
		{
			return result;
		}

		var selector = start.Text;
		if (string.IsNullOrEmpty(selector) || !char.IsAsciiLetterUpper(selector[0]))
		{
			return result;
		}

		var first = result.Message.ToPlainText()[0];
		if (char.ToUpperInvariant(first) == first)
		{
			return result;
		}

		var firstChar = result.Message.Substring(0, 1).Apply(x => x.ToUpperInvariant());
		var rest = result.Message.Substring(1, result.Message.Length - 1);
		return result with { Message = MarkupText.Concat(firstChar, rest) };
	}

	public override async ValueTask<CallState?> VisitCommand([NotNull] CommandContext context)
	{
		if (parser.CurrentState.ParseMode == ParseMode.NoParse)
		{
			return await VisitChildren(context) ?? new CallState(context.GetText());
		}

		var result = await _services.Dispatcher.DispatchAsync(this, source, context);
		return result is CallState callState ? callState : CallState.Empty;
	}

	public override async ValueTask<CallState?> VisitStartCommandString(
		[NotNull] StartCommandStringContext context)
	{
		var result = await VisitChildren(context);
		if (result != null)
		{
			return result;
		}

		var text = GetContextText(context);
		return new CallState(text, context.Depth());
	}

	private bool BreakTriggered()
		=> parser.CurrentState.ExecutionStack.TryPeek(out var result) && result.CommandListBreak;

	public override async ValueTask<CallState?> VisitCommandList([NotNull] CommandListContext context)
	{
		// Claim the one-shot before the children run, so only THIS list — the outermost of the parse
		// the caller started — reports its break upward. Lists nested deeper inside it find the flag
		// already cleared and keep containing their own breaks.
		var propagation = parser.CurrentState.BreakPropagation;
		var reportBreak = propagation is { PreserveNext: true };
		if (reportBreak)
		{
			propagation!.PreserveNext = false;
		}

		var result = parser.CurrentState.ParseMode != ParseMode.NoParse && HasPipe(context)
			? await VisitPipedCommandList(context)
			: await VisitChildrenOrBreak(context, BreakTriggered);

		if (BreakTriggered())
		{
			if (reportBreak)
			{
				propagation!.Broke = true;
			}

			parser.CurrentState.ExecutionStack.TryPop(out _);
		}

		if (result is not null)
		{
			return result;
		}

		var text2 = GetContextText(context);
		return new CallState(text2, context.Depth());
	}

	/// <summary>
	/// Whether the command at <paramref name="index"/> of a list is piped into: TinyMUX's <c>;|</c>, a
	/// <c>|</c> straight after the separator (<c>look ;| say %|</c>).
	/// </summary>
	private bool IsPipedInto(CommandListContext context, int index)
		=> index > 0
			&& index < context.ChildCount
			&& context.GetChild(index) is CommandContext { Start: { } start }
			&& context.GetChild(index - 1) is ITerminalNode { Symbol: { } separator }
			&& separator.Type == SEMICOLON
			// The separator token takes the spaces after the ';', so `; |x` is not a pipe: the '|' must follow
			// the ';' itself.
			&& start.StartIndex == separator.StartIndex + 1
			&& start.StartIndex < source.Text.Length
			&& source.Text[start.StartIndex] == '|';

	private bool HasPipe(CommandListContext context)
	{
		for (var i = 2; i < context.ChildCount; i++)
		{
			if (IsPipedInto(context, i)) return true;
		}

		return false;
	}

	/// <summary>
	/// Runs a command list that pipes (help piping). What a command followed by <c>;|</c> tells its
	/// executor is taken instead of shown, and the next command, run without its <c>|</c>, reads it as
	/// <c>%|</c>. Otherwise the list runs as <see cref="VisitChildrenOrBreak"/> runs it.
	/// </summary>
	private async ValueTask<CallState?> VisitPipedCommandList(CommandListContext context)
	{
		List<CallState>? results = null;
		MString? printed = null;

		for (var i = 0; i < context.ChildCount; i++)
		{
			if (BreakTriggered()) break;
			ExecutionBudget.Current?.ThrowIfExceeded();
			var child = context.GetChild(i);
			if (child is null) continue;

			var pipesOn = child is CommandContext && IsPipedInto(context, i + 2);
			var buffer = pipesOn && parser.CurrentState.Executor is { } executor && _services.PipeCapture is { } capture
				? (Buffer: new PipeBuffer(parser.CurrentState.OutputLimit), Capture: capture, Executor: executor.Number)
				: default;

			CallState? childResult;
			using (buffer.Buffer is not null ? buffer.Capture.BeginCapture(buffer.Executor, buffer.Buffer) : null)
			{
				childResult = child is CommandContext command && IsPipedInto(context, i)
					? await RunPipedInto(command, printed ?? MarkupText.Empty)
					: await child.Accept(this);
			}

			if (child is CommandContext)
			{
				printed = buffer.Buffer?.Text;
			}

			if (childResult is not null)
			{
				results ??= new List<CallState>(context.ChildCount);
				results.Add(childResult);
			}
		}

		return results switch
		{
			null or [] => null,
			[var only] => only,
			_ => BatchMergeResults(CollectionsMarshal.AsSpan(results))
		};
	}

	/// <summary>
	/// Runs a command after <c>;|</c> without its <c>|</c>, with <paramref name="printed"/> as its
	/// <c>%|</c> and the list's own <c>%|</c> back afterwards.
	/// </summary>
	private async ValueTask<CallState?> RunPipedInto(CommandContext command, MString printed)
	{
		var start = command.Start.StartIndex + 1;
		var text = source.Substring(start, command.Stop.StopIndex - start + 1);
		if (text.Length == 0) return CallState.Empty;

		var commandText = parser.CurrentState.CommandText;
		var before = commandText?.Printed;
		if (commandText is not null) commandText.Printed = printed;
		try
		{
			return await parser.CommandParse(text);
		}
		finally
		{
			if (commandText is not null) commandText.Printed = before!;
		}
	}

	public override async ValueTask<CallState?> VisitStartSingleCommandString(
		[NotNull] StartSingleCommandStringContext context)
	{
		var result = await VisitChildren(context);
		if (result is not null)
		{
			return result;
		}

		return new CallState(GetContextText(context), context.Depth());
	}

	/// <summary>
	/// Decodes <c>\x</c> to <c>x</c> — but only in an evaluating pass. A NoParse/NoEval pass keeps the
	/// backslash, exactly as it keeps <c>%#</c> and <c>[fn()]</c> raw: its text is either shown or stored
	/// as written (<c>]think</c>, <c>/noeval</c>, <c>&amp;</c>) or evaluated later, and that later
	/// evaluation is the one that decodes. Decoding in both would decode every escape in a command
	/// argument twice: <c>think \[</c> would reach its evaluation as <c>[</c> and fail to parse.
	/// </summary>
	public override async ValueTask<CallState?> VisitEscapedText([NotNull] EscapedTextContext context)
	{
		if (parser.CurrentState.ParseMode is ParseMode.NoParse or ParseMode.NoEval)
		{
			return new CallState(GetContextText(context), context.Depth());
		}

		return await VisitChildren(context)
					 ?? new CallState(
						 source.Substring(context.Start.StartIndex + 1, context.Stop.StopIndex - context.Start.StartIndex + 1 - 1), context.Depth());
	}

	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.startPlainSingleCommandArg"/>.
	/// Wraps the evaluated string result as Arguments to match the expected output format.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public override async ValueTask<CallState?> VisitStartPlainSingleCommandArg(
		[NotNull] StartPlainSingleCommandArgContext context)
	{
		var evalString = context.evaluationString();
		if (evalString is null)
			return CallState.Empty;

		var visited = await Visit(evalString);

		return new CallState(
			Message: null,
			context.Depth(),
			Arguments:
			[
				visited?.Message ?? GetContextText(evalString)
			],
			ParsedMessage: () => ValueTask.FromResult<MString?>(null))
		{
			ArgumentContexts = [evalString],
			HadErrors = visited?.HadErrors == true
		};
	}

	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.startEqSplitCommandArgs"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public override async ValueTask<CallState?> VisitStartEqSplitCommandArgs(
		[NotNull] StartEqSplitCommandArgsContext context)
	{
		var evalString = context.evaluationString();
		var baseArg = evalString is not null ? await Visit(evalString) : CallState.Empty;
		var commaArgsContext = context.commaCommandArgs();
		var commaArgs = commaArgsContext is not null
			? await VisitCommaCommandArgs(commaArgsContext)
			: null;
		return new CallState(null,
			context.Depth(),
			[baseArg?.Message ?? MarkupText.Empty, .. commaArgs?.Arguments ?? []],
			() => ValueTask.FromResult<MString?>(null))
		{
			ArgumentContexts = [evalString, .. commaArgs?.ArgumentContexts ?? []],
			HadErrors = baseArg?.HadErrors == true || commaArgs?.HadErrors == true
		};
	}

	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.startEqSplitCommand"/>.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public override async ValueTask<CallState?> VisitStartEqSplitCommand(
		[NotNull] StartEqSplitCommandContext context)
	{
		var evalStrings = context.evaluationString();
		var equalsToken = context.EQUALS();

		if (equalsToken is null)
		{
			var argument = evalStrings.Length > 0 ? await Visit(evalStrings[0]) : null;
			return new CallState(null, context.Depth(), [
					argument?.Message ?? MarkupText.Empty
				],
				() => ValueTask.FromResult<MString?>(null))
			{
				ArgumentContexts = [evalStrings.Length > 0 ? evalStrings[0] : null],
				HadErrors = argument?.HadErrors == true
			};
		}

		var lhsExists = evalStrings.Length > 0 && evalStrings[0].Start.StartIndex < equalsToken.Symbol.StartIndex;
		var lhsArg = lhsExists ? await Visit(evalStrings[0]) : null;
		var rsIdx = lhsExists ? 1 : 0;
		var rhsArg = rsIdx < evalStrings.Length ? await Visit(evalStrings[rsIdx]) : null;
		return new CallState(null, context.Depth(),
			[lhsArg?.Message ?? MarkupText.Empty, rhsArg?.Message ?? MarkupText.Empty],
			() => ValueTask.FromResult<MString?>(null))
		{
			ArgumentContexts =
			[
				lhsExists ? evalStrings[0] : null,
				rsIdx < evalStrings.Length ? evalStrings[rsIdx] : null
			],
			HadErrors = lhsArg?.HadErrors == true || rhsArg?.HadErrors == true
		};
	}

	/// <summary>
	/// Visit a parse tree produced by <see cref="SharpMUSHParser.commaCommandArgs"/>.
	/// Directly visits evaluationString contexts and maps them to argument slots
	/// based on their positions relative to COMMAWS tokens.
	/// </summary>
	/// <param name="context">The parse tree.</param>
	/// <return>The visitor result.</return>
	public override async ValueTask<CallState?> VisitCommaCommandArgs(
		[NotNull] CommaCommandArgsContext context)
	{
		var evalStrings = context.evaluationString();
		var commas = context.COMMAWS();
		var argCount = commas.Length + 1;
		var arguments = new MString[argCount];
		var contexts = new object?[argCount];
		var evalIdx = 0;
		var hadErrors = false;
		for (var i = 0; i < argCount; i++)
		{
			if (evalIdx < evalStrings.Length
					&& (i >= commas.Length || evalStrings[evalIdx].Start.StartIndex < commas[i].Symbol.StartIndex))
			{
				var result = await Visit(evalStrings[evalIdx++]);
				hadErrors |= result?.HadErrors == true;
				arguments[i] = result?.Message ?? GetContextText(evalStrings[evalIdx - 1]);
				contexts[i] = evalStrings[evalIdx - 1];
			}
			else
			{
				arguments[i] = MarkupText.Empty;
				contexts[i] = null;
			}
		}

		return new CallState(null, context.Depth(), arguments, () => ValueTask.FromResult<MString?>(null))
		{
			ArgumentContexts = contexts,
			HadErrors = hadErrors
		};
	}

	public override async ValueTask<CallState?> VisitComplexSubstitutionSymbol(
		[NotNull] ComplexSubstitutionSymbolContext context)
	{
		if (context.ChildCount > 1)
			return await VisitChildren(context);

		if (context.REG_NUM() is not null
				|| context.REG_ALPHA() is not null
				|| context.ITEXT_NUM() is not null
				|| context.STEXT_NUM() is not null)
		{
			return new CallState(
				source.Substring(context.Start.StartIndex + 1, context.Stop.StopIndex - context.Start.StartIndex + 1 - 1), context.Depth());
		}

		return new CallState(
			source.Substring(context.Start.StartIndex, context.Stop?.StopIndex is null
					? 0
					: context.Stop.StopIndex - context.Start.StartIndex + 1),
			context.Depth());
	}
}
