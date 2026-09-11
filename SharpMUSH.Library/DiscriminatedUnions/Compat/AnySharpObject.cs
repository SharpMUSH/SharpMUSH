// Positional accessors (IsT0/AsT0/Match/Switch/FromT0) for call sites not yet rewritten to pattern
// matching. Generated scaffolding: it goes away once nothing calls it.

using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

partial class AnySharpObject
{
	public bool IsT0 => Value is SharpPlayer;
	public bool IsT1 => Value is SharpRoom;
	public bool IsT2 => Value is SharpExit;
	public bool IsT3 => Value is SharpThing;

	public SharpPlayer AsT0 => Value is SharpPlayer t0
		? t0
		: throw new InvalidOperationException($"Cannot return as T0 as result is {Value?.GetType().Name ?? "null"}");

	public SharpRoom AsT1 => Value is SharpRoom t1
		? t1
		: throw new InvalidOperationException($"Cannot return as T1 as result is {Value?.GetType().Name ?? "null"}");

	public SharpExit AsT2 => Value is SharpExit t2
		? t2
		: throw new InvalidOperationException($"Cannot return as T2 as result is {Value?.GetType().Name ?? "null"}");

	public SharpThing AsT3 => Value is SharpThing t3
		? t3
		: throw new InvalidOperationException($"Cannot return as T3 as result is {Value?.GetType().Name ?? "null"}");

	public TResult Match<TResult>(Func<SharpPlayer, TResult> f0, Func<SharpRoom, TResult> f1, Func<SharpExit, TResult> f2, Func<SharpThing, TResult> f3) => Value switch
	{
		SharpPlayer t0 => f0(t0),
		SharpRoom t1 => f1(t1),
		SharpExit t2 => f2(t2),
		SharpThing t3 => f3(t3),
		_ => throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}")
	};

	public void Switch(Action<SharpPlayer> f0, Action<SharpRoom> f1, Action<SharpExit> f2, Action<SharpThing> f3)
	{
		switch (Value)
		{
			case SharpPlayer t0:
				f0(t0);
				return;
			case SharpRoom t1:
				f1(t1);
				return;
			case SharpExit t2:
				f2(t2);
				return;
			case SharpThing t3:
				f3(t3);
				return;
			default:
				throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}");
		}
	}

	public static AnySharpObject FromT0(SharpPlayer value) => new(value);
	public static AnySharpObject FromT1(SharpRoom value) => new(value);
	public static AnySharpObject FromT2(SharpExit value) => new(value);
	public static AnySharpObject FromT3(SharpThing value) => new(value);
}
