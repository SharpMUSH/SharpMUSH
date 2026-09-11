// Positional accessors (IsT0/AsT0/Match/Switch/FromT0) for call sites not yet rewritten to pattern
// matching. Generated scaffolding: it goes away once nothing calls it.

using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Implementation.Commands.MailCommand;

partial class ErrorOrMailList
{
	public bool IsT0 => Value is Error<string>;
	public bool IsT1 => Value is IAsyncEnumerable<SharpMail>;

	public Error<string> AsT0 => Value is Error<string> t0
		? t0
		: throw new InvalidOperationException($"Cannot return as T0 as result is {Value?.GetType().Name ?? "null"}");

	public IAsyncEnumerable<SharpMail> AsT1 => Value is IAsyncEnumerable<SharpMail> t1
		? t1
		: throw new InvalidOperationException($"Cannot return as T1 as result is {Value?.GetType().Name ?? "null"}");

	public TResult Match<TResult>(Func<Error<string>, TResult> f0, Func<IAsyncEnumerable<SharpMail>, TResult> f1) => Value switch
	{
		Error<string> t0 => f0(t0),
		IAsyncEnumerable<SharpMail> t1 => f1(t1),
		_ => throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}")
	};

	public void Switch(Action<Error<string>> f0, Action<IAsyncEnumerable<SharpMail>> f1)
	{
		switch (Value)
		{
			case Error<string> t0:
				f0(t0);
				return;
			case IAsyncEnumerable<SharpMail> t1:
				f1(t1);
				return;
			default:
				throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}");
		}
	}

	public static ErrorOrMailList FromT0(Error<string> value) => new(value);
	public static ErrorOrMailList FromT1(IAsyncEnumerable<SharpMail> value) => new(value);
}
