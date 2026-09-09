namespace SharpMUSH.Library.Definitions;

/// <summary>Shared bounds used by function implementations and the evaluator.</summary>
public static class FunctionLimits
{
	/// <summary>
	/// Maximum UTF-16 code units in one function result. Producers can reject oversized
	/// expansion before allocating; the evaluator also checks every completed result.
	/// </summary>
	public const int MaxOutputCodeUnits = 5 * 1024 * 1024;
}
