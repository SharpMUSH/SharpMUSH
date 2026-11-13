namespace SharpMUSH.Library.ParserInterfaces;

/// <summary>
/// An evaluation in which every side-effect function answers <c>#-1 FUNCTION DISABLED</c>, as PennMUSH's
/// <c>nosidefx</c> restriction does for one function, for the whole evaluation and every <c>u()</c> it reaches. A
/// reader that must change nothing, such as a map drawing its places through softcode, evaluates inside it. A scope
/// can also refuse functions whose answer varies from call to call (<c>FunctionFlags.NonDeterministic</c>), as
/// <c>memo()</c>'s does.
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
	/// lost a side effect from one that had none. With <paramref name="deterministic"/>, functions whose answer varies
	/// are refused too (<see cref="SideEffectFreeScope.RefusedNonDeterministic"/>).
	/// </summary>
	public static SideEffectFreeScope Track(bool deterministic = false)
	{
		var scope = new SideEffectFreeScope(Ambient.Value, deterministic);
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

	/// <summary>
	/// Whether the current evaluation refuses an answer that varies between calls; when it does, records the refusal
	/// in every enclosing scope. A function that is only sometimes non-deterministic (one that reads the clock when no
	/// time is given) asks this before it does, and answers <c>#-1 FUNCTION DISABLED</c> when it is true.
	/// </summary>
	public static bool RefuseNonDeterministic()
	{
		var refused = false;
		for (var current = Ambient.Value; current is not null; current = current.Enclosing)
		{
			refused |= current.Deterministic;
		}

		if (!refused) return false;
		for (var current = Ambient.Value; current is not null; current = current.Enclosing)
		{
			current.RefusedNonDeterministic = true;
		}

		return true;
	}

	internal static void Leave(SideEffectFreeScope scope) => Ambient.Value = scope.Enclosing;
}

/// <summary>One <see cref="SideEffectFreeEvaluation.Track"/> scope.</summary>
public sealed class SideEffectFreeScope : IDisposable
{
	internal SideEffectFreeScope(SideEffectFreeScope? enclosing, bool deterministic)
	{
		Enclosing = enclosing;
		Deterministic = deterministic;
	}

	internal SideEffectFreeScope? Enclosing { get; }

	/// <summary>Whether this scope refuses functions whose answer varies between calls.</summary>
	public bool Deterministic { get; }

	/// <summary>Whether a side-effect function was refused while this scope was active.</summary>
	public bool Refused { get; internal set; }

	/// <summary>Whether a function whose answer varies was refused while this scope was active.</summary>
	public bool RefusedNonDeterministic { get; internal set; }

	/// <inheritdoc />
	public void Dispose() => SideEffectFreeEvaluation.Leave(this);
}
