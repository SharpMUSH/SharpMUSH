// Positional accessors (IsT0/AsT0/Match/Switch/FromT0) for call sites not yet rewritten to pattern
// matching. Generated scaffolding: it goes away once nothing calls it.

namespace SharpMUSH.Library.DiscriminatedUnions;

partial union Found<T>
{
	public bool IsT0 => Value is T;
	public bool IsT1 => Value is NotFound;

	public T AsT0 => Value is T t0
		? t0
		: throw new InvalidOperationException($"Cannot return as T0 as result is {Value?.GetType().Name ?? "null"}");

	public NotFound AsT1 => Value is NotFound t1
		? t1
		: throw new InvalidOperationException($"Cannot return as T1 as result is {Value?.GetType().Name ?? "null"}");

	public TResult Match<TResult>(Func<T, TResult> f0, Func<NotFound, TResult> f1) => Value switch
	{
		T t0 => f0(t0),
		NotFound t1 => f1(t1),
		_ => throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}")
	};

	public void Switch(Action<T> f0, Action<NotFound> f1)
	{
		switch (Value)
		{
			case T t0:
				f0(t0);
				return;
			case NotFound t1:
				f1(t1);
				return;
			default:
				throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}");
		}
	}

	public static Found<T> FromT0(T value) => new(value);
	public static Found<T> FromT1(NotFound value) => new(value);
}
