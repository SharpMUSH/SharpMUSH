// Positional accessors (IsT0/AsT0/Match/Switch/FromT0) for call sites not yet rewritten to pattern
// matching. Generated scaffolding: it goes away once nothing calls it.

using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.DiscriminatedUnions;

partial union DeliveryResult
{
	public bool IsT0 => Value is AnySharpObject;
	public bool IsT1 => Value is DeliveryFailure;

	public AnySharpObject AsT0 => Value is AnySharpObject t0
		? t0
		: throw new InvalidOperationException($"Cannot return as T0 as result is {Value?.GetType().Name ?? "null"}");

	public DeliveryFailure AsT1 => Value is DeliveryFailure t1
		? t1
		: throw new InvalidOperationException($"Cannot return as T1 as result is {Value?.GetType().Name ?? "null"}");

	public TResult Match<TResult>(Func<AnySharpObject, TResult> f0, Func<DeliveryFailure, TResult> f1) => Value switch
	{
		AnySharpObject t0 => f0(t0),
		DeliveryFailure t1 => f1(t1),
		_ => throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}")
	};

	public void Switch(Action<AnySharpObject> f0, Action<DeliveryFailure> f1)
	{
		switch (Value)
		{
			case AnySharpObject t0:
				f0(t0);
				return;
			case DeliveryFailure t1:
				f1(t1);
				return;
			default:
				throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}");
		}
	}

	public static DeliveryResult FromT0(AnySharpObject value) => new(value);
	public static DeliveryResult FromT1(DeliveryFailure value) => new(value);
}
