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
	List<CallState> Arguments,
	DBRef? Executor,
	DBRef? Enactor,
	DBRef? Caller,
	string? Handle,
	ParseMode ParseMode = ParseMode.Default)
{
	public AnyOptionalSharpObject ExecutorObject(ISharpDatabase db) => Executor is null ? new None() : db.GetObjectNode(Executor.Value);
	public AnyOptionalSharpObject EnactorObject(ISharpDatabase db) => Executor is null ? new None() : db.GetObjectNode(Executor.Value);
	public AnyOptionalSharpObject CallerObject(ISharpDatabase db) => Executor is null ? new None() : db.GetObjectNode(Executor.Value);

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