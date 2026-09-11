// Positional accessors (IsT0/AsT0/Match/Switch/FromT0) for call sites not yet rewritten to pattern
// matching. Generated scaffolding: it goes away once nothing calls it.

using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.DiscriminatedUnions;

partial class AnySharpObjectOrErrorCallState
{
	public bool IsT0 => Value is AnySharpObject;
	public bool IsT1 => Value is Error<CallState>;

	public AnySharpObject AsT0 => Value is AnySharpObject t0
		? t0
		: throw new InvalidOperationException($"Cannot return as T0 as result is {Value?.GetType().Name ?? "null"}");

	public Error<CallState> AsT1 => Value is Error<CallState> t1
		? t1
		: throw new InvalidOperationException($"Cannot return as T1 as result is {Value?.GetType().Name ?? "null"}");

	public TResult Match<TResult>(Func<AnySharpObject, TResult> f0, Func<Error<CallState>, TResult> f1) => Value switch
	{
		AnySharpObject t0 => f0(t0),
		Error<CallState> t1 => f1(t1),
		_ => throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}")
	};

	public void Switch(Action<AnySharpObject> f0, Action<Error<CallState>> f1)
	{
		switch (Value)
		{
			case AnySharpObject t0:
				f0(t0);
				return;
			case Error<CallState> t1:
				f1(t1);
				return;
			default:
				throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}");
		}
	}

	public static AnySharpObjectOrErrorCallState FromT0(AnySharpObject value) => new(value);
	public static AnySharpObjectOrErrorCallState FromT1(Error<CallState> value) => new(value);
}
