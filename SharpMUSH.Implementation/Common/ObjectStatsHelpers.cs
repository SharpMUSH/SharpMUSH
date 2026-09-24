using System.Runtime.InteropServices;
using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Common;

/// <summary>
/// The object census behind <c>@stats</c> (<c>do_stats</c>, <c>src/wiz.c:760</c>) and
/// <c>lstats()</c> (<c>fun_lstats</c>, <c>src/fundb.c:2376</c>) — in PennMUSH one is the other as a
/// function, so the count, the owner filter and the field order have to be the one thing.
/// </summary>
internal static class ObjectStatsHelpers
{
	/// <summary>
	/// SharpMUSH removes a destroyed object rather than keeping it as garbage, so the garbage column
	/// PennMUSH prints in the whole-database form is always this. The column stays because
	/// <c>help lstats()</c> documents six fields and softcode counts them.
	/// </summary>
	public const int Garbage = 0;

	/// <summary>
	/// The figures <c>do_stats</c> prints, in its order. <c>fun_lstats</c> prints the same ones and
	/// appends <see cref="Garbage"/> for the whole database only (<c>src/fundb.c:2400-2404</c>).
	/// </summary>
	public readonly record struct ObjectCounts(int Total, int Rooms, int Exits, int Things, int Players)
	{
		/// <summary>The one-player form: five fields, no garbage column.</summary>
		public string ForOnePlayer() => $"{Total} {Rooms} {Exits} {Things} {Players}";

		/// <summary>The whole-database form: the same five, then the garbage column.</summary>
		public string ForEveryone() => $"{Total} {Rooms} {Exits} {Things} {Players} {Garbage}";
	}

	/// <summary>
	/// One pass over the database — or over one owner's objects, when
	/// <paramref name="owner"/> is given rather than PennMUSH's <c>ANY_OWNER</c>.
	/// </summary>
	public static async ValueTask<ObjectCounts> CountAsync(IMediator mediator, DBRef? owner)
	{
		var countByType = new Dictionary<string, int>();
		var query = new GetFilteredObjectsQuery(new ObjectSearchFilter { Owner = owner });
		await foreach (var obj in mediator.CreateStream(query))
		{
			CollectionsMarshal.GetValueRefOrAddDefault(countByType, obj.Type, out _)++;
		}

		var rooms = countByType.GetValueOrDefault("ROOM");
		var exits = countByType.GetValueOrDefault("EXIT");
		var things = countByType.GetValueOrDefault("THING");
		var players = countByType.GetValueOrDefault("PLAYER");

		return new ObjectCounts(rooms + exits + things + players, rooms, exits, things, players);
	}

	/// <summary>
	/// PennMUSH's <c>Search_All</c> (<c>hdrs/dbdefs.h</c>): a wizard, or anyone carrying the Search
	/// power, may ask about objects that are not theirs.
	/// </summary>
	public static async ValueTask<bool> CanSearchAll(AnySharpObject executor)
		=> await executor.IsPriv() || await executor.HasPower("SEARCH");
}
