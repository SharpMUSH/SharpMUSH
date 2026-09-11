// Positional accessors (IsT0/AsT0/Match/Switch/FromT0) for call sites not yet rewritten to pattern
// matching. Generated scaffolding: it goes away once nothing calls it.

using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

partial union DbRefOrContainer
{
	public bool IsT0 => Value is DBRef;
	public bool IsT1 => Value is AnySharpContainer;

	public DBRef AsT0 => Value is DBRef t0
		? t0
		: throw new InvalidOperationException($"Cannot return as T0 as result is {Value?.GetType().Name ?? "null"}");

	public AnySharpContainer AsT1 => Value is AnySharpContainer t1
		? t1
		: throw new InvalidOperationException($"Cannot return as T1 as result is {Value?.GetType().Name ?? "null"}");

	public TResult Match<TResult>(Func<DBRef, TResult> f0, Func<AnySharpContainer, TResult> f1) => Value switch
	{
		DBRef t0 => f0(t0),
		AnySharpContainer t1 => f1(t1),
		_ => throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}")
	};

	public void Switch(Action<DBRef> f0, Action<AnySharpContainer> f1)
	{
		switch (Value)
		{
			case DBRef t0:
				f0(t0);
				return;
			case AnySharpContainer t1:
				f1(t1);
				return;
			default:
				throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}");
		}
	}

	public static DbRefOrContainer FromT0(DBRef value) => new(value);
	public static DbRefOrContainer FromT1(AnySharpContainer value) => new(value);
}
