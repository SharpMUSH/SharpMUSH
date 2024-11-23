using System.Collections.Concurrent;
using OneOf.Types;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.ParserInterfaces;

public enum ParseMode
{
	Default,
	NoParse
}

public class IterationWrapper<T>
{
	public required T Value { get; set; }
	public uint Iteration { get; set; } = 0;
	public bool Break { get; set; } = false;
}

public record ParserState(
	ConcurrentStack<Dictionary<string, MString>> Registers,
	ConcurrentStack<IterationWrapper<MString>> IterationRegisters,
	ConcurrentStack<Dictionary<string, MString>> RegexRegisters,
	DBAttribute? CurrentEvaluation,
	string? Function,
	string? Command,
	IEnumerable<string> Switches,
	ConcurrentDictionary<string, CallState> Arguments,
	DBRef? Executor,
	DBRef? Enactor,
	DBRef? Caller,
	string? Handle,
	ParseMode ParseMode = ParseMode.Default)
{
	private AnyOptionalSharpObject? _executorObject;
	private AnyOptionalSharpObject? _enactorObject;
	private AnyOptionalSharpObject? _callerObject;
	
	public async ValueTask<AnyOptionalSharpObject> ExecutorObject(ISharpDatabase db) 
		=> _executorObject ??= Executor is null ? new None() : await db.GetObjectNodeAsync(Executor.Value);
	public async ValueTask<AnyOptionalSharpObject> EnactorObject(ISharpDatabase db) 
		=> _enactorObject ??= Enactor is null ? new None() : await db.GetObjectNodeAsync(Enactor.Value);
	public async ValueTask<AnyOptionalSharpObject> CallerObject(ISharpDatabase db) 
		=> _callerObject ??= Caller is null ? new None() : await db.GetObjectNodeAsync(Caller.Value);

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