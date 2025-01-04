using MoreLinq;
using NaturalSort.Extension;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Implementation.Extensions;

public static class OrderByExtensions
{
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
	public static async ValueTask<IOrderedEnumerable<MString>> OrderByAsync(this IEnumerable<MString> source,
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
			"a" => source.OrderBy(keySelector, StringComparer.Ordinal, direction),
			"i" => source.OrderBy(keySelector, StringComparer.OrdinalIgnoreCase, direction),
			"d" => source.OrderBy(key => parser.LocateService
				.Locate(parser, executor, executor, keySelector(key), LocateFlags.All)
				.AsTask().GetAwaiter().GetResult()
				.Match(
					player => player.Object.DBRef.Number,
					room => room.Object.DBRef.Number,
					exit => exit.Object.DBRef.Number,
					thing => thing.Object.DBRef.Number,
					_ => -1,
					_ => -1
				), direction),
			"n" => source.OrderBy(key => int.TryParse(keySelector(key), out var value) ? value : -1, direction),
			"f" => source.OrderBy(key => decimal.TryParse(keySelector(key), out var value) ? value : -1, direction),
			"m" => source.OrderBy(keySelector, StringComparer.OrdinalIgnoreCase.WithNaturalSort(), direction),
			"name" => source.OrderBy(key => parser.LocateService
				.Locate(parser, executor, executor, keySelector(key), LocateFlags.All)
				.AsTask().GetAwaiter().GetResult()
				.Match(
					player => player.Object.Name,
					room => room.Object.Name,
					exit => exit.Object.Name,
					thing => thing.Object.Name,
					_ => keySelector(key),
					_ => keySelector(key)
				), StringComparer.Ordinal, direction),
			"namei" => source.OrderBy(key => parser.LocateService
					.Locate(parser, executor, executor, keySelector(key), LocateFlags.All)
					.AsTask().GetAwaiter().GetResult()
					.Match(
						player => player.Object.Name,
						room => room.Object.Name,
						exit => exit.Object.Name,
						thing => thing.Object.Name,
						_ => keySelector(key),
						_ => keySelector(key)
					), StringComparer.OrdinalIgnoreCase,
				direction),
			"conn" => source.OrderBy(key => parser.LocateService
					.Locate(parser, executor, executor, keySelector(key), LocateFlags.All)
					.AsTask().GetAwaiter().GetResult()
					.Match(
						player => parser.ConnectionService.Get(player.Object.DBRef).FirstOrDefault()?.Connected ??
						          TimeSpan.MaxValue,
						_ => TimeSpan.MaxValue,
						_ => TimeSpan.MaxValue,
						_ => TimeSpan.MaxValue,
						_ => TimeSpan.MaxValue,
						_ => TimeSpan.MaxValue
					),
				direction),
			"idle" => source.OrderBy(key => parser.LocateService
					.Locate(parser, executor, executor, keySelector(key), LocateFlags.All)
					.AsTask().GetAwaiter().GetResult()
					.Match(
						player => parser.ConnectionService.Get(player.Object.DBRef).FirstOrDefault()?.Connected ??
						          TimeSpan.MaxValue,
						_ => TimeSpan.MaxValue,
						_ => TimeSpan.MaxValue,
						_ => TimeSpan.MaxValue,
						_ => TimeSpan.MaxValue,
						_ => TimeSpan.MaxValue
					),
				direction),
			"owner" => source.OrderBy(key => parser.LocateService
					.Locate(parser, executor, executor, keySelector(key), LocateFlags.All)
					.AsTask().GetAwaiter().GetResult()
					.Match(
						player => player.Object.Owner.Value.Object.DBRef.Number,
						room => room.Object.Owner.Value.Object.DBRef.Number,
						exit => exit.Object.Owner.Value.Object.DBRef.Number,
						thing => thing.Object.Owner.Value.Object.DBRef.Number,
						_ => -1,
						_ => -1
					),
				direction),
			"loc" => source.OrderBy(key => parser.LocateService
					.Locate(parser, executor, executor, keySelector(key), LocateFlags.All)
					.AsTask().GetAwaiter().GetResult()
					.Match(
						player => player.Location.Value.Object().DBRef.Number,
						room => room.Object.DBRef.Number,
						exit => exit.Location.Value.Object().DBRef.Number,
						thing => thing.Location.Value.Object().DBRef.Number,
						_ => -1,
						_ => -1
					),
				direction),
			"ctime" => source.OrderBy(key => parser.LocateService
					.Locate(parser, executor, executor, keySelector(key), LocateFlags.All)
					.AsTask().GetAwaiter().GetResult()
					.Match(
						player => player.Object.CreationTime,
						room => room.Object.CreationTime,
						exit => exit.Object.CreationTime,
						thing => thing.Object.CreationTime,
						_ => -1,
						_ => -1
					),
				direction),
			"mtime" => source.OrderBy(key => parser.LocateService
					.Locate(parser, executor, executor, keySelector(key), LocateFlags.All)
					.AsTask().GetAwaiter().GetResult()
					.Match(
						player => player.Object.ModifiedTime,
						room => room.Object.ModifiedTime,
						exit => exit.Object.ModifiedTime,
						thing => thing.Object.ModifiedTime,
						_ => -1,
						_ => -1
					),
				direction),
			"lattr" => source.OrderBy(keySelector, StringComparer.OrdinalIgnoreCase.WithNaturalSort(),
				direction),
			_ => source.OrderBy(keySelector, StringComparer.OrdinalIgnoreCase,
				direction)
		};
	}
}