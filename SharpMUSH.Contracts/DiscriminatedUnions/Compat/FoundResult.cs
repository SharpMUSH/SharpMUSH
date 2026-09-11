// Positional accessors (IsT0/AsT0/Match/Switch/FromT0) for call sites not yet rewritten to pattern
// matching. Generated scaffolding: it goes away once nothing calls it.

namespace SharpMUSH.Library.DiscriminatedUnions;

partial union FoundResult<T>
{
	public bool IsT0 => Value is T;
	public bool IsT1 => Value is NotFound;
	public bool IsT2 => Value is Error<string>;

	public T AsT0 => Value is T t0
		? t0
		: throw new InvalidOperationException($"Cannot return as T0 as result is {Value?.GetType().Name ?? "null"}");

	public NotFound AsT1 => Value is NotFound t1
		? t1
		: throw new InvalidOperationException($"Cannot return as T1 as result is {Value?.GetType().Name ?? "null"}");

	public Error<string> AsT2 => Value is Error<string> t2
		? t2
		: throw new InvalidOperationException($"Cannot return as T2 as result is {Value?.GetType().Name ?? "null"}");

	public TResult Match<TResult>(Func<T, TResult> f0, Func<NotFound, TResult> f1, Func<Error<string>, TResult> f2) => Value switch
	{
		T t0 => f0(t0),
		NotFound t1 => f1(t1),
		Error<string> t2 => f2(t2),
		_ => throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}")
	};

	public void Switch(Action<T> f0, Action<NotFound> f1, Action<Error<string>> f2)
	{
		switch (Value)
		{
			case T t0:
				f0(t0);
				return;
			case NotFound t1:
				f1(t1);
				return;
			case Error<string> t2:
				f2(t2);
				return;
			default:
				throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}");
		}
	}

	public static FoundResult<T> FromT0(T value) => new(value);
	public static FoundResult<T> FromT1(NotFound value) => new(value);
	public static FoundResult<T> FromT2(Error<string> value) => new(value);
}
