using System.Collections.Concurrent;
using System.Collections.Immutable;
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
	ParseMode ParseMode = ParseMode.Default)
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
		ParseMode.Default);
	
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
		// TODO: Validate Register Pattern
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