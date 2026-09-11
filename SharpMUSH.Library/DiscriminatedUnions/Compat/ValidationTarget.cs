// Positional accessors (IsT0/AsT0/Match/Switch/FromT0) for call sites not yet rewritten to pattern
// matching. Generated scaffolding: it goes away once nothing calls it.

using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

partial union ValidationTarget
{
	public bool IsT0 => Value is AnySharpObject;
	public bool IsT1 => Value is SharpAttributeEntry;
	public bool IsT2 => Value is SharpChannel;
	public bool IsT3 => Value is None;

	public AnySharpObject AsT0 => Value is AnySharpObject t0
		? t0
		: throw new InvalidOperationException($"Cannot return as T0 as result is {Value?.GetType().Name ?? "null"}");

	public SharpAttributeEntry AsT1 => Value is SharpAttributeEntry t1
		? t1
		: throw new InvalidOperationException($"Cannot return as T1 as result is {Value?.GetType().Name ?? "null"}");

	public SharpChannel AsT2 => Value is SharpChannel t2
		? t2
		: throw new InvalidOperationException($"Cannot return as T2 as result is {Value?.GetType().Name ?? "null"}");

	public None AsT3 => Value is None t3
		? t3
		: throw new InvalidOperationException($"Cannot return as T3 as result is {Value?.GetType().Name ?? "null"}");

	public TResult Match<TResult>(Func<AnySharpObject, TResult> f0, Func<SharpAttributeEntry, TResult> f1, Func<SharpChannel, TResult> f2, Func<None, TResult> f3) => Value switch
	{
		AnySharpObject t0 => f0(t0),
		SharpAttributeEntry t1 => f1(t1),
		SharpChannel t2 => f2(t2),
		None t3 => f3(t3),
		_ => throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}")
	};

	public void Switch(Action<AnySharpObject> f0, Action<SharpAttributeEntry> f1, Action<SharpChannel> f2, Action<None> f3)
	{
		switch (Value)
		{
			case AnySharpObject t0:
				f0(t0);
				return;
			case SharpAttributeEntry t1:
				f1(t1);
				return;
			case SharpChannel t2:
				f2(t2);
				return;
			case None t3:
				f3(t3);
				return;
			default:
				throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}");
		}
	}

	public static ValidationTarget FromT0(AnySharpObject value) => new(value);
	public static ValidationTarget FromT1(SharpAttributeEntry value) => new(value);
	public static ValidationTarget FromT2(SharpChannel value) => new(value);
	public static ValidationTarget FromT3(None value) => new(value);
}
