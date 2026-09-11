// Positional accessors (IsT0/AsT0/Match/Switch/FromT0) for call sites not yet rewritten to pattern
// matching. Generated scaffolding: it goes away once nothing calls it.

using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

partial class AnyOptionalSharpObjectOrError
{
	public bool IsT0 => Value is SharpPlayer;
	public bool IsT1 => Value is SharpRoom;
	public bool IsT2 => Value is SharpExit;
	public bool IsT3 => Value is SharpThing;
	public bool IsT4 => Value is None;
	public bool IsT5 => Value is Error<string>;

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

	public None AsT4 => Value is None t4
		? t4
		: throw new InvalidOperationException($"Cannot return as T4 as result is {Value?.GetType().Name ?? "null"}");

	public Error<string> AsT5 => Value is Error<string> t5
		? t5
		: throw new InvalidOperationException($"Cannot return as T5 as result is {Value?.GetType().Name ?? "null"}");

	public TResult Match<TResult>(Func<SharpPlayer, TResult> f0, Func<SharpRoom, TResult> f1, Func<SharpExit, TResult> f2, Func<SharpThing, TResult> f3, Func<None, TResult> f4, Func<Error<string>, TResult> f5) => Value switch
	{
		SharpPlayer t0 => f0(t0),
		SharpRoom t1 => f1(t1),
		SharpExit t2 => f2(t2),
		SharpThing t3 => f3(t3),
		None t4 => f4(t4),
		Error<string> t5 => f5(t5),
		_ => throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}")
	};

	public void Switch(Action<SharpPlayer> f0, Action<SharpRoom> f1, Action<SharpExit> f2, Action<SharpThing> f3, Action<None> f4, Action<Error<string>> f5)
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
			case None t4:
				f4(t4);
				return;
			case Error<string> t5:
				f5(t5);
				return;
			default:
				throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}");
		}
	}

	public static AnyOptionalSharpObjectOrError FromT0(SharpPlayer value) => new(value);
	public static AnyOptionalSharpObjectOrError FromT1(SharpRoom value) => new(value);
	public static AnyOptionalSharpObjectOrError FromT2(SharpExit value) => new(value);
	public static AnyOptionalSharpObjectOrError FromT3(SharpThing value) => new(value);
	public static AnyOptionalSharpObjectOrError FromT4(None value) => new(value);
	public static AnyOptionalSharpObjectOrError FromT5(Error<string> value) => new(value);
}
