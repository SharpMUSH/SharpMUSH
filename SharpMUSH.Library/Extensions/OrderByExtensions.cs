using DotNext.Collections.Generic;
using MoreLinq;
using NaturalSort.Extension;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Extensions;

public static class OrderByExtensions
{
	public static IOrderedAsyncEnumerable<TSource> OrderByAwait<TSource, TKey>(
		this IAsyncEnumerable<TSource> source,
		Func<TSource, ValueTask<TKey>> keySelector, OrderByDirection direction = OrderByDirection.Ascending)
	{
		return direction == OrderByDirection.Ascending
			? source.OrderByAwait(keySelector)
			: source.OrderByDescendingAwait(keySelector);
	}

	public static IOrderedAsyncEnumerable<TSource> OrderByAwait<TSource, TKey>(
		this IAsyncEnumerable<TSource> source,
		Func<TSource, ValueTask<TKey>> keySelector,
		IComparer<TKey> comparer, OrderByDirection direction = OrderByDirection.Ascending)
	{
		return direction == OrderByDirection.Ascending
			? source.OrderByAwait(keySelector, comparer)
			: source.OrderByDescendingAwait(keySelector, comparer);
	}

	/// <summary>
	/// Orders a collection of MarkupStrings (based on a MarkupString -> string keySelector) based on the Order Type passed.
	/// </summary>
	/// <param name="source">Source collection</param>
	/// <param name="keySelector">Selector that translates to non-ansi plaintext for comparison</param>
	/// <param name="parser">Parser</param>
	/// <param name="order">Order string</param>
	/// <code>
	///  a       Sorts lexicographically (Maybe case-sensitive).
	///  i       Sorts lexicographically (Always case-insensitive).
	///  d       Sorts dbrefs.
	///  n       Sorts integer numbers.
	///  f       Sorts decimal numbers.
	///  m       Sorts strings with embedded numbers and dbrefs (as names).
	///  name    Sorts dbrefs by their names. (Maybe case-sensitive)
	///  namei   Sorts dbrefs by their names. (Always case-insensitive)
	///  conn    Sorts dbrefs by their connection time.
	///  idle    Sorts dbrefs by their idle time.
	///  owner   Sorts dbrefs by their owner dbrefs.
	///  loc     Sorts dbrefs by their location dbref.
	///  ctime   Sorts dbrefs by their creation time.
	///  mtime   Sorts dbrefs by their modification time.
	///  lattr   Sorts attribute names.
	/// </code>
	/// <returns>An Ordered Enumerable</returns>
	public static async ValueTask<MString[]> OrderByAsync(this IEnumerable<MString> source,
		Func<MString, string> keySelector,
		IMUSHCodeParser parser, string order)
	{
		var descending = order.StartsWith('-');
		var workedOrder = descending ? order[1..] : order;
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		var direction = descending ? OrderByDirection.Descending : OrderByDirection.Ascending;

		// TODO: Special 'attr:' and 'attri:' types.
		return workedOrder switch
		{
			"a" => source.OrderBy(keySelector, StringComparer.Ordinal, direction).ToArray(),
			"i" => source.OrderBy(keySelector, StringComparer.OrdinalIgnoreCase, direction).ToArray(),
			"d" => await Collection.ToAsyncEnumerable(source).OrderByAwait(async key => (await parser.LocateService
					.Locate(parser, executor, executor, keySelector(key), LocateFlags.All))
				.Match(
					player => player.Object.DBRef.Number,
					room => room.Object.DBRef.Number,
					exit => exit.Object.DBRef.Number,
					thing => thing.Object.DBRef.Number,
					_ => -1,
					_ => -1
				), direction).ToArrayAsync(CancellationToken.None),
			"n" => source.OrderBy(key => int.TryParse(keySelector(key), out var value) ? value : -1, direction).ToArray(),
			"f" => source.OrderBy(key => decimal.TryParse(keySelector(key), out var value) ? value : -1, direction).ToArray(),
			"m" => source.OrderBy(keySelector, StringComparer.OrdinalIgnoreCase.WithNaturalSort(), direction).ToArray(),
			"name" => await Collection.ToAsyncEnumerable(source).OrderByAwait(async key 
				=> (await parser.LocateService.Locate(parser, executor, executor, keySelector(key), LocateFlags.All))
				.Match(
					player => player.Object.Name,
					room => room.Object.Name,
					exit => exit.Object.Name,
					thing => thing.Object.Name,
					_ => keySelector(key),
					_ => keySelector(key)
				), StringComparer.Ordinal, direction).ToArrayAsync(CancellationToken.None),
			"namei" => await Collection.ToAsyncEnumerable(source).OrderByAwait(async key 
					=> (await parser.LocateService.Locate(parser, executor, executor, keySelector(key), LocateFlags.All))
					.Match(
						player => player.Object.Name,
						room => room.Object.Name,
						exit => exit.Object.Name,
						thing => thing.Object.Name,
						_ => keySelector(key),
						_ => keySelector(key)
					), StringComparer.OrdinalIgnoreCase,
				direction).ToArrayAsync(CancellationToken.None),
			"conn" => await Collection.ToAsyncEnumerable(source).OrderByAwait(async key 
					=> (await parser.LocateService.Locate(parser, executor, executor, keySelector(key), LocateFlags.All))
					.Match(
						player => parser.ConnectionService.Get(player.Object.DBRef).FirstOrDefault()?.Connected ??
						          TimeSpan.MaxValue,
						_ => TimeSpan.MaxValue,
						_ => TimeSpan.MaxValue,
						_ => TimeSpan.MaxValue,
						_ => TimeSpan.MaxValue,
						_ => TimeSpan.MaxValue
					),
				direction).ToArrayAsync(CancellationToken.None),
			"idle" => await Collection.ToAsyncEnumerable(source).OrderByAwait(async key 
					=> (await parser.LocateService.Locate(parser, executor, executor, keySelector(key), LocateFlags.All))
					.Match(
						player => parser.ConnectionService.Get(player.Object.DBRef).FirstOrDefault()?.Connected ??
						          TimeSpan.MaxValue,
						_ => TimeSpan.MaxValue,
						_ => TimeSpan.MaxValue,
						_ => TimeSpan.MaxValue,
						_ => TimeSpan.MaxValue,
						_ => TimeSpan.MaxValue
					),
				direction).ToArrayAsync(CancellationToken.None),
			"owner" => await Collection.ToAsyncEnumerable(source).OrderByAwait(async key 
					=> (await parser.LocateService.Locate(parser, executor, executor, keySelector(key), LocateFlags.All))
					.Match(
						async player => (await player.Object.Owner.WithCancellation(CancellationToken.None)).Object.DBRef.Number,
						async room => (await room.Object.Owner.WithCancellation(CancellationToken.None)).Object.DBRef.Number,
						async exit => (await exit.Object.Owner.WithCancellation(CancellationToken.None)).Object.DBRef.Number,
						async thing => (await thing.Object.Owner.WithCancellation(CancellationToken.None)).Object.DBRef.Number,
						async _ => await ValueTask.FromResult(-1),
						async _ => await ValueTask.FromResult(-1)
					),
				direction).ToArrayAsync(CancellationToken.None),
			"loc" => await Collection.ToAsyncEnumerable(source).OrderByAwait(async key 
					=> (await parser.LocateService.Locate(parser, executor, executor, keySelector(key), LocateFlags.All))
					.Match(
						async player => (await player.Location.WithCancellation(CancellationToken.None)).Object().DBRef.Number,
						async room => await ValueTask.FromResult(room.Object.DBRef.Number),
						async exit => (await exit.Location.WithCancellation(CancellationToken.None)).Object().DBRef.Number,
						async thing => (await thing.Location.WithCancellation(CancellationToken.None)).Object().DBRef.Number,
						async _ => await ValueTask.FromResult(-1),
						async _ => await ValueTask.FromResult(-1)
					),
				direction).ToArrayAsync(CancellationToken.None),
			"ctime" => await Collection.ToAsyncEnumerable(source).OrderByAwait(async key 
					=> (await parser.LocateService.Locate(parser, executor, executor, keySelector(key), LocateFlags.All))
					.Match(
						player => player.Object.CreationTime,
						room => room.Object.CreationTime,
						exit => exit.Object.CreationTime,
						thing => thing.Object.CreationTime,
						_ => -1,
						_ => -1
					),
				direction).ToArrayAsync(CancellationToken.None),
			"mtime" => await Collection.ToAsyncEnumerable(source).OrderByAwait(async key 
					=> (await parser.LocateService.Locate(parser, executor, executor, keySelector(key), LocateFlags.All))
					.Match(
						player => player.Object.ModifiedTime,
						room => room.Object.ModifiedTime,
						exit => exit.Object.ModifiedTime,
						thing => thing.Object.ModifiedTime,
						_ => -1,
						_ => -1
					),
				direction).ToArrayAsync(CancellationToken.None),
			"lattr" => source.OrderBy(keySelector, StringComparer.OrdinalIgnoreCase.WithNaturalSort(),
				direction).ToArray(),
			_ => source.OrderBy(keySelector, StringComparer.OrdinalIgnoreCase,
				direction).ToArray()
		};
	}
}