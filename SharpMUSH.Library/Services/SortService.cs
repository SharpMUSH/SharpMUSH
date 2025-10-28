using Mediator;
using MoreLinq;
using NaturalSort.Extension;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public class SortService(ILocateService locateService, IConnectionService connectionService, IMediator mediator)
	: ISortService
{
	private readonly NaturalSortComparer _naturalSortComparer =
		new(StringComparison.CurrentCultureIgnoreCase);

	/// <summary>
	/// Transforms string to Sort Data.
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

			_ when sortType.StartsWith("attr:") => new ISortService.SortInformation(
				ISortService.SortType.CasedAttributeContent, OrderByDirection.Ascending, sortType[5..]),
			_ when sortType.StartsWith("attri:") => new ISortService.SortInformation(
				ISortService.SortType.UncasedAttributeContent, OrderByDirection.Ascending, sortType[6..]),

			_ => new ISortService.SortInformation(ISortService.SortType.UncasedLexicographically, OrderByDirection.Ascending,
				null)
		};

	public ValueTask<IAsyncEnumerable<MString>> Sort(IEnumerable<MString> items,
		Func<MString, CancellationToken, ValueTask<string>> keySelector,
		IMUSHCodeParser parser, ISortService.SortInformation sortData)
		=> Sort(items.ToAsyncEnumerable(), keySelector, parser, sortData);

	public async ValueTask<IAsyncEnumerable<MString>> Sort(IAsyncEnumerable<MString> source,
		Func<MString, CancellationToken, ValueTask<string>> keySelector,
		IMUSHCodeParser parser, ISortService.SortInformation sortData)
	{
		await ValueTask.CompletedTask;
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);

		var (sortType, direction, _) = sortData;

		return sortType switch
		{
			ISortService.SortType.CasedLexicographically
				=> source
					.OrderByAwait((i, ct) => ValueTask.FromResult(i.ToPlainText()), StringComparer.CurrentCulture, direction),

			ISortService.SortType.UncasedLexicographically
				=> source
					.OrderByAwait((i, ct) => ValueTask.FromResult(i.ToPlainText()), StringComparer.CurrentCultureIgnoreCase,
						direction),

			ISortService.SortType.DbRef
				=> source
					.OrderByAwait((i, ct) => ValueTask.FromResult(i.ToPlainText()), StringComparer.CurrentCulture, direction),

			ISortService.SortType.IntegerSort
				=> source
					.Select(mString => (i: int.TryParse(mString.ToPlainText(), out var number) ? number : -1, mString))
					.OrderByAwait((val, ct) => ValueTask.FromResult(val.i), Comparer<int>.Default, direction)
					.Select(val => val.mString),

			ISortService.SortType.DecimalSort
				=> source
					.Select(mString => (i: decimal.TryParse(mString.ToPlainText(), out var number) ? number : -1, mString))
					.OrderByAwait((val, ct) => ValueTask.FromResult(val.i), Comparer<decimal>.Default, direction)
					.Select(val => val.mString),

			ISortService.SortType.NaturalSort
				=> source.OrderByAwait((i, ct) => ValueTask.FromResult(i.ToPlainText()), _naturalSortComparer, direction),

			ISortService.SortType.CasedName => source
				.OrderByAwait(async (key, ct)
					=> await (await locateService.Locate(parser, executor, executor, await keySelector(key, ct), LocateFlags.All))
					.Match<ValueTask<string>>(
						player => ValueTask.FromResult(player.Object.Name),
						room => ValueTask.FromResult(room.Object.Name),
						exit => ValueTask.FromResult(exit.Object.Name),
						thing => ValueTask.FromResult(thing.Object.Name),
						_ => keySelector(key, ct),
						_ => keySelector(key, ct)
					), StringComparer.Ordinal, direction),

			ISortService.SortType.UncasedName => source
				.OrderByAwait(async (key, ct)
						=> await (await locateService.Locate(parser, executor, executor, await keySelector(key, ct), LocateFlags.All))
						.Match<ValueTask<string>>(
							player => ValueTask.FromResult(player.Object.Name),
							room => ValueTask.FromResult(room.Object.Name),
							exit => ValueTask.FromResult(exit.Object.Name),
							thing => ValueTask.FromResult(thing.Object.Name),
							_ => keySelector(key, ct),
							_ => keySelector(key, ct)
						), StringComparer.OrdinalIgnoreCase,
					direction),

			ISortService.SortType.Conn => source
				.OrderByAwait(async (key, ct)
						=> (await locateService.Locate(parser, executor, executor, await keySelector(key, ct), LocateFlags.All))
						.Match(
							player => connectionService.Get(player.Object.DBRef).FirstOrDefault()?.Connected ??
							          TimeSpan.MaxValue,
							_ => TimeSpan.MaxValue,
							_ => TimeSpan.MaxValue,
							_ => TimeSpan.MaxValue,
							_ => TimeSpan.MaxValue,
							_ => TimeSpan.MaxValue
						),
					direction),

			ISortService.SortType.Idle => source
				.OrderByAwait(async (key, ct)
						=> (await locateService.Locate(parser, executor, executor, await keySelector(key, ct), LocateFlags.All))
						.Match(
							player => connectionService.Get(player.Object.DBRef).FirstOrDefault()?.Connected ??
							          TimeSpan.MaxValue,
							_ => TimeSpan.MaxValue,
							_ => TimeSpan.MaxValue,
							_ => TimeSpan.MaxValue,
							_ => TimeSpan.MaxValue,
							_ => TimeSpan.MaxValue
						),
					direction),

			ISortService.SortType.Owner => source
				.OrderByAwait(async (key, ct)
						=> (await locateService.Locate(parser, executor, executor, await keySelector(key, ct), LocateFlags.All))
						.Match(
							async player => (await player.Object.Owner.WithCancellation(CancellationToken.None)).Object.DBRef.Number,
							async room => (await room.Object.Owner.WithCancellation(CancellationToken.None)).Object.DBRef.Number,
							async exit => (await exit.Object.Owner.WithCancellation(CancellationToken.None)).Object.DBRef.Number,
							async thing => (await thing.Object.Owner.WithCancellation(CancellationToken.None)).Object.DBRef.Number,
							async _ => await ValueTask.FromResult(-1),
							async _ => await ValueTask.FromResult(-1)
						),
					direction),

			ISortService.SortType.Location => source
				.OrderByAwait(async (key, ct)
						=> (await locateService.Locate(parser, executor, executor, await keySelector(key, ct), LocateFlags.All))
						.Match(
							async player => (await player.Location.WithCancellation(CancellationToken.None)).Object().DBRef.Number,
							async room => await ValueTask.FromResult(room.Object.DBRef.Number),
							async exit => (await exit.Location.WithCancellation(CancellationToken.None)).Object().DBRef.Number,
							async thing => (await thing.Location.WithCancellation(CancellationToken.None)).Object().DBRef.Number,
							async _ => await ValueTask.FromResult(-1),
							async _ => await ValueTask.FromResult(-1)
						),
					direction),

			ISortService.SortType.CreatedTime => source
				.OrderByAwait(async (key, ct)
						=> (await locateService.Locate(parser, executor, executor, await keySelector(key, ct), LocateFlags.All))
						.Match(
							player => player.Object.CreationTime,
							room => room.Object.CreationTime,
							exit => exit.Object.CreationTime,
							thing => thing.Object.CreationTime,
							_ => -1,
							_ => -1
						),
					direction),

			ISortService.SortType.ModifiedTime => source
				.OrderByAwait(async (key, ct)
						=> (await locateService.Locate(parser, executor, executor, await keySelector(key, ct), LocateFlags.All))
						.Match(
							player => player.Object.ModifiedTime,
							room => room.Object.ModifiedTime,
							exit => exit.Object.ModifiedTime,
							thing => thing.Object.ModifiedTime,
							_ => -1,
							_ => -1
						),
					direction),

			ISortService.SortType.AttributeName => source
				.OrderByAwait(keySelector, StringComparer.OrdinalIgnoreCase.WithNaturalSort(), direction),

			ISortService.SortType.Invalid
				=> source.OrderByAwait((i, ct) => ValueTask.FromResult(i.ToPlainText()), StringComparer.CurrentCulture,
					direction),
			_ => source.OrderByAwait((i, ct) => ValueTask.FromResult(i.ToPlainText()), StringComparer.CurrentCulture,
				direction)
		};
	}
}