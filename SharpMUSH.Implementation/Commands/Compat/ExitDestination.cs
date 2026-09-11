// Positional accessors (IsT0/AsT0/Match/Switch/FromT0) for call sites not yet rewritten to pattern
// matching. Generated scaffolding: it goes away once nothing calls it.

using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Implementation.Commands;

partial class Commands
{
	partial union ExitDestination
	{
		public bool IsT0 => Value is AnySharpContainer;
		public bool IsT1 => Value is ExitDestinationFailure;

		public AnySharpContainer AsT0 => Value is AnySharpContainer t0
			? t0
			: throw new InvalidOperationException($"Cannot return as T0 as result is {Value?.GetType().Name ?? "null"}");

		public ExitDestinationFailure AsT1 => Value is ExitDestinationFailure t1
			? t1
			: throw new InvalidOperationException($"Cannot return as T1 as result is {Value?.GetType().Name ?? "null"}");

		public TResult Match<TResult>(Func<AnySharpContainer, TResult> f0, Func<ExitDestinationFailure, TResult> f1) => Value switch
		{
			AnySharpContainer t0 => f0(t0),
			ExitDestinationFailure t1 => f1(t1),
			_ => throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}")
		};

		public void Switch(Action<AnySharpContainer> f0, Action<ExitDestinationFailure> f1)
		{
			switch (Value)
			{
				case AnySharpContainer t0:
					f0(t0);
					return;
				case ExitDestinationFailure t1:
					f1(t1);
					return;
				default:
					throw new InvalidOperationException($"Unexpected case {Value?.GetType().Name ?? "null"}");
			}
		}

		public static ExitDestination FromT0(AnySharpContainer value) => new(value);
		public static ExitDestination FromT1(ExitDestinationFailure value) => new(value);
	}
}
