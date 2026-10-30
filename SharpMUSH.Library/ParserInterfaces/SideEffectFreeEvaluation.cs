namespace SharpMUSH.Library.ParserInterfaces;

/// <summary>
/// An evaluation in which every side-effect function answers <c>#-1 FUNCTION DISABLED</c>, as PennMUSH's
/// <c>nosidefx</c> restriction does for one function, for the whole evaluation and every <c>u()</c> it reaches. A
/// reader that must change nothing, such as a map drawing its places through softcode, evaluates inside it.
/// </summary>
public static class SideEffectFreeEvaluation
{
	private static readonly AsyncLocal<bool> Ambient = new();

	/// <summary>Whether the current evaluation refuses side-effect functions.</summary>
	public static bool Active => Ambient.Value;

	/// <summary>Refuses side-effect functions until the returned scope is disposed.</summary>
	public static IDisposable Enter()
	{
		var previous = Ambient.Value;
		Ambient.Value = true;
		return new Scope(previous);
	}

	private sealed class Scope(bool previous) : IDisposable
	{
		public void Dispose() => Ambient.Value = previous;
	}
}
