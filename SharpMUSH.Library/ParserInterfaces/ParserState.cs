using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text;
using Mediator;
using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Library.ParserInterfaces;

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

/// <summary>
/// Tracks debug information for an expression evaluation
/// </summary>
public class DebugInfo
{
	/// <summary>
	/// The expression being evaluated
	/// </summary>
	public required string Expression { get; set; }
	
	/// <summary>
	/// The nesting depth of this evaluation
	/// </summary>
	public required int Depth { get; set; }
	
	/// <summary>
	/// The executor dbref for this evaluation
	/// </summary>
	public required DBRef Executor { get; set; }
	
	/// <summary>
	/// Previous debug info in the linked list (for traversal)
	/// </summary>
	public DebugInfo? Previous { get; set; }
	
	/// <summary>
	/// Next debug info in the linked list (for traversal)
	/// </summary>
	public DebugInfo? Next { get; set; }
}

/// <summary>
/// Debug context for tracking debug output during parser evaluation
/// </summary>
public class DebugContext
{
	/// <summary>
	/// Current nesting depth for indentation
	/// </summary>
	public int Depth { get; set; } = 0;
	
	/// <summary>
	/// Debugging mode: 0=disabled, -1=explicitly disabled, 1=explicitly enabled
	/// </summary>
	public int DebuggingMode { get; set; } = 0;
	
	/// <summary>
	/// Head of linked list of DebugInfo objects tracking evaluation hierarchy
	/// </summary>
	public DebugInfo? DebugStrings { get; set; }
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
/// <param name="DebugContext">Debug context for tracking debug output</param>
public record ParserState(
	ConcurrentStack<Dictionary<string, MString>> Registers,
	ConcurrentStack<IterationWrapper<MString>> IterationRegisters,
	ConcurrentStack<Dictionary<string, MString>> RegexRegisters,
	ConcurrentStack<Execution> ExecutionStack,
	Dictionary<string, CallState> EnvironmentRegisters,
	DBAttribute? CurrentEvaluation,
	int? ParserFunctionDepth,
	string? Function,
	string? Command,
	Func<IMUSHCodeParser,ValueTask<Option<CallState>>> CommandInvoker,
	IEnumerable<string> Switches,
	Dictionary<string, CallState> Arguments,
	DBRef? Executor,
	DBRef? Enactor,
	DBRef? Caller,
	long? Handle,
	ParseMode ParseMode = ParseMode.Default,
	HttpResponseContext? HttpResponse = null,
	DebugContext? DebugContext = null)
{
	private AnyOptionalSharpObject? _executorObject;
	private AnyOptionalSharpObject? _enactorObject;
	private AnyOptionalSharpObject? _callerObject;

	public static ParserState Empty => new(
		new ConcurrentStack<Dictionary<string, MString>>(),
		new ConcurrentStack<IterationWrapper<MString>>(),
		new ConcurrentStack<Dictionary<string, MString>>(),
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
		null);
	
	/// <summary>
	/// The executor of a command is the object actually carrying out the command or running the code: %!
	/// </summary>
	/// <param name="mediator">Mediator to get the object node with.</param>
	/// <returns>A ValueTask containing either a SharpObject, or None.</returns>
	public async ValueTask<AnyOptionalSharpObject> ExecutorObject(IMediator mediator)
		=> _executorObject ??= Executor is null ? new None() : await mediator.Send(new GetObjectNodeQuery(Executor.Value));

	/// <summary>
	/// The enactor is the object which causes something to happen: %# or %:
	/// </summary>
	/// <param name="mediator">Mediator to get the object node with.</param>
	/// <returns>A ValueTask containing either a SharpObject, or None.</returns>
	public async ValueTask<AnyOptionalSharpObject> EnactorObject(IMediator mediator)
		=> _enactorObject ??= Enactor is null ? new None() : await mediator.Send(new GetObjectNodeQuery(Enactor.Value));

	/// <summary>
	/// The caller is the object which causes an attribute to be evaluated (for instance, by using ufun() or a similar function): %@
	/// </summary>
	/// <param name="mediator">Mediator to get the object node with.</param>
	/// <returns>A ValueTask containing either a SharpObject, or None.</returns>
	public async ValueTask<AnyOptionalSharpObject> CallerObject(IMediator mediator)
		=> _callerObject ??= Caller is null ? new None() : await mediator.Send(new GetObjectNodeQuery(Caller.Value));
	
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
	/// <exception cref="Exception">If we somehow failed to peek. Fatal.</exception>
	public bool AddRegister(string register, MString value)
	{
		// Validate register pattern: alphanumeric characters, underscores, and hyphens
		// Register names should be uppercase and match pattern: [A-Z0-9_-]+
		if (string.IsNullOrEmpty(register) || !System.Text.RegularExpressions.Regex.IsMatch(register, @"^[A-Z0-9_\-]+$"))
		{
			return false;
		}
		
		var canPeek = Registers.TryPeek(out var top);
		if (!canPeek)
		{
			throw new Exception("Could not peek!");
		}

		if (!top!.TryAdd(register, value))
		{
			top[register] = value;
		}

		return true;
	}
}