// Positional accessors (IsT0/AsT0/Match/Switch/FromT0) for call sites not yet rewritten to pattern
// matching. Generated scaffolding: it goes away once nothing calls it.

using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Implementation.Common;

partial union DigestResult
{
	public bool IsT0 => Value is string;
	public bool IsT1 => Value is None;

	public string AsT0 => Value is string t0
		? t0
		: throw new InvalidOperationException($"Cannot return as T0 as result is {Value?.GetType().Name ?? "null"}");

	public None AsT1 => Value is None t1
		? t1
		: throw new InvalidOperationException($"Cannot return as T1 as result is {Value?.GetType().Name ?? "null"}");

	public TResult Match<TResult>(Func<string, TResult> f0, Func<None, TResult> f1) => Value switch
	{
		string t0 => f0(t0),
		None t1 => f1(t1),
		_ => throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}")
	};

	public void Switch(Action<string> f0, Action<None> f1)
	{
		switch (Value)
		{
			case string t0:
				f0(t0);
				return;
			case None t1:
				f1(t1);
				return;
			default:
				throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}");
		}
	}

	public static DigestResult FromT0(string value) => new(value);
	public static DigestResult FromT1(None value) => new(value);
}
