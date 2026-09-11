// Positional accessors (IsT0/AsT0/Match/Switch/FromT0) for call sites not yet rewritten to pattern
// matching. Generated scaffolding: it goes away once nothing calls it.

namespace SharpMUSH.Library.DiscriminatedUnions;

partial union ObjectAttributeSplit
{
	public bool IsT0 => Value is ValueTuple<string, string>;
	public bool IsT1 => Value is None;

	public (string db, string Attribute) AsT0 => Value is ValueTuple<string, string> t0
		? t0
		: throw new InvalidOperationException($"Cannot return as T0 as result is {Value?.GetType().Name ?? "null"}");

	public None AsT1 => Value is None t1
		? t1
		: throw new InvalidOperationException($"Cannot return as T1 as result is {Value?.GetType().Name ?? "null"}");

	public TResult Match<TResult>(Func<(string db, string Attribute), TResult> f0, Func<None, TResult> f1) => Value switch
	{
		ValueTuple<string, string> t0 => f0(t0),
		None t1 => f1(t1),
		_ => throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}")
	};

	public void Switch(Action<(string db, string Attribute)> f0, Action<None> f1)
	{
		switch (Value)
		{
			case ValueTuple<string, string> t0:
				f0(t0);
				return;
			case None t1:
				f1(t1);
				return;
			default:
				throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}");
		}
	}

	public static ObjectAttributeSplit FromT0((string db, string Attribute) value) => new(value);
	public static ObjectAttributeSplit FromT1(None value) => new(value);
}
