// Positional accessors (IsT0/AsT0/Match/Switch/FromT0) for call sites not yet rewritten to pattern
// matching. Generated scaffolding: it goes away once nothing calls it.

using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

partial class LazySharpAttributesOrError
{
	public bool IsT0 => Value is IAsyncEnumerable<LazySharpAttribute>;
	public bool IsT1 => Value is Error<string>;

	public IAsyncEnumerable<LazySharpAttribute> AsT0 => Value is IAsyncEnumerable<LazySharpAttribute> t0
		? t0
		: throw new InvalidOperationException($"Cannot return as T0 as result is {Value?.GetType().Name ?? "null"}");

	public Error<string> AsT1 => Value is Error<string> t1
		? t1
		: throw new InvalidOperationException($"Cannot return as T1 as result is {Value?.GetType().Name ?? "null"}");

	public TResult Match<TResult>(Func<IAsyncEnumerable<LazySharpAttribute>, TResult> f0, Func<Error<string>, TResult> f1) => Value switch
	{
		IAsyncEnumerable<LazySharpAttribute> t0 => f0(t0),
		Error<string> t1 => f1(t1),
		_ => throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}")
	};

	public void Switch(Action<IAsyncEnumerable<LazySharpAttribute>> f0, Action<Error<string>> f1)
	{
		switch (Value)
		{
			case IAsyncEnumerable<LazySharpAttribute> t0:
				f0(t0);
				return;
			case Error<string> t1:
				f1(t1);
				return;
			default:
				throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}");
		}
	}

	public static LazySharpAttributesOrError FromT0(IAsyncEnumerable<LazySharpAttribute> value) => new(value);
	public static LazySharpAttributesOrError FromT1(Error<string> value) => new(value);
}
