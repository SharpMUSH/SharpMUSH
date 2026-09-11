// Positional accessors (IsT0/AsT0/Match/Switch/FromT0) for call sites not yet rewritten to pattern
// matching. Generated scaffolding: it goes away once nothing calls it.

using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

partial class OptionalSharpAttributeOrError
{
	public bool IsT0 => Value is SharpAttribute[];
	public bool IsT1 => Value is None;
	public bool IsT2 => Value is Error<string>;

	public SharpAttribute[] AsT0 => Value is SharpAttribute[] t0
		? t0
		: throw new InvalidOperationException($"Cannot return as T0 as result is {Value?.GetType().Name ?? "null"}");

	public None AsT1 => Value is None t1
		? t1
		: throw new InvalidOperationException($"Cannot return as T1 as result is {Value?.GetType().Name ?? "null"}");

	public Error<string> AsT2 => Value is Error<string> t2
		? t2
		: throw new InvalidOperationException($"Cannot return as T2 as result is {Value?.GetType().Name ?? "null"}");

	public TResult Match<TResult>(Func<SharpAttribute[], TResult> f0, Func<None, TResult> f1, Func<Error<string>, TResult> f2) => Value switch
	{
		SharpAttribute[] t0 => f0(t0),
		None t1 => f1(t1),
		Error<string> t2 => f2(t2),
		_ => throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}")
	};

	public void Switch(Action<SharpAttribute[]> f0, Action<None> f1, Action<Error<string>> f2)
	{
		switch (Value)
		{
			case SharpAttribute[] t0:
				f0(t0);
				return;
			case None t1:
				f1(t1);
				return;
			case Error<string> t2:
				f2(t2);
				return;
			default:
				throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}");
		}
	}

	public static OptionalSharpAttributeOrError FromT0(SharpAttribute[] value) => new(value);
	public static OptionalSharpAttributeOrError FromT1(None value) => new(value);
	public static OptionalSharpAttributeOrError FromT2(Error<string> value) => new(value);
}
