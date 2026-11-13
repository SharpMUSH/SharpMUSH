namespace SharpMUSH.Library.ParserInterfaces;

/// <summary>
/// An evaluation in which every side-effect function answers <c>#-1 FUNCTION DISABLED</c>, as PennMUSH's
/// <c>nosidefx</c> restriction does for one function, for the whole evaluation and every <c>u()</c> it reaches. A
/// reader that must change nothing, such as a map drawing its places through softcode, evaluates inside it.
/// </summary>
public static class SideEffectFreeEvaluation
{
	private static readonly AsyncLocal<SideEffectFreeScope?> Ambient = new();

	/// <summary>Whether the current evaluation refuses side-effect functions.</summary>
	public static bool Active => Ambient.Value is not null;

	/// <summary>Refuses side-effect functions until the returned scope is disposed.</summary>
	public static IDisposable Enter() => Track();

	/// <summary>
	/// Refuses side-effect functions until the returned scope is disposed, and records whether any was refused in it
	/// (<see cref="SideEffectFreeScope.Refused"/>), so a caller such as <c>memo()</c> can tell an answer that
	/// lost a side effect from one that had none.
	/// </summary>
	public static SideEffectFreeScope Track()
	{
		var scope = new SideEffectFreeScope(Ambient.Value);
		Ambient.Value = scope;
		return scope;
	}

	/// <summary>
	/// Whether the current evaluation refuses side effects; when it does, records the refusal in every enclosing
	/// scope. A function whose side effect is not covered by <c>HasSideFX</c> (one option of it, say) asks this before
	/// it acts, and answers <c>#-1 FUNCTION DISABLED</c> when it is true.
	/// </summary>
	public static bool Refuse()
	{
		if (Ambient.Value is not { } scope) return false;
		for (var current = scope; current is not null; current = current.Enclosing)
		{
			current.Refused = true;
		}

		return true;
	}

	internal static void Leave(SideEffectFreeScope scope) => Ambient.Value = scope.Enclosing;
}

/// <summary>One <see cref="SideEffectFreeEvaluation.Track"/> scope.</summary>
public sealed class SideEffectFreeScope : IDisposable
{
	internal SideEffectFreeScope(SideEffectFreeScope? enclosing) => Enclosing = enclosing;

	internal SideEffectFreeScope? Enclosing { get; }

	/// <summary>Whether a side-effect function was refused while this scope was active.</summary>
	public bool Refused { get; internal set; }

	/// <inheritdoc />
	public void Dispose() => SideEffectFreeEvaluation.Leave(this);
}
