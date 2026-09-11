// Positional accessors (IsT0/AsT0/Match/Switch/FromT0) for call sites not yet rewritten to pattern
// matching. Generated scaffolding: it goes away once nothing calls it.

namespace SharpMUSH.Library.DiscriminatedUnions;

partial union DbRefOptionalAttributeSplit
{
	public bool IsT0 => Value is ValueTuple<string, string?>;
	public bool IsT1 => Value is bool;

	public (string db, string? Attribute) AsT0 => Value is ValueTuple<string, string?> t0
		? t0
		: throw new InvalidOperationException($"Cannot return as T0 as result is {Value?.GetType().Name ?? "null"}");

	public bool AsT1 => Value is bool t1
		? t1
		: throw new InvalidOperationException($"Cannot return as T1 as result is {Value?.GetType().Name ?? "null"}");

	public TResult Match<TResult>(Func<(string db, string? Attribute), TResult> f0, Func<bool, TResult> f1) => Value switch
	{
		ValueTuple<string, string?> t0 => f0(t0),
		bool t1 => f1(t1),
		_ => throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}")
	};

	public void Switch(Action<(string db, string? Attribute)> f0, Action<bool> f1)
	{
		switch (Value)
		{
			case ValueTuple<string, string?> t0:
				f0(t0);
				return;
			case bool t1:
				f1(t1);
				return;
			default:
				throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}");
		}
	}

	public static DbRefOptionalAttributeSplit FromT0((string db, string? Attribute) value) => new(value);
	public static DbRefOptionalAttributeSplit FromT1(bool value) => new(value);
}
