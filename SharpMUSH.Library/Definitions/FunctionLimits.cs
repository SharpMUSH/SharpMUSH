using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Definitions;

/// <summary>Output ceiling, checked before expanding or joining text as well as at dispatch.</summary>
public static class FunctionLimits
{
	/// <summary>Maximum UTF-16 code units in one function result, for anyone. Producers check before
	/// expansion; the evaluator also checks every completed result. An evaluation may lower it
	/// (<see cref="ParserState.OutputLimit"/>), never raise it.</summary>
	public const int MaxOutputCodeUnits = 5 * 1024 * 1024;

	public static bool ExceedsOutput(ParserState state, long characters) => characters > state.OutputLimit;

	public static bool ExceedsCombinedOutput(ParserState state, IEnumerable<MString> values, int separatorLength = 0)
	{
		long length = 0;
		var first = true;
		foreach (var value in values)
		{
			length += value.Length + (first ? 0L : separatorLength);
			if (ExceedsOutput(state, length)) return true;
			first = false;
		}
		return false;
	}

	public static CallState RejectOutput(ParserState state)
	{
		if (state.LimitExceeded is { } exceeded)
		{
			exceeded.IsExceeded = true;
			exceeded.ErrorMessage ??= ErrorMessages.Returns.OutputTooLarge;
		}
		return new CallState(ErrorMessages.Returns.OutputTooLarge);
	}
}
