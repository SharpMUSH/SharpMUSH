// Positional accessors (IsT0/AsT0/Match/Switch/FromT0) for call sites not yet rewritten to pattern
// matching. Generated scaffolding: it goes away once nothing calls it.

using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Commands.ChannelCommand;

partial union RecallSelection
{
	public bool IsT0 => Value is ChannelRecall.RecallWindow;
	public bool IsT1 => Value is CallState;

	public ChannelRecall.RecallWindow AsT0 => Value is ChannelRecall.RecallWindow t0
		? t0
		: throw new InvalidOperationException($"Cannot return as T0 as result is {Value?.GetType().Name ?? "null"}");

	public CallState AsT1 => Value is CallState t1
		? t1
		: throw new InvalidOperationException($"Cannot return as T1 as result is {Value?.GetType().Name ?? "null"}");

	public TResult Match<TResult>(Func<ChannelRecall.RecallWindow, TResult> f0, Func<CallState, TResult> f1) => Value switch
	{
		ChannelRecall.RecallWindow t0 => f0(t0),
		CallState t1 => f1(t1),
		_ => throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}")
	};

	public void Switch(Action<ChannelRecall.RecallWindow> f0, Action<CallState> f1)
	{
		switch (Value)
		{
			case ChannelRecall.RecallWindow t0:
				f0(t0);
				return;
			case CallState t1:
				f1(t1);
				return;
			default:
				throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}");
		}
	}

	public static RecallSelection FromT0(ChannelRecall.RecallWindow value) => new(value);
	public static RecallSelection FromT1(CallState value) => new(value);
}
