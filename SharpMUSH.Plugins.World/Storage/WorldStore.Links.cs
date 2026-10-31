using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Plugins.World.Common;

namespace SharpMUSH.Plugins.World.Storage;

/// <summary>
/// One way out of a place of a graph map, as <c>@world/link</c> made it.
/// </summary>
/// <param name="Names">Its names, as an exit's are given: <c>Old Road;road</c>. The first is shown; any of them walks it.</param>
/// <param name="X">The place it leads to.</param>
/// <param name="Edge">Open, a wall (kept, and refusing with its reason) or a lock.</param>
/// <param name="Reason">Why it refuses a mover; null when it does not say.</param>
/// <param name="Key">The key a locked way opens for, bound to objids as <c>@lock</c> stores one.</param>
public sealed record WorldLinkRecord(string Names, int X, int Y, int Z, WorldEdge Edge, string? Reason, string? Key)
{
	/// <summary>Its names, one by one.</summary>
	public IReadOnlyList<string> NameList => WorldLinks.Split(Names);

	/// <summary>The name a place knows it by: its first, in lower case, spaces as <c>_</c>.</summary>
	public string Way => WorldLinks.Key(NameList[0]);

	/// <summary>Whether <paramref name="name"/> is one of its names, keyed as its way is: any case, <c>_</c> for a space.</summary>
	public bool Answers(string name) =>
		NameList.Any(own => WorldLinks.Key(own) == WorldLinks.Key(name));
}

/// <summary>The names a graph map's ways are given by.</summary>
public static partial class WorldLinks
{
	/// <summary>The most characters one name of a way has.</summary>
	public const int MaxNameLength = 64;

	/// <summary>The most ways out of one place.</summary>
	public const int MaxWays = 64;

	[GeneratedRegex(@"\s+")]
	private static partial Regex Spaces();

	/// <summary>The names in <c>Old Road;road</c>, trimmed, the empty ones left out.</summary>
	public static IReadOnlyList<string> Split(string names) =>
		names.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

	/// <summary>A way by its first name: lower case, each run of spaces one <c>_</c>.</summary>
	public static string Key(string name) => Spaces().Replace(name.Trim().ToLowerInvariant(), "_");

	/// <summary>
	/// The names, joined as they are stored, when each is at most <see cref="MaxNameLength"/> characters and holds no
	/// <c>,</c>, <c>=</c>, <c>/</c> or <c>|</c>, and at least one is given; null otherwise.
	/// </summary>
	public static string? Normalize(string names)
	{
		var list = Split(names);
		if (list.Count == 0 || list.Any(name => name.Length > MaxNameLength || name.IndexOfAny([',', '=', '/', '|']) >= 0)) return null;
		return string.Join(";", list);
	}
}

/// <summary>What became of making a way.</summary>
public enum WorldLinking
{
	/// <summary>The way leads there now.</summary>
	Linked,

	/// <summary>The map is no stored map.</summary>
	NotAMap,

	/// <summary>A place it leads out of has as many ways as it may.</summary>
	TooMany,

	/// <summary>Another way out of a place it leads out of already goes by one of its names.</summary>
	NameTaken,
}

/// <summary>
/// The ways of graph maps:
/// <list type="table">
///   <item><term><c>world.link</c></term><description><see cref="WorldGrid.PlaceKey"/> of the place a way leads out of, then the way's name → <see cref="WorldLinkRecord"/> JSON.</description></item>
/// </list>
/// </summary>
public sealed partial class WorldStore
{
	private readonly TableDef _links;

	private static byte[] LinkKey(long map, int x, int y, int z, string way) =>
		Keys.Concat(WorldGrid.PlaceKey(map, z, x, y), Encoding.UTF8.GetBytes(way));

	/// <summary>The ways out of the place at (x, y, z), by name.</summary>
	public IReadOnlyList<WorldLinkRecord> Links(DBRef map, int x, int y, int z) => _store.Read(tx => LinksIn(tx, map.Number, x, y, z));

	private List<WorldLinkRecord> LinksIn(ITx tx, long map, int x, int y, int z) =>
		[.. tx.Range(_links, WorldGrid.PlaceKey(map, z, x, y))
			.Select(entry => JsonSerializer.Deserialize<WorldLinkRecord>(entry.Value, Json))
			.OfType<WorldLinkRecord>()];

	/// <summary>The way out of (x, y, z) that <paramref name="name"/> names, by its way or any of its names; null for none.</summary>
	public WorldLinkRecord? Link(DBRef map, int x, int y, int z, string name) => _store.Read(tx => LinkIn(tx, map.Number, x, y, z, name));

	private WorldLinkRecord? LinkIn(ITx tx, long map, int x, int y, int z, string name) =>
		tx.TryGet(_links, LinkKey(map, x, y, z, WorldLinks.Key(name)), out var bytes)
			? JsonSerializer.Deserialize<WorldLinkRecord>(bytes, Json)
			: LinksIn(tx, map, x, y, z).FirstOrDefault(link => link.Answers(name));

	/// <summary>Every way of <paramref name="map"/>, with the place it leads out of.</summary>
	public IReadOnlyList<(WorldPosition From, WorldLinkRecord Link)> AllLinks(DBRef map) => _store.Read(tx =>
		tx.Range(_links, Keys.Dbref(map.Number))
			.Select(entry => (Key: entry.Key, Link: JsonSerializer.Deserialize<WorldLinkRecord>(entry.Value, Json)))
			.Where(entry => entry.Link is not null)
			.Select(entry =>
			{
				var (level, x, y) = WorldGrid.ReadPlaceKey(entry.Key);
				return (new WorldPosition(x, y, level), entry.Link!);
			})
			.ToList());

	/// <summary>
	/// Makes the way <paramref name="names"/> out of <paramref name="from"/> to <paramref name="to"/>, and, given
	/// <paramref name="back"/>, the way back under those names. A way of the same name out of the same place is
	/// replaced, its lock and reason with it.
	/// </summary>
	public ValueTask<WorldLinking> LinkAsync(DBRef map, (int X, int Y, int Z) from, (int X, int Y, int Z) to, string names, string? back) =>
		_store.WriteAsync(tx =>
		{
			if (MapIn(tx, map) is null) return WorldLinking.NotAMap;
			var there = new WorldLinkRecord(names, to.X, to.Y, to.Z, WorldEdge.Open, null, null);
			var home = back is null ? null : new WorldLinkRecord(back, from.X, from.Y, from.Z, WorldEdge.Open, null, null);
			if (Full(from, there) || (home is not null && Full(to, home))) return WorldLinking.TooMany;
			if (Taken(from, there) || (home is not null && Taken(to, home))) return WorldLinking.NameTaken;
			PutLink(tx, map.Number, from, there);
			if (home is not null) PutLink(tx, map.Number, to, home);
			return WorldLinking.Linked;

			bool Full((int X, int Y, int Z) at, WorldLinkRecord link)
			{
				var ways = LinksIn(tx, map.Number, at.X, at.Y, at.Z);
				return ways.Count >= WorldLinks.MaxWays && ways.All(way => way.Way != link.Way);
			}

			// One name, one way out of a place: a way it would replace keeps none of its names.
			bool Taken((int X, int Y, int Z) at, WorldLinkRecord link) =>
				LinksIn(tx, map.Number, at.X, at.Y, at.Z).Any(way => way.Way != link.Way && link.NameList.Any(way.Answers));
		});

	private void PutLink(ITx tx, long map, (int X, int Y, int Z) at, WorldLinkRecord link) =>
		tx.Put(_links, LinkKey(map, at.X, at.Y, at.Z, link.Way), JsonSerializer.SerializeToUtf8Bytes(link, Json));

	/// <summary>
	/// Changes the way out of (x, y, z) that <paramref name="name"/> names with <paramref name="change"/>, which a null
	/// answer removes. False when there is no such way.
	/// </summary>
	public ValueTask<bool> ChangeLinkAsync(DBRef map, int x, int y, int z, string name, Func<WorldLinkRecord, WorldLinkRecord?> change) =>
		_store.WriteAsync(tx =>
		{
			if (MapIn(tx, map) is null || LinkIn(tx, map.Number, x, y, z, name) is not { } link) return false;
			var key = LinkKey(map.Number, x, y, z, link.Way);
			if (change(link) is { } changed) tx.Put(_links, key, JsonSerializer.SerializeToUtf8Bytes(changed, Json));
			else tx.Delete(_links, key);
			return true;
		});
}
