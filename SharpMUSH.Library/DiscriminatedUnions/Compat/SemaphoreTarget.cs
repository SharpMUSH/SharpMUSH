// Positional accessors (IsT0/AsT0/Match/Switch/FromT0) for call sites not yet rewritten to pattern
// matching. Generated scaffolding: it goes away once nothing calls it.

using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

partial union SemaphoreTarget
{
	public bool IsT0 => Value is long;
	public bool IsT1 => Value is DBRef;
	public bool IsT2 => Value is DbRefAttribute;

	public long AsT0 => Value is long t0
		? t0
		: throw new InvalidOperationException($"Cannot return as T0 as result is {Value?.GetType().Name ?? "null"}");

	public DBRef AsT1 => Value is DBRef t1
		? t1
		: throw new InvalidOperationException($"Cannot return as T1 as result is {Value?.GetType().Name ?? "null"}");

	public DbRefAttribute AsT2 => Value is DbRefAttribute t2
		? t2
		: throw new InvalidOperationException($"Cannot return as T2 as result is {Value?.GetType().Name ?? "null"}");

	public TResult Match<TResult>(Func<long, TResult> f0, Func<DBRef, TResult> f1, Func<DbRefAttribute, TResult> f2) => Value switch
	{
		long t0 => f0(t0),
		DBRef t1 => f1(t1),
		DbRefAttribute t2 => f2(t2),
		_ => throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}")
	};

	public void Switch(Action<long> f0, Action<DBRef> f1, Action<DbRefAttribute> f2)
	{
		switch (Value)
		{
			case long t0:
				f0(t0);
				return;
			case DBRef t1:
				f1(t1);
				return;
			case DbRefAttribute t2:
				f2(t2);
				return;
			default:
				throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}");
		}
	}

	public static SemaphoreTarget FromT0(long value) => new(value);
	public static SemaphoreTarget FromT1(DBRef value) => new(value);
	public static SemaphoreTarget FromT2(DbRefAttribute value) => new(value);
}
