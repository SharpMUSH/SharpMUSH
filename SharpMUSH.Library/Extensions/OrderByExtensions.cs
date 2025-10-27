using MoreLinq;

namespace SharpMUSH.Library.Extensions;

public static class OrderByExtensions
{
	public static IOrderedAsyncEnumerable<TSource> OrderByAwait<TSource, TKey>(
		this IAsyncEnumerable<TSource> source,
		Func<TSource, CancellationToken, ValueTask<TKey>> keySelector,
		OrderByDirection direction = OrderByDirection.Ascending)
	{
		return direction == OrderByDirection.Ascending
			? source.OrderBy(keySelector)
			: source.OrderByDescending(keySelector);
	}

	public static IOrderedAsyncEnumerable<TSource> OrderByAwait<TSource, TKey>(
		this IAsyncEnumerable<TSource> source,
		Func<TSource, CancellationToken, ValueTask<TKey>> keySelector,
		IComparer<TKey> comparer, OrderByDirection direction = OrderByDirection.Ascending)
	{
		return direction == OrderByDirection.Ascending
			? source.OrderBy(keySelector, comparer)
			: source.OrderByDescending(keySelector, comparer);
	}
}