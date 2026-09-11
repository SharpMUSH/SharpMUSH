// Positional accessors (IsT0/AsT0/Match/Switch/FromT0) for call sites not yet rewritten to pattern
// matching. Generated scaffolding: it goes away once nothing calls it.

using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

partial union ObjidResolution
{
	public bool IsT0 => Value is string;
	public bool IsT1 => Value is NotFound;
	public bool IsT2 => Value is Error;

	public string AsT0 => Value is string t0
		? t0
		: throw new InvalidOperationException($"Cannot return as T0 as result is {Value?.GetType().Name ?? "null"}");

	public NotFound AsT1 => Value is NotFound t1
		? t1
		: throw new InvalidOperationException($"Cannot return as T1 as result is {Value?.GetType().Name ?? "null"}");

	public Error AsT2 => Value is Error t2
		? t2
		: throw new InvalidOperationException($"Cannot return as T2 as result is {Value?.GetType().Name ?? "null"}");

	public TResult Match<TResult>(Func<string, TResult> f0, Func<NotFound, TResult> f1, Func<Error, TResult> f2) => Value switch
	{
		string t0 => f0(t0),
		NotFound t1 => f1(t1),
		Error t2 => f2(t2),
		_ => throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}")
	};

	public void Switch(Action<string> f0, Action<NotFound> f1, Action<Error> f2)
	{
		switch (Value)
		{
			case string t0:
				f0(t0);
				return;
			case NotFound t1:
				f1(t1);
				return;
			case Error t2:
				f2(t2);
				return;
			default:
				throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}");
		}
	}

	public static ObjidResolution FromT0(string value) => new(value);
	public static ObjidResolution FromT1(NotFound value) => new(value);
	public static ObjidResolution FromT2(Error value) => new(value);
}
