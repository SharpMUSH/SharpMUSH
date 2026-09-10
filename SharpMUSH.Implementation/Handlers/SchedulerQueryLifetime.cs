using System.Runtime.CompilerServices;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Handlers;

internal static class SchedulerQueryLifetime
{
	// Capture synchronously: enumeration may begin after the creating scope ends.
	internal static IAsyncEnumerable<T> Read<T>(Func<IAsyncEnumerable<T>> read, CancellationToken requestToken)
		=> Enumerate(read, ExecutionBudget.Current, ExecutionBudget.CurrentToken, requestToken);

	private static async IAsyncEnumerable<T> Enumerate<T>(Func<IAsyncEnumerable<T>> read,
		ExecutionBudget? origin, CancellationToken originToken, CancellationToken requestToken,
		[EnumeratorCancellation] CancellationToken enumerationToken = default)
	{
		using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
			originToken, requestToken, enumerationToken, ExecutionBudget.CurrentToken);
		// Remaining uses the original monotonic deadline even after origin.Dispose().
		var remaining = origin?.Remaining ?? TimeSpan.MaxValue;
		var consumerRemaining = ExecutionBudget.Current?.Remaining ?? TimeSpan.MaxValue;
		if (consumerRemaining < remaining) remaining = consumerRemaining;
		using var budget = new ExecutionBudget(remaining == TimeSpan.MaxValue ? Timeout.InfiniteTimeSpan : remaining, cancellation.Token);
		IAsyncEnumerator<T>? rows = null;
		try
		{
			using (budget.Enter())
			{
				budget.ThrowIfExceeded();
				rows = read().GetAsyncEnumerator(budget.Token);
			}
			while (true)
			{
				bool hasRow;
				T row = default!;
				// Re-enter around every legacy read. Never carry an ambient scope
				// across yield, where it could replace the consumer's own budget.
				using (budget.Enter())
				{
					budget.ThrowIfExceeded();
					hasRow = await rows.MoveNextAsync();
					budget.ThrowIfExceeded();
					if (hasRow) row = rows.Current;
				}
				if (!hasRow) yield break;
				yield return row;
			}
		}
		finally
		{
			if (rows is not null)
			{
				using (budget.Enter()) await rows.DisposeAsync();
			}
		}
	}
}
