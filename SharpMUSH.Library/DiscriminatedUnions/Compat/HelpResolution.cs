// Positional accessors (IsT0/AsT0/Match/Switch/FromT0) for call sites not yet rewritten to pattern
// matching. Generated scaffolding: it goes away once nothing calls it.

using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.DiscriminatedUnions;

partial union HelpResolution
{
	public bool IsT0 => Value is HelpEntry;
	public bool IsT1 => Value is HelpCandidates;
	public bool IsT2 => Value is None;

	public HelpEntry AsT0 => Value is HelpEntry t0
		? t0
		: throw new InvalidOperationException($"Cannot return as T0 as result is {Value?.GetType().Name ?? "null"}");

	public HelpCandidates AsT1 => Value is HelpCandidates t1
		? t1
		: throw new InvalidOperationException($"Cannot return as T1 as result is {Value?.GetType().Name ?? "null"}");

	public None AsT2 => Value is None t2
		? t2
		: throw new InvalidOperationException($"Cannot return as T2 as result is {Value?.GetType().Name ?? "null"}");

	public TResult Match<TResult>(Func<HelpEntry, TResult> f0, Func<HelpCandidates, TResult> f1, Func<None, TResult> f2) => Value switch
	{
		HelpEntry t0 => f0(t0),
		HelpCandidates t1 => f1(t1),
		None t2 => f2(t2),
		_ => throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}")
	};

	public void Switch(Action<HelpEntry> f0, Action<HelpCandidates> f1, Action<None> f2)
	{
		switch (Value)
		{
			case HelpEntry t0:
				f0(t0);
				return;
			case HelpCandidates t1:
				f1(t1);
				return;
			case None t2:
				f2(t2);
				return;
			default:
				throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}");
		}
	}

	public static HelpResolution FromT0(HelpEntry value) => new(value);
	public static HelpResolution FromT1(HelpCandidates value) => new(value);
	public static HelpResolution FromT2(None value) => new(value);
}
