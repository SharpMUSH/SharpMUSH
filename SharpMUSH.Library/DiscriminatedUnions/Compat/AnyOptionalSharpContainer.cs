// Positional accessors (IsT0/AsT0/Match/Switch/FromT0) for call sites not yet rewritten to pattern
// matching. Generated scaffolding: it goes away once nothing calls it.

using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

partial class AnyOptionalSharpContainer
{
	public bool IsT0 => Value is SharpPlayer;
	public bool IsT1 => Value is SharpRoom;
	public bool IsT2 => Value is SharpThing;
	public bool IsT3 => Value is None;

	public SharpPlayer AsT0 => Value is SharpPlayer t0
		? t0
		: throw new InvalidOperationException($"Cannot return as T0 as result is {Value?.GetType().Name ?? "null"}");

	public SharpRoom AsT1 => Value is SharpRoom t1
		? t1
		: throw new InvalidOperationException($"Cannot return as T1 as result is {Value?.GetType().Name ?? "null"}");

	public SharpThing AsT2 => Value is SharpThing t2
		? t2
		: throw new InvalidOperationException($"Cannot return as T2 as result is {Value?.GetType().Name ?? "null"}");

	public None AsT3 => Value is None t3
		? t3
		: throw new InvalidOperationException($"Cannot return as T3 as result is {Value?.GetType().Name ?? "null"}");

	public TResult Match<TResult>(Func<SharpPlayer, TResult> f0, Func<SharpRoom, TResult> f1, Func<SharpThing, TResult> f2, Func<None, TResult> f3) => Value switch
	{
		SharpPlayer t0 => f0(t0),
		SharpRoom t1 => f1(t1),
		SharpThing t2 => f2(t2),
		None t3 => f3(t3),
		_ => throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}")
	};

	public void Switch(Action<SharpPlayer> f0, Action<SharpRoom> f1, Action<SharpThing> f2, Action<None> f3)
	{
		switch (Value)
		{
			case SharpPlayer t0:
				f0(t0);
				return;
			case SharpRoom t1:
				f1(t1);
				return;
			case SharpThing t2:
				f2(t2);
				return;
			case None t3:
				f3(t3);
				return;
			default:
				throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}");
		}
	}

	public static AnyOptionalSharpContainer FromT0(SharpPlayer value) => new(value);
	public static AnyOptionalSharpContainer FromT1(SharpRoom value) => new(value);
	public static AnyOptionalSharpContainer FromT2(SharpThing value) => new(value);
	public static AnyOptionalSharpContainer FromT3(None value) => new(value);
}
