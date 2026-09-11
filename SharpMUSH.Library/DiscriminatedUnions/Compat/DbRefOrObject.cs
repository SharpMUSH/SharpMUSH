// Positional accessors (IsT0/AsT0/Match/Switch/FromT0) for call sites not yet rewritten to pattern
// matching. Generated scaffolding: it goes away once nothing calls it.

using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

partial union DbRefOrObject
{
	public bool IsT0 => Value is DBRef;
	public bool IsT1 => Value is AnySharpObject;

	public DBRef AsT0 => Value is DBRef t0
		? t0
		: throw new InvalidOperationException($"Cannot return as T0 as result is {Value?.GetType().Name ?? "null"}");

	public AnySharpObject AsT1 => Value is AnySharpObject t1
		? t1
		: throw new InvalidOperationException($"Cannot return as T1 as result is {Value?.GetType().Name ?? "null"}");

	public TResult Match<TResult>(Func<DBRef, TResult> f0, Func<AnySharpObject, TResult> f1) => Value switch
	{
		DBRef t0 => f0(t0),
		AnySharpObject t1 => f1(t1),
		_ => throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}")
	};

	public void Switch(Action<DBRef> f0, Action<AnySharpObject> f1)
	{
		switch (Value)
		{
			case DBRef t0:
				f0(t0);
				return;
			case AnySharpObject t1:
				f1(t1);
				return;
			default:
				throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}");
		}
	}

	public static DbRefOrObject FromT0(DBRef value) => new(value);
	public static DbRefOrObject FromT1(AnySharpObject value) => new(value);
}
