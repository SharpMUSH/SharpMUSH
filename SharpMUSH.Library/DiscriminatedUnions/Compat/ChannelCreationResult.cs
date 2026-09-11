// Positional accessors (IsT0/AsT0/Match/Switch/FromT0) for call sites not yet rewritten to pattern
// matching. Generated scaffolding: it goes away once nothing calls it.

namespace SharpMUSH.Library.DiscriminatedUnions;

partial class ChannelCreationResult
{
	public bool IsT0 => Value is Success;
	public bool IsT1 => Value is ChannelNameTaken;
	public bool IsT2 => Value is Error<string>;

	public Success AsT0 => Value is Success t0
		? t0
		: throw new InvalidOperationException($"Cannot return as T0 as result is {Value?.GetType().Name ?? "null"}");

	public ChannelNameTaken AsT1 => Value is ChannelNameTaken t1
		? t1
		: throw new InvalidOperationException($"Cannot return as T1 as result is {Value?.GetType().Name ?? "null"}");

	public Error<string> AsT2 => Value is Error<string> t2
		? t2
		: throw new InvalidOperationException($"Cannot return as T2 as result is {Value?.GetType().Name ?? "null"}");

	public TResult Match<TResult>(Func<Success, TResult> f0, Func<ChannelNameTaken, TResult> f1, Func<Error<string>, TResult> f2) => Value switch
	{
		Success t0 => f0(t0),
		ChannelNameTaken t1 => f1(t1),
		Error<string> t2 => f2(t2),
		_ => throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}")
	};

	public void Switch(Action<Success> f0, Action<ChannelNameTaken> f1, Action<Error<string>> f2)
	{
		switch (Value)
		{
			case Success t0:
				f0(t0);
				return;
			case ChannelNameTaken t1:
				f1(t1);
				return;
			case Error<string> t2:
				f2(t2);
				return;
			default:
				throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}");
		}
	}

	public static ChannelCreationResult FromT0(Success value) => new(value);
	public static ChannelCreationResult FromT1(ChannelNameTaken value) => new(value);
	public static ChannelCreationResult FromT2(Error<string> value) => new(value);
}
