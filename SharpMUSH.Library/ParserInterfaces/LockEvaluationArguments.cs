using System.Collections.Immutable;

namespace SharpMUSH.Library.ParserInterfaces;

/// <summary>Per-evaluation lock arguments; cached lock delegates read these at invocation time.</summary>
public static class LockEvaluationArguments
{
	private static readonly AsyncLocal<ImmutableDictionary<string, MString>?> Ambient = new();

	public static Dictionary<string, CallState> CreateArguments() =>
		(Ambient.Value ?? ImmutableDictionary<string, MString>.Empty)
			.ToDictionary(pair => pair.Key, pair => new CallState(pair.Value));

	/// <summary>Enters synchronously and restores the outer arguments when disposed.</summary>
	public static IDisposable Enter(IEnumerable<KeyValuePair<string, MString>> arguments)
	{
		var previous = Ambient.Value;
		Ambient.Value = arguments.ToImmutableDictionary();
		return new Scope(previous);
	}

	private sealed class Scope(ImmutableDictionary<string, MString>? previous) : IDisposable
	{
		public void Dispose() => Ambient.Value = previous;
	}
}
