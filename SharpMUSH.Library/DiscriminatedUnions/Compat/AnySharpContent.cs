// Positional accessors (IsT0/AsT0/Match/Switch/FromT0) for call sites not yet rewritten to pattern
// matching. Generated scaffolding: it goes away once nothing calls it.

using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

partial class AnySharpContent
{
	public bool IsT0 => Value is SharpPlayer;
	public bool IsT1 => Value is SharpExit;
	public bool IsT2 => Value is SharpThing;

	public SharpPlayer AsT0 => Value is SharpPlayer t0
		? t0
		: throw new InvalidOperationException($"Cannot return as T0 as result is {Value?.GetType().Name ?? "null"}");

	public SharpExit AsT1 => Value is SharpExit t1
		? t1
		: throw new InvalidOperationException($"Cannot return as T1 as result is {Value?.GetType().Name ?? "null"}");

	public SharpThing AsT2 => Value is SharpThing t2
		? t2
		: throw new InvalidOperationException($"Cannot return as T2 as result is {Value?.GetType().Name ?? "null"}");

	public TResult Match<TResult>(Func<SharpPlayer, TResult> f0, Func<SharpExit, TResult> f1, Func<SharpThing, TResult> f2) => Value switch
	{
		SharpPlayer t0 => f0(t0),
		SharpExit t1 => f1(t1),
		SharpThing t2 => f2(t2),
		_ => throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}")
	};

	public void Switch(Action<SharpPlayer> f0, Action<SharpExit> f1, Action<SharpThing> f2)
	{
		switch (Value)
		{
			case SharpPlayer t0:
				f0(t0);
				return;
			case SharpExit t1:
				f1(t1);
				return;
			case SharpThing t2:
				f2(t2);
				return;
			default:
				throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}");
		}
	}

	public static AnySharpContent FromT0(SharpPlayer value) => new(value);
	public static AnySharpContent FromT1(SharpExit value) => new(value);
	public static AnySharpContent FromT2(SharpThing value) => new(value);
}
