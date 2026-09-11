// Positional accessors (IsT0/AsT0/Match/Switch/FromT0) for call sites not yet rewritten to pattern
// matching. Generated scaffolding: it goes away once nothing calls it.

using Microsoft.AspNetCore.Mvc;

namespace SharpMUSH.Server.Controllers;

partial union ValueOrResponse<T>
{
	public bool IsT0 => Value is T;
	public bool IsT1 => Value is ActionResult;

	public T AsT0 => Value is T t0
		? t0
		: throw new InvalidOperationException($"Cannot return as T0 as result is {Value?.GetType().Name ?? "null"}");

	public ActionResult AsT1 => Value is ActionResult t1
		? t1
		: throw new InvalidOperationException($"Cannot return as T1 as result is {Value?.GetType().Name ?? "null"}");

	public TResult Match<TResult>(Func<T, TResult> f0, Func<ActionResult, TResult> f1) => Value switch
	{
		T t0 => f0(t0),
		ActionResult t1 => f1(t1),
		_ => throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}")
	};

	public void Switch(Action<T> f0, Action<ActionResult> f1)
	{
		switch (Value)
		{
			case T t0:
				f0(t0);
				return;
			case ActionResult t1:
				f1(t1);
				return;
			default:
				throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}");
		}
	}

	public static ValueOrResponse<T> FromT0(T value) => new(value);
	public static ValueOrResponse<T> FromT1(ActionResult value) => new(value);
}
