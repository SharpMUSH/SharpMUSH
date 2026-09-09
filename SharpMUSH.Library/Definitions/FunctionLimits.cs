using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Definitions;

/// <summary>Shared output ceiling, checked before expanding or joining text as well as at dispatch.</summary>
public static class FunctionLimits
{
	/// <summary>Maximum UTF-16 code units in one function result. Producers check before
	/// expansion; the evaluator also checks every completed result.</summary>
	public const int MaxOutputCodeUnits = 5 * 1024 * 1024;
	public static bool ExceedsOutput(long characters) => characters > MaxOutputCodeUnits;

	public static bool ExceedsCombinedOutput(IEnumerable<MString> values, int separatorLength = 0)
	{
		long length = 0;
		var first = true;
		foreach (var value in values)
		{
			length += value.Length + (first ? 0L : separatorLength);
			if (ExceedsOutput(length)) return true;
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
