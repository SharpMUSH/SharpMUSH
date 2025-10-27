using Mediator;
using MoreLinq;
using NaturalSort.Extension;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public class SortService(ILocateService locate, IConnectionService connectionService, IMediator mediator) : ISortService
{
	private readonly NaturalSortComparer _naturalSortComparer =
		new(StringComparison.CurrentCultureIgnoreCase);

	/// <summary>
	/// Transforms string to a Sort Type.
	/// </summary>
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
	public ISortService.SortInformation StringToSortType(string sortType) =>
		sortType switch
		{
			"a" => new ISortService.SortInformation(ISortService.SortType.UncasedLexicographically,
				OrderByDirection.Ascending, null),
			"i" => new ISortService.SortInformation(ISortService.SortType.CasedLexicographically, OrderByDirection.Ascending,
				null),
			"d" => new ISortService.SortInformation(ISortService.SortType.DbRef, OrderByDirection.Ascending, null),
			"n" => new ISortService.SortInformation(ISortService.SortType.IntegerSort, OrderByDirection.Ascending, null),
			"f" => new ISortService.SortInformation(ISortService.SortType.DecimalSort, OrderByDirection.Ascending, null),
			"m" => new ISortService.SortInformation(ISortService.SortType.NaturalSort, OrderByDirection.Ascending, null),
			"name" => new ISortService.SortInformation(ISortService.SortType.CasedName, OrderByDirection.Ascending, null),
			"iname" => new ISortService.SortInformation(ISortService.SortType.UncasedName, OrderByDirection.Ascending, null),
			"conn" => new ISortService.SortInformation(ISortService.SortType.Conn, OrderByDirection.Ascending, null),
			"idle" => new ISortService.SortInformation(ISortService.SortType.Idle, OrderByDirection.Ascending, null),
			"owner" => new ISortService.SortInformation(ISortService.SortType.Owner, OrderByDirection.Ascending, null),
			"loc" => new ISortService.SortInformation(ISortService.SortType.Location, OrderByDirection.Ascending, null),
			"ctime" => new ISortService.SortInformation(ISortService.SortType.CreatedTime, OrderByDirection.Ascending, null),
			"mtime" => new ISortService.SortInformation(ISortService.SortType.ModifiedTime, OrderByDirection.Ascending, null),
			"lattr" => new ISortService.SortInformation(ISortService.SortType.AttributeName, OrderByDirection.Ascending,
				null),

			"-a" => new ISortService.SortInformation(ISortService.SortType.UncasedLexicographically,
				OrderByDirection.Descending, null),
			"-i" => new ISortService.SortInformation(ISortService.SortType.CasedLexicographically,
				OrderByDirection.Descending, null),
			"-d" => new ISortService.SortInformation(ISortService.SortType.DbRef, OrderByDirection.Descending, null),
			"-n" => new ISortService.SortInformation(ISortService.SortType.IntegerSort, OrderByDirection.Descending, null),
			"-f" => new ISortService.SortInformation(ISortService.SortType.DecimalSort, OrderByDirection.Descending, null),
			"-m" => new ISortService.SortInformation(ISortService.SortType.NaturalSort, OrderByDirection.Descending, null),
			"-name" => new ISortService.SortInformation(ISortService.SortType.CasedName, OrderByDirection.Descending, null),
			"-iname" => new ISortService.SortInformation(ISortService.SortType.UncasedName, OrderByDirection.Descending,
				null),
			"-conn" => new ISortService.SortInformation(ISortService.SortType.Conn, OrderByDirection.Descending, null),
			"-idle" => new ISortService.SortInformation(ISortService.SortType.Idle, OrderByDirection.Descending, null),
			"-owner" => new ISortService.SortInformation(ISortService.SortType.Owner, OrderByDirection.Descending, null),
			"-loc" => new ISortService.SortInformation(ISortService.SortType.Location, OrderByDirection.Descending, null),
			"-ctime" => new ISortService.SortInformation(ISortService.SortType.CreatedTime, OrderByDirection.Descending,
				null),
			"-mtime" => new ISortService.SortInformation(ISortService.SortType.ModifiedTime, OrderByDirection.Descending,
				null),
			"-lattr" => new ISortService.SortInformation(ISortService.SortType.AttributeName, OrderByDirection.Descending,
				null),

			_ => new ISortService.SortInformation(ISortService.SortType.UncasedLexicographically, OrderByDirection.Ascending,
				null)
		};

	public async ValueTask<IEnumerable<MString>> Sort(IEnumerable<MString> items, Func<MString, string> keySelector,
		IMUSHCodeParser parser, ISortService.SortInformation sortData)
	{
		await ValueTask.CompletedTask;
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);

		var (sortType, direction, _) = sortData;

		return sortType switch
		{
			ISortService.SortType.CasedLexicographically
				=> items.OrderBy(i => i.ToPlainText(), StringComparer.CurrentCulture, direction),
			ISortService.SortType.UncasedLexicographically
				=> items.OrderBy(i => i.ToPlainText(), StringComparer.CurrentCultureIgnoreCase, direction),
			ISortService.SortType.DbRef
				=> items.OrderBy(i => i.ToPlainText(), StringComparer.CurrentCulture, direction),
			ISortService.SortType.IntegerSort
				=> items
					.Select(mString => (i: int.TryParse(mString.ToPlainText(), out var number) ? number : -1, mString))
					.OrderBy(val => val.i, Comparer<int>.Default, direction)
					.Select(val => val.mString),
			ISortService.SortType.DecimalSort
				=> items
					.Select(mString => (i: decimal.TryParse(mString.ToPlainText(), out var number) ? number : -1, mString))
					.OrderBy(val => val.i, Comparer<decimal>.Default, direction)
					.Select(val => val.mString),
			ISortService.SortType.NaturalSort
				=> items.OrderBy(x => x.ToPlainText(), _naturalSortComparer, direction),
			ISortService.SortType.CasedName => throw new NotImplementedException(),
			ISortService.SortType.UncasedName => throw new NotImplementedException(),
			ISortService.SortType.Conn => await items
				.ToAsyncEnumerable()
				.OrderByAwait(async (key, _)
						=> (await locate.Locate(parser, executor, executor, keySelector(key), LocateFlags.All))
						.Match(
							player => connectionService.Get(player.Object.DBRef).FirstOrDefault()?.Connected ??
							          TimeSpan.MaxValue,
							_ => TimeSpan.MaxValue,
							_ => TimeSpan.MaxValue,
							_ => TimeSpan.MaxValue,
							_ => TimeSpan.MaxValue,
							_ => TimeSpan.MaxValue
						),
					direction)
				.ToArrayAsync(CancellationToken.None),
			ISortService.SortType.Idle => throw new NotImplementedException(),
			ISortService.SortType.Owner => throw new NotImplementedException(),
			ISortService.SortType.Location => throw new NotImplementedException(),
			ISortService.SortType.CreatedTime => throw new NotImplementedException(),
			ISortService.SortType.ModifiedTime => throw new NotImplementedException(),
			ISortService.SortType.AttributeName => throw new NotImplementedException(),
			ISortService.SortType.Invalid
				=> throw new ArgumentOutOfRangeException(nameof(sortType), sortType, null),
			_ => throw new ArgumentOutOfRangeException(nameof(sortType), sortType, null)
		};
	}

	/// <summary>
	/// Orders a collection of MarkupStrings (based on a MarkupString -> string keySelector) based on the Order Type passed.
	/// </summary>
	/// <param name="source">Source collection</param>
	/// <param name="keySelector">Selector that translates to non-ansi plaintext for comparison</param>
	/// <param name="parser">Parser</param>
	/// <param name="mediator">Mediator</param>
	/// <param name="locateService">Locate Service</param>
	/// <param name="connectionService">Connection Service</param>
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
	public static async ValueTask<MString[]> OrderByAsync(IEnumerable<MString> source,
		Func<MString, string> keySelector,
		IMUSHCodeParser parser,
		IMediator mediator,
		ILocateService locateService,
		IConnectionService connectionService,
		string order)
	{
		var descending = order.StartsWith('-');
		var workedOrder = descending ? order[1..] : order;
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var direction = descending ? OrderByDirection.Descending : OrderByDirection.Ascending;

		// TODO: Special 'attr:' and 'attri:' types.
		return workedOrder switch
		{
			"a" => source.OrderBy(keySelector, StringComparer.Ordinal, direction).ToArray(),
			"i" => source.OrderBy(keySelector, StringComparer.OrdinalIgnoreCase, direction).ToArray(),
			"d" => await source.ToAsyncEnumerable().OrderByAwait(async (key, _) => (await locateService
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
			"name" => await source.ToAsyncEnumerable().OrderByAwait(async (key, _)
				=> (await locateService.Locate(parser, executor, executor, keySelector(key), LocateFlags.All))
				.Match(
					player => player.Object.Name,
					room => room.Object.Name,
					exit => exit.Object.Name,
					thing => thing.Object.Name,
					_ => keySelector(key),
					_ => keySelector(key)
				), StringComparer.Ordinal, direction).ToArrayAsync(CancellationToken.None),
			"namei" => await source.ToAsyncEnumerable().OrderByAwait(async (key, _)
					=> (await locateService.Locate(parser, executor, executor, keySelector(key), LocateFlags.All))
					.Match(
						player => player.Object.Name,
						room => room.Object.Name,
						exit => exit.Object.Name,
						thing => thing.Object.Name,
						_ => keySelector(key),
						_ => keySelector(key)
					), StringComparer.OrdinalIgnoreCase,
				direction).ToArrayAsync(CancellationToken.None),
			"conn" => await source.ToAsyncEnumerable().OrderByAwait(async (key, _)
					=> (await locateService.Locate(parser, executor, executor, keySelector(key), LocateFlags.All))
					.Match(
						player => connectionService.Get(player.Object.DBRef).FirstOrDefault()?.Connected ??
						          TimeSpan.MaxValue,
						_ => TimeSpan.MaxValue,
						_ => TimeSpan.MaxValue,
						_ => TimeSpan.MaxValue,
						_ => TimeSpan.MaxValue,
						_ => TimeSpan.MaxValue
					),
				direction).ToArrayAsync(CancellationToken.None),
			"idle" => await source.ToAsyncEnumerable().OrderByAwait(async (key, _)
					=> (await locateService.Locate(parser, executor, executor, keySelector(key), LocateFlags.All))
					.Match(
						player => connectionService.Get(player.Object.DBRef).FirstOrDefault()?.Connected ??
						          TimeSpan.MaxValue,
						_ => TimeSpan.MaxValue,
						_ => TimeSpan.MaxValue,
						_ => TimeSpan.MaxValue,
						_ => TimeSpan.MaxValue,
						_ => TimeSpan.MaxValue
					),
				direction).ToArrayAsync(CancellationToken.None),
			"owner" => await source.ToAsyncEnumerable().OrderByAwait(async (key, _)
					=> (await locateService.Locate(parser, executor, executor, keySelector(key), LocateFlags.All))
					.Match(
						async player => (await player.Object.Owner.WithCancellation(CancellationToken.None)).Object.DBRef.Number,
						async room => (await room.Object.Owner.WithCancellation(CancellationToken.None)).Object.DBRef.Number,
						async exit => (await exit.Object.Owner.WithCancellation(CancellationToken.None)).Object.DBRef.Number,
						async thing => (await thing.Object.Owner.WithCancellation(CancellationToken.None)).Object.DBRef.Number,
						async _ => await ValueTask.FromResult(-1),
						async _ => await ValueTask.FromResult(-1)
					),
				direction).ToArrayAsync(CancellationToken.None),
			"loc" => await source.ToAsyncEnumerable().OrderByAwait(async (key, _)
					=> (await locateService.Locate(parser, executor, executor, keySelector(key), LocateFlags.All))
					.Match(
						async player => (await player.Location.WithCancellation(CancellationToken.None)).Object().DBRef.Number,
						async room => await ValueTask.FromResult(room.Object.DBRef.Number),
						async exit => (await exit.Location.WithCancellation(CancellationToken.None)).Object().DBRef.Number,
						async thing => (await thing.Location.WithCancellation(CancellationToken.None)).Object().DBRef.Number,
						async _ => await ValueTask.FromResult(-1),
						async _ => await ValueTask.FromResult(-1)
					),
				direction).ToArrayAsync(CancellationToken.None),
			"ctime" => await source.ToAsyncEnumerable().OrderByAwait(async (key, _)
					=> (await locateService.Locate(parser, executor, executor, keySelector(key), LocateFlags.All))
					.Match(
						player => player.Object.CreationTime,
						room => room.Object.CreationTime,
						exit => exit.Object.CreationTime,
						thing => thing.Object.CreationTime,
						_ => -1,
						_ => -1
					),
				direction).ToArrayAsync(CancellationToken.None),
			"mtime" => await source.ToAsyncEnumerable().OrderByAwait(async (key, _)
					=> (await locateService.Locate(parser, executor, executor, keySelector(key), LocateFlags.All))
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