using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Library.ParserInterfaces;

/// <summary>
/// The output limit of the evaluation in progress. A parser root that a service creates while
/// that evaluation runs (an evaluation lock, a triggered attribute) has no caller state to copy
/// the limit from; <see cref="ParserState.OutputLimit"/> starts from this instead, and a root that
/// takes <see cref="Flag"/> reports a limit it hits to the evaluation that caused it.
/// </summary>
public sealed class OutputCeiling
{
	private static readonly AsyncLocal<OutputCeiling?> Ambient = new();

	private OutputCeiling(int limit, LimitExceededFlag? flag) => (Limit, Flag) = (limit, flag);

	public static OutputCeiling? Current => Ambient.Value;

	/// <summary>Most UTF-16 code units one function may produce in this flow.</summary>
	public int Limit { get; }

	/// <summary>The limit flag of the evaluation that lowered the ceiling.</summary>
	public LimitExceededFlag? Flag { get; }

	/// <summary>The ceiling a new parser root starts with.</summary>
	public static int CurrentLimit => Current?.Limit ?? FunctionLimits.MaxOutputCodeUnits;

	/// <summary>
	/// Lowers the ceiling to <paramref name="state"/>'s for the rest of this flow, until disposed.
	/// A state that would not lower it changes nothing: an evaluation never raises the ceiling.
	/// </summary>
	public static IDisposable? Enter(ParserState state)
	{
		if (state.OutputLimit >= CurrentLimit) return null;
		var previous = Current;
		Ambient.Value = new OutputCeiling(state.OutputLimit, state.LimitExceeded);
		return new Scope(previous);
	}

	private sealed class Scope(OutputCeiling? previous) : IDisposable
	{
		public void Dispose() => Ambient.Value = previous;
	}
}
