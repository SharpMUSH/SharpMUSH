// Positional accessors (IsT0/AsT0/Match/Switch/FromT0) for call sites not yet rewritten to pattern
// matching. Generated scaffolding: it goes away once nothing calls it.

using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

partial class ChannelOrError
{
	public bool IsT0 => Value is SharpChannel;
	public bool IsT1 => Value is Error<CallState>;

	public SharpChannel AsT0 => Value is SharpChannel t0
		? t0
		: throw new InvalidOperationException($"Cannot return as T0 as result is {Value?.GetType().Name ?? "null"}");

	public Error<CallState> AsT1 => Value is Error<CallState> t1
		? t1
		: throw new InvalidOperationException($"Cannot return as T1 as result is {Value?.GetType().Name ?? "null"}");

	public TResult Match<TResult>(Func<SharpChannel, TResult> f0, Func<Error<CallState>, TResult> f1) => Value switch
	{
		SharpChannel t0 => f0(t0),
		Error<CallState> t1 => f1(t1),
		_ => throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}")
	};

	public void Switch(Action<SharpChannel> f0, Action<Error<CallState>> f1)
	{
		switch (Value)
		{
			case SharpChannel t0:
				f0(t0);
				return;
			case Error<CallState> t1:
				f1(t1);
				return;
			default:
				throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}");
		}
	}

	public static ChannelOrError FromT0(SharpChannel value) => new(value);
	public static ChannelOrError FromT1(Error<CallState> value) => new(value);
}
