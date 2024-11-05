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
	Stack<Dictionary<string, MString>> Registers,
	Stack<IterationWrapper<MString>> IterationRegisters,
	Stack<Dictionary<string, MString>> RegexRegisters,
	DBAttribute? CurrentEvaluation,
	string? Function,
	string? Command,
	IEnumerable<string> Switches,
	Dictionary<string, CallState> Arguments,
	DBRef? Executor,
	DBRef? Enactor,
	DBRef? Caller,
	string? Handle,
	ParseMode ParseMode = ParseMode.Default)
{
	private AnyOptionalSharpObject? _executorObject;
	private AnyOptionalSharpObject? _enactorObject;
	private AnyOptionalSharpObject? _callerObject;
	
	public AnyOptionalSharpObject ExecutorObject(ISharpDatabase db) 
		=> _executorObject ??= Executor is null ? new None() : db.GetObjectNode(Executor.Value);
	public AnyOptionalSharpObject EnactorObject(ISharpDatabase db) 
		=> _enactorObject ??= Executor is null ? new None() : db.GetObjectNode(Executor.Value);
	public AnyOptionalSharpObject CallerObject(ISharpDatabase db) 
		=> _callerObject ??= Caller is null ? new None() : db.GetObjectNode(Caller.Value);

	public bool AddRegister(string register, MString value)
	{
		// TODO: Validate Register Pattern

		var top = Registers.Peek();
		if (!top.TryAdd(register, value))
		{
			top[register] = value;
		}

		return true;
	}
}