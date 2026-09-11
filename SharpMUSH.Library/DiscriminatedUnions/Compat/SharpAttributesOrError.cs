// Positional accessors (IsT0/AsT0/Match/Switch/FromT0) for call sites not yet rewritten to pattern
// matching. Generated scaffolding: it goes away once nothing calls it.

using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

partial class SharpAttributesOrError
{
	public bool IsT0 => Value is SharpAttribute[];
	public bool IsT1 => Value is Error<string>;

	public SharpAttribute[] AsT0 => Value is SharpAttribute[] t0
		? t0
		: throw new InvalidOperationException($"Cannot return as T0 as result is {Value?.GetType().Name ?? "null"}");

	public Error<string> AsT1 => Value is Error<string> t1
		? t1
		: throw new InvalidOperationException($"Cannot return as T1 as result is {Value?.GetType().Name ?? "null"}");

	public TResult Match<TResult>(Func<SharpAttribute[], TResult> f0, Func<Error<string>, TResult> f1) => Value switch
	{
		SharpAttribute[] t0 => f0(t0),
		Error<string> t1 => f1(t1),
		_ => throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}")
	};

	public void Switch(Action<SharpAttribute[]> f0, Action<Error<string>> f1)
	{
		switch (Value)
		{
			case SharpAttribute[] t0:
				f0(t0);
				return;
			case Error<string> t1:
				f1(t1);
				return;
			default:
				throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}");
		}
	}

	public static SharpAttributesOrError FromT0(SharpAttribute[] value) => new(value);
	public static SharpAttributesOrError FromT1(Error<string> value) => new(value);
}
