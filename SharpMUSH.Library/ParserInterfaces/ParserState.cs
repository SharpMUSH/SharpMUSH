using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.ParserInterfaces;

public enum ParseMode
{
	Default,
	NoParse
}

public record ParserState(
	Stack<Dictionary<string, MString>> Registers,
	DBAttribute? CurrentEvaluation,
	string? Function,
	string? Command,
	List<CallState> Arguments,
	DBRef? Executor,
	DBRef? Enactor,
	DBRef? Caller,
	string? Handle,
	ParseMode ParseMode = ParseMode.Default)
{
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