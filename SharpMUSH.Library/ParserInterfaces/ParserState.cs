using Mediator;
using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.ParserInterfaces;

/// <summary>
/// Bitfield flags for the parser state, consolidating individual boolean properties into a single
/// compact field.  Adding a new flag here (instead of a new <see langword="bool"/> property) keeps
/// <see cref="ParserState"/> compact and avoids record-copy overhead for each new flag.
///
/// <para>
/// Mapping to PennMUSH <c>QUEUE_*</c> constants (<c>mque.queue_type</c>):
/// <list type="table">
///   <listheader><term>SharpMUSH flag</term><description>PennMUSH equivalent</description></listheader>
///   <item><term><see cref="DirectInput"/></term><description><c>QUEUE_NOLIST</c> (0x0200) — don't split on semicolons; don't evaluate the RHS of the <c>&amp;</c> command</description></item>
///   <item><term><see cref="Debug"/></term><description><c>QUEUE_DEBUG</c> (0x1000) — attribute carries the DEBUG flag; force debug output on</description></item>
///   <item><term><see cref="NoDebug"/></term><description><c>QUEUE_NODEBUG</c> (0x2000) — attribute carries the NO_DEBUG flag; suppress debug output</description></item>
/// </list>
/// Other notable PennMUSH flags not (yet) represented here:
/// <c>QUEUE_SOCKET</c> (0x0004, socket input) is covered by <see cref="ParserState.Handle"/> being non-null;
/// <c>QUEUE_BREAK</c> (0x0400) maps to <see cref="Execution.CommandListBreak"/>;
/// <c>QUEUE_INPLACE</c>, <c>QUEUE_PRESERVE_QREG</c>, etc. are not yet implemented.
/// </para>
/// </summary>
[Flags]
public enum ParserStateFlags
{
	/// <summary>No flags set.</summary>
	None = 0,

	/// <summary>
	/// Equivalent to PennMUSH's <c>QUEUE_NOLIST</c> flag (0x0200).
	/// Set when a command originates directly from a player's network connection (typed at the prompt).
	/// When set, commands like <c>&amp;</c> store their value argument as literal code without evaluation,
	/// and the command string is not split on semicolons.
	/// Cleared by <c>CommandListParse</c> / <c>CommandListParseVisitor</c> so that all queue and
	/// callback contexts (e.g., <c>@wait</c>, <c>@force</c>, triggered attributes) evaluate the RHS.
	/// </summary>
	DirectInput = 1 << 0,

	/// <summary>
	/// Equivalent to PennMUSH's <c>QUEUE_DEBUG</c> flag (0x1000).
	/// Set when the attribute being evaluated carries the DEBUG flag.
	/// Forces debug output on for this evaluation, regardless of the executor's DEBUG object flag.
	/// Takes lower precedence than <see cref="NoDebug"/>: if both are set, <c>NoDebug</c> wins.
	/// </summary>
	Debug = 1 << 1,

	/// <summary>
	/// Equivalent to PennMUSH's <c>QUEUE_NODEBUG</c> flag (0x2000).
	/// Set when the attribute being evaluated carries the NO_DEBUG flag.
	/// Suppresses debug output for this evaluation, regardless of the executor's DEBUG object flag.
	/// Takes precedence over <see cref="Debug"/>: if both are set, this wins.
	/// </summary>
	NoDebug = 1 << 2,

	/// <summary>
	/// Set during argument parsing when the command has <see cref="CommandBehavior.RSBrace"/>.
	/// Causes <c>VisitBracePattern</c> to preserve outer braces in the argument text instead of
	/// stripping them. This implements PennMUSH's <c>CS_BRACES</c> flag behavior, where brace
	/// stripping is deferred to the command handler (via <see cref="HelperFunctions.StripOuterBraces"/>)
	/// rather than happening during argument tokenization.
	/// </summary>
	PreserveBraces = 1 << 3,
}

/// <summary>
/// What parsing mode the parser should consider.
/// </summary>
public enum ParseMode
{
	/// <summary>
	/// Normal parsing. Parse everything.
	/// </summary>
	Default,
	/// <summary>
	/// Do not parse argument-splits (commas). This is usually called by {}s. 
	/// </summary>
	NoParse,
	/// <summary>
	/// Do not evaluate any parameters.
	/// </summary>
	NoEval
}

/// <summary>
/// Sets execution values pertinent to the parser.
/// </summary>
/// <param name="CommandListBreak">Sets whether to stop executing the remainder of the CommandList. This is reset in the SharpMUSHParserVisitor</param>
public record Execution(bool CommandListBreak = false);

/// <summary>
/// Shared counter for function invocations. Mutable reference type to share across parser states.
/// </summary>
public class InvocationCounter
{
	/// <summary>
	/// Total number of function calls made during this evaluation.
	/// </summary>
	public int Count { get; private set; }

	/// <summary>
	/// Increment the counter and return the new value.
	/// </summary>
	public int Increment() => ++Count;

	/// <summary>
	/// Decrement the counter and return the new value.
	/// </summary>
	public int Decrement() => --Count;
}

/// <summary>
/// Shared flag for tracking when a limit (invocation, recursion, depth, call) has been exceeded.
/// Must be a reference type (class) to enable sharing the same flag across all immutable ParserState records.
/// </summary>
public class LimitExceededFlag
{
	/// <summary>
	/// Indicates whether a limit has been exceeded during this evaluation.
	/// </summary>
	public bool IsExceeded { get; set; }
}

/// <summary>
/// HTTP response context for building HTTP responses
/// </summary>
public class HttpResponseContext
{
	/// <summary>
	/// HTTP status line (e.g., "200 OK", "404 Not Found")
	/// </summary>
	public string? StatusLine { get; set; }

	/// <summary>
	/// Content-Type header value
	/// </summary>
	public string? ContentType { get; set; }

	/// <summary>
	/// Additional HTTP headers
	/// </summary>
	public List<(string Name, string Value)> Headers { get; } = new();

	/// <summary>
	/// Response body content
	/// </summary>
	public StringBuilder Body { get; } = new();
}

public class IterationWrapper<T>
{
	/// <summary>
	/// The iteration value.
	/// </summary>
	public required T Value { get; set; }

	/// <summary>
	/// Iteration number.
	/// </summary>
	public required uint Iteration { get; set; }

	/// <summary>
	/// This is for the break() function iterator.
	/// </summary>
	public required bool Break { get; set; }

	/// <summary>
	/// NoBreak indicator is to ensure that a CommandListBreak does not also break the Iteration.
	/// </summary>
	public required bool NoBreak { get; set; }
}

/// <summary>
/// A layer or Parser State
/// </summary>
/// <param name="Registers">The current standard registers (%0, %1, named arguments)</param>
/// <param name="IterationRegisters">The current iteration registers: %i0, #@, etc</param>
/// <param name="RegexRegisters">The current regex registers, %$0 and named ones.</param>
/// <param name="SwitchStack">The switch context stack for stext() and slev() functions. Tracks the string being matched in nested switch statements.</param>
/// <param name="CurrentEvaluation">The current evaluation context</param>
/// <param name="ParserFunctionDepth">The function depth.</param>
/// <param name="Function">Function name being evaluated</param>
/// <param name="Command">Command name being evaluated</param>
/// <param name="Switches">Switches for the command being evaluated</param>
/// <param name="Arguments">The arguments to the command or function</param>
/// <param name="Executor">The executor of a command is the object actually carrying out the command or running the code: %!</param>
/// <param name="Enactor">The enactor is the object which causes something to happen: %# or %:</param>
/// <param name="Caller">The caller is the object which causes an attribute to be evaluated (for instance, by using ufun() or a similar function): %@</param>
/// <param name="Handle">The telnet handle running the command.</param>
/// <param name="ParseMode">Parse mode, in case we need to NoParse.</param>
/// <param name="HttpResponse">HTTP response context for building HTTP responses</param>
/// <param name="CallDepth">Shared counter tracking overall function call nesting depth. Mutable and shared across all states in an evaluation.</param>
/// <param name="FunctionRecursionDepths">Shared dictionary tracking per-function recursion depths. Mutable and shared across all states in an evaluation.</param>
/// <param name="TotalInvocations">Shared counter for total function invocations. Mutable and shared across all states in an evaluation.</param>
/// <param name="LimitExceeded">Shared flag indicating a limit has been exceeded. Mutable and shared across all states in an evaluation.</param>
/// <param name="CommandHistory">Shared mutable stack tracking command invocations (invoker + args) for @retry support. Null outside CommandListParse context.</param>
/// <param name="Flags">
/// Bitfield of <see cref="ParserStateFlags"/> values controlling parser behavior.
/// Use <see cref="ParserStateFlags.DirectInput"/> (≙ <c>QUEUE_NOLIST</c>),
/// <see cref="ParserStateFlags.Debug"/> (≙ <c>QUEUE_DEBUG</c>), and
/// <see cref="ParserStateFlags.NoDebug"/> (≙ <c>QUEUE_NODEBUG</c>).
/// </param>
/// <param name="CallerArguments">
/// Saves the caller's numbered arguments (%0-%9) from the enclosing scope before a command
/// overwrites Arguments with its own parsed args. Used by @wait/@force to preserve pattern-match
/// variables in queued callbacks. Equivalent to PennMUSH's wenv (wild environment).
/// </param>
public partial record ParserState(
	ConcurrentStack<Dictionary<string, MString>> Registers,
	ConcurrentStack<IterationWrapper<MString>> IterationRegisters,
	ConcurrentStack<Dictionary<string, MString>> RegexRegisters,
	ConcurrentStack<MString> SwitchStack,
	ConcurrentStack<Execution> ExecutionStack,
	Dictionary<string, CallState> EnvironmentRegisters,
	DBAttribute? CurrentEvaluation,
	int? ParserFunctionDepth,
	string? Function,
	string? Command,
	Func<IMUSHCodeParser, ValueTask<Option<CallState>>> CommandInvoker,
	IEnumerable<string> Switches,
	Dictionary<string, CallState> Arguments,
	DBRef? Executor,
	DBRef? Enactor,
	DBRef? Caller,
	long? Handle,
	ParseMode ParseMode = ParseMode.Default,
	HttpResponseContext? HttpResponse = null,
	InvocationCounter? CallDepth = null,
	Dictionary<string, int>? FunctionRecursionDepths = null,
	InvocationCounter? TotalInvocations = null,
	LimitExceededFlag? LimitExceeded = null,
	ConcurrentStack<(Func<IMUSHCodeParser, ValueTask<Option<CallState>>> Invoker, Dictionary<string, CallState> Args)>? CommandHistory = null,
	ParserStateFlags Flags = ParserStateFlags.None,
	Dictionary<string, CallState>? CallerArguments = null)
{
	private AnyOptionalSharpObject? _executorObject;
	private AnyOptionalSharpObject? _enactorObject;
	private AnyOptionalSharpObject? _callerObject;

	/// <summary>
	/// Validates that a cached object matches the expected DBRef and clears it if not.
	/// This handles the case where ParserState is copied with a new DBRef but the cached object is stale.
	/// </summary>
	private static void ValidateAndClearCacheIfNeeded(
		ref AnyOptionalSharpObject? cachedObject,
		DBRef? expectedDBRef)
	{
		if (cachedObject is not null && !cachedObject.IsNone && expectedDBRef is not null)
		{
			try
			{
				var cachedDBRef = cachedObject.Known().Object().DBRef;
				if (!cachedDBRef.Equals(expectedDBRef.Value))
				{
					cachedObject = null;
				}
			}
			catch
			{
				// If we can't extract the DBRef (shouldn't happen), clear the cache
				cachedObject = null;
			}
		}
	}

	public static ParserState Empty => new(
		new ConcurrentStack<Dictionary<string, MString>>(),
		new ConcurrentStack<IterationWrapper<MString>>(),
		new ConcurrentStack<Dictionary<string, MString>>(),
		new ConcurrentStack<MString>(),
		new ConcurrentStack<Execution>(),
		[],
		null,
		null,
		null,
		null,
		_ => new ValueTask<Option<CallState>>(new None()),
		[],
		new Dictionary<string, CallState>(),
		null,
		null,
		null,
		null,
		ParseMode.Default,
		null,
		new InvocationCounter(),
		new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
		new InvocationCounter(),
		new LimitExceededFlag());

	/// <summary>
	/// The executor of a command is the object actually carrying out the command or running the code: %!
	/// </summary>
	/// <param name="mediator">Mediator to get the object node with.</param>
	/// <returns>A ValueTask containing either a SharpObject, or None.</returns>
	public async ValueTask<AnyOptionalSharpObject> ExecutorObject(IMediator mediator)
	{
		ValidateAndClearCacheIfNeeded(ref _executorObject, Executor);
		return _executorObject ??= Executor is null ? new None() : await mediator.Send(new GetObjectNodeQuery(Executor.Value));
	}

	/// <summary>
	/// The enactor is the object which causes something to happen: %# or %:
	/// </summary>
	/// <param name="mediator">Mediator to get the object node with.</param>
	/// <returns>A ValueTask containing either a SharpObject, or None.</returns>
	public async ValueTask<AnyOptionalSharpObject> EnactorObject(IMediator mediator)
	{
		ValidateAndClearCacheIfNeeded(ref _enactorObject, Enactor);
		return _enactorObject ??= Enactor is null ? new None() : await mediator.Send(new GetObjectNodeQuery(Enactor.Value));
	}

	/// <summary>
	/// The caller is the object which causes an attribute to be evaluated (for instance, by using ufun() or a similar function): %@
	/// </summary>
	/// <param name="mediator">Mediator to get the object node with.</param>
	/// <returns>A ValueTask containing either a SharpObject, or None.</returns>
	public async ValueTask<AnyOptionalSharpObject> CallerObject(IMediator mediator)
	{
		ValidateAndClearCacheIfNeeded(ref _callerObject, Caller);
		return _callerObject ??= Caller is null ? new None() : await mediator.Send(new GetObjectNodeQuery(Caller.Value));
	}

	/// <summary>
	/// The executor of a command is the object actually carrying out the command or running the code: %!
	/// </summary>
	/// <param name="mediator">Mediator to get the object node with.</param>
	/// <returns>A ValueTask containing either a SharpObject, or it will throw.</returns>
	public async ValueTask<AnySharpObject> KnownExecutorObject(IMediator mediator)
		=> (await ExecutorObject(mediator)).Known();

	/// <summary>
	/// The enactor is the object which causes something to happen: %# or %:
	/// </summary>
	/// <param name="mediator">Mediator to get the object node with.</param>
	/// <returns>A ValueTask containing either a SharpObject, or it will throw.</returns>
	public async ValueTask<AnySharpObject> KnownEnactorObject(IMediator mediator)
		=> (await EnactorObject(mediator)).Known();

	/// <summary>
	/// The caller is the object which causes an attribute to be evaluated (for instance, by using ufun() or a similar function): %@
	/// </summary>
	/// <param name="mediator">Mediator to get the object node with.</param>
	/// <returns>A ValueTask containing either a SharpObject, or it will throw.</returns>
	public async ValueTask<AnySharpObject> KnownCallerObject(IMediator mediator)
		=> (await CallerObject(mediator)).Known();

	/// <summary>
	/// Just the numbered arguments, %0-%9 etc., in numerical order. This excludes named arguments.
	/// </summary>
	public ImmutableSortedDictionary<string, CallState> ArgumentsOrdered => Arguments
		.Where(x => int.TryParse(x.Key, out _))
		.OrderBy(x => int.Parse(x.Key))
		.ToImmutableSortedDictionary();

	/// <summary>
	/// Add a register value to the Register stack.
	/// </summary>
	/// <param name="register">Register string.</param>
	/// <param name="value">Value.</param>
	/// <returns>Success if it was a valid register.</returns>
	public bool AddRegister(string register, MString value)
	{
		// Validate register pattern: alphanumeric characters, underscores, and hyphens
		// Register names should be uppercase and match pattern: [A-Z0-9_-]+
		if (string.IsNullOrEmpty(register) || !RegisterNameRegex().IsMatch(register))
		{
			return false;
		}

		var canPeek = Registers.TryPeek(out var top);
		if (!canPeek)
		{
			return false;
		}

		if (!top!.TryAdd(register, value))
		{
			top[register] = value;
		}

		return true;
	}

	[GeneratedRegex(@"^[A-Z0-9_\-]+$")]
	private static partial Regex RegisterNameRegex();
}