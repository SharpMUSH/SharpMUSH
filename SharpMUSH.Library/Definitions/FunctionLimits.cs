using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Definitions;

/// <summary>Shared output ceiling, checked before expanding or joining text as well as at dispatch.</summary>
public static class FunctionLimits
{
	public const int MaxOutputCharacters = 5 * 1024 * 1024;
	public static bool ExceedsOutput(long characters) => characters > MaxOutputCharacters;

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
