using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services;
using OneOf.Types;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IObjectStore"/>: object identity and structure. Ported from
/// <c>SurrealDatabase.Objects.cs</c>; see that file and the hydration helpers in
/// <c>SurrealDatabase.cs</c> for the semantics this mirrors.
/// </summary>
public partial class LightningDatabase
{
	#region Object CRUD

	public async ValueTask<DBRef> CreatePlayerAsync(string name, string password, DBRef location, DBRef home, int quota,
		string? salt = null, CancellationToken cancellationToken = default)
	{
		return await Store.WriteAsync(tx =>
		{
			var dbref = AllocateDbref(tx);
			var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			var hashedPassword = salt != null ? password : _passwordService.HashPassword($"#{dbref}:{now}", password);

			tx.Put(Tables.Obj, Keys.Dbref(dbref), Codec.Serialize(new ObjectRecord
			{
				Name = name,
				Type = DatabaseConstants.TypePlayer,
				Aliases = [],
				CreationTime = now,
				ModifiedTime = now,
				PasswordHash = hashedPassword,
				PasswordSalt = salt ?? "",
				Quota = quota,
				Warnings = null,
				Locks = new Dictionary<string, LockRecord>()
			}));
			tx.Put(Tables.ObjName, Keys.Lower(name), Keys.Dbref(dbref));

			// A player owns itself, same as the SurrealDB and the migration seed.
			SetSingleEdge(tx, Tables.Owner, dbref, dbref);
			SetSingleEdge(tx, Tables.Location, dbref, (long)location.Number);
			SetSingleEdge(tx, Tables.Home, dbref, (long)home.Number);

			return new DBRef((int)dbref, now);
		}, cancellationToken);
	}

	public async ValueTask<DBRef> CreateRoomAsync(string name, SharpPlayer creator, CancellationToken cancellationToken = default)
	{
		var ownerKey = (long)creator.Object.Key;

		return await Store.WriteAsync(tx =>
		{
			var dbref = AllocateDbref(tx);
			var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

			tx.Put(Tables.Obj, Keys.Dbref(dbref), Codec.Serialize(new ObjectRecord
			{
				Name = name,
				Type = DatabaseConstants.TypeRoom,
				Aliases = [],
				CreationTime = now,
				ModifiedTime = now,
				Quota = 0,
				Warnings = null,
				Locks = new Dictionary<string, LockRecord>()
			}));
			tx.Put(Tables.ObjName, Keys.Lower(name), Keys.Dbref(dbref));

			// Rooms carry no location/home edge of their own; LinkRoomAsync sets a drop-to later.
			SetSingleEdge(tx, Tables.Owner, dbref, ownerKey);

			return new DBRef((int)dbref, now);
		}, cancellationToken);
	}

	public async ValueTask<DBRef> CreateThingAsync(string name, AnySharpContainer location, SharpPlayer creator,
		AnySharpContainer home, CancellationToken cancellationToken = default)
	{
		var locKey = (long)location.Object().Key;
		var homeKey = (long)home.Object().Key;
		var ownerKey = (long)creator.Object.Key;

		return await Store.WriteAsync(tx =>
		{
			var dbref = AllocateDbref(tx);
			var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

			tx.Put(Tables.Obj, Keys.Dbref(dbref), Codec.Serialize(new ObjectRecord
			{
				Name = name,
				Type = DatabaseConstants.TypeThing,
				Aliases = [],
				CreationTime = now,
				ModifiedTime = now,
				Quota = 0,
				Warnings = null,
				Locks = new Dictionary<string, LockRecord>()
			}));
			tx.Put(Tables.ObjName, Keys.Lower(name), Keys.Dbref(dbref));

			SetSingleEdge(tx, Tables.Location, dbref, locKey);
			SetSingleEdge(tx, Tables.Home, dbref, homeKey);
			SetSingleEdge(tx, Tables.Owner, dbref, ownerKey);

			return new DBRef((int)dbref, now);
		}, cancellationToken);
	}

	public async ValueTask<DBRef> CreateExitAsync(string name, string[] aliases, AnySharpContainer location,
		SharpPlayer creator, CancellationToken cancellationToken = default)
	{
		var locKey = (long)location.Object().Key;
		var ownerKey = (long)creator.Object.Key;

		return await Store.WriteAsync(tx =>
		{
			var dbref = AllocateDbref(tx);
			var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

			tx.Put(Tables.Obj, Keys.Dbref(dbref), Codec.Serialize(new ObjectRecord
			{
				Name = name,
				Type = DatabaseConstants.TypeExit,
				Aliases = aliases,
				CreationTime = now,
				ModifiedTime = now,
				Quota = 0,
				Warnings = null,
				Locks = new Dictionary<string, LockRecord>()
			}));
			tx.Put(Tables.ObjName, Keys.Lower(name), Keys.Dbref(dbref));
			foreach (var alias in aliases)
			{
				tx.Put(Tables.ObjName, Keys.Lower(alias), Keys.Dbref(dbref));
			}

			// The exit's own location is its source room (at_location, same table Location() reads).
			// The room->exit direction is a second, dedicated edge (Tables.Exit) so "exits at this
			// room" is a cheap point lookup instead of a type-filtered scan of every reverse-location
			// entry — SurrealDB can afford that scan; a flat key-value store cannot.
			SetSingleEdge(tx, Tables.Location, dbref, locKey);
			PutEdge(tx, Tables.Exit, locKey, dbref);
			SetSingleEdge(tx, Tables.Owner, dbref, ownerKey);

			return new DBRef((int)dbref, now);
		}, cancellationToken);
	}

	#endregion

	#region Links, Locks, Player Operations

	public async ValueTask<bool> LinkExitAsync(SharpExit exit, AnySharpContainer location, CancellationToken cancellationToken = default)
	{
		var dbref = (long)exit.Object.Key;
		var destKey = (long)location.Object().Key;
		await Store.WriteAsync(tx => SetSingleEdge(tx, Tables.Home, dbref, destKey), cancellationToken);
		return true;
	}

	public async ValueTask<bool> UnlinkExitAsync(SharpExit exit, CancellationToken cancellationToken = default)
	{
		var dbref = (long)exit.Object.Key;
		return await Store.WriteAsync(tx =>
		{
			var existed = GetSingleEdge(tx, Tables.Home.Forward, dbref) is not null;
			SetSingleEdge(tx, Tables.Home, dbref, null);
			return existed;
		}, cancellationToken);
	}

	public async ValueTask<bool> LinkRoomAsync(SharpRoom room, AnyOptionalSharpContainer location, CancellationToken cancellationToken = default)
	{
		if (location.IsNone)
		{
			return await UnlinkRoomAsync(room, cancellationToken);
		}

		var dbref = (long)room.Object.Key;
		var destKey = (long)location.WithoutNone().Object().Key;
		await Store.WriteAsync(tx => SetSingleEdge(tx, Tables.Home, dbref, destKey), cancellationToken);
		return true;
	}

	public async ValueTask<bool> UnlinkRoomAsync(SharpRoom room, CancellationToken cancellationToken = default)
	{
		var dbref = (long)room.Object.Key;
		return await Store.WriteAsync(tx =>
		{
			var existed = GetSingleEdge(tx, Tables.Home.Forward, dbref) is not null;
			SetSingleEdge(tx, Tables.Home, dbref, null);
			return existed;
		}, cancellationToken);
	}

	public async ValueTask SetLockAsync(SharpObject target, string lockName, SharpLockData lockData, CancellationToken cancellationToken = default)
	{
		var dbref = (long)target.Key;
		await Store.WriteAsync(tx =>
		{
			var found = ReadObject(tx, dbref) ?? throw new InvalidOperationException($"Object #{dbref} not found");
			var locks = new Dictionary<string, LockRecord>(found.Record.Locks)
			{
				[lockName] = new LockRecord { LockString = lockData.LockString, Flags = lockData.Flags.ToString() }
			};
			tx.Put(Tables.Obj, Keys.Dbref(dbref), Codec.Serialize(found.Record with { Locks = locks }));
		}, cancellationToken);
	}

	public async ValueTask UnsetLockAsync(SharpObject target, string lockName, CancellationToken cancellationToken = default)
	{
		var dbref = (long)target.Key;
		await Store.WriteAsync(tx =>
		{
			var found = ReadObject(tx, dbref) ?? throw new InvalidOperationException($"Object #{dbref} not found");
			var locks = new Dictionary<string, LockRecord>(found.Record.Locks);
			locks.Remove(lockName);
			tx.Put(Tables.Obj, Keys.Dbref(dbref), Codec.Serialize(found.Record with { Locks = locks }));
		}, cancellationToken);
	}

	public async ValueTask SetPlayerPasswordAsync(SharpPlayer player, string password, string? salt = null, CancellationToken cancellationToken = default)
	{
		var dbref = (long)player.Object.Key;
		await Store.WriteAsync(tx =>
		{
			var found = ReadObject(tx, dbref) ?? throw new InvalidOperationException($"Object #{dbref} not found");
			tx.Put(Tables.Obj, Keys.Dbref(dbref), Codec.Serialize(found.Record with { PasswordHash = password, PasswordSalt = salt ?? "" }));
		}, cancellationToken);
	}

	public async ValueTask SetPlayerQuotaAsync(SharpPlayer player, int quota, CancellationToken cancellationToken = default)
	{
		var dbref = (long)player.Object.Key;
		await Store.WriteAsync(tx =>
		{
			var found = ReadObject(tx, dbref) ?? throw new InvalidOperationException($"Object #{dbref} not found");
			tx.Put(Tables.Obj, Keys.Dbref(dbref), Codec.Serialize(found.Record with { Quota = quota }));
		}, cancellationToken);
	}

	public ValueTask<int> GetOwnedObjectCountAsync(SharpPlayer player, CancellationToken cancellationToken = default)
	{
		var count = Store.Read(tx => tx.Dups(Tables.Owner.Reverse, Keys.Dbref(player.Object.Key)).Count());
		return ValueTask.FromResult(count);
	}

	public ValueTask<int> GetObjectCountAsync(CancellationToken cancellationToken = default)
		=> ValueTask.FromResult((int)Store.Count(Tables.Obj));

	#endregion

	#region Object Retrieval

	public ValueTask<AnyOptionalSharpObject> GetObjectNodeAsync(DBRef dbref, CancellationToken cancellationToken = default)
	{
		var result = Store.Read<AnyOptionalSharpObject>(tx =>
		{
			var found = ReadObject(tx, dbref.Number);
			if (found is null)
			{
				return new None();
			}

			if (dbref.CreationMilliseconds is not null && found.Value.Record.CreationTime != dbref.CreationMilliseconds)
			{
				return new None();
			}

			return Hydrate(found.Value.Dbref, found.Value.Record).WithNoneOption();
		});
		return ValueTask.FromResult(result);
	}

	public ValueTask<SharpObject?> GetBaseObjectNodeAsync(DBRef dbref, CancellationToken cancellationToken = default)
	{
		var result = Store.Read<SharpObject?>(tx =>
		{
			var found = ReadObject(tx, dbref.Number);
			if (found is null)
			{
				return null;
			}

			if (dbref.CreationMilliseconds is not null && found.Value.Record.CreationTime != dbref.CreationMilliseconds)
			{
				return null;
			}

			return MapToSharpObject(found.Value.Dbref, found.Value.Record);
		});
		return ValueTask.FromResult(result);
	}

	public IAsyncEnumerable<SharpPlayer> GetPlayerByNameOrAliasAsync(string name, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<SharpPlayer>(ct => GetPlayerByNameOrAliasCoreAsync(name, ct));

	private async IAsyncEnumerable<SharpPlayer> GetPlayerByNameOrAliasCoreAsync(string name, [EnumeratorCancellation] CancellationToken ct)
	{
		var players = Store.Read(tx => tx.Dups(Tables.ObjName, Keys.Lower(name))
			.Select(v => Keys.ReadDbref(v))
			.Select(dbref => ReadObject(tx, dbref))
			.Where(found => found is not null && found.Value.Record.Type == DatabaseConstants.TypePlayer)
			.Select(found => Hydrate(found!.Value.Dbref, found.Value.Record).AsPlayer)
			.ToList());

		foreach (var player in players)
		{
			ct.ThrowIfCancellationRequested();
			yield return player;
		}
	}

	public IAsyncEnumerable<SharpObject> GetAllObjectsAsync(CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<SharpObject>(ct => GetAllObjectsCoreAsync(ct));

	private async IAsyncEnumerable<SharpObject> GetAllObjectsCoreAsync([EnumeratorCancellation] CancellationToken ct)
	{
		await foreach (var (key, value) in Store.RangeAsync(Tables.Obj, [], ct: ct))
		{
			yield return MapToSharpObject(Keys.ReadDbref(key), Codec.Deserialize<ObjectRecord>(value));
		}
	}

	public IAsyncEnumerable<AnySharpObject> GetAllTypedObjectsAsync(CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<AnySharpObject>(ct => GetAllTypedObjectsCoreAsync(ct));

	private async IAsyncEnumerable<AnySharpObject> GetAllTypedObjectsCoreAsync([EnumeratorCancellation] CancellationToken ct)
	{
		await foreach (var (key, value) in Store.RangeAsync(Tables.Obj, [], ct: ct))
		{
			var dbref = Keys.ReadDbref(key);
			var record = Codec.Deserialize<ObjectRecord>(value);
			yield return Hydrate(dbref, record);
		}
	}

	public IAsyncEnumerable<SharpObject> GetFilteredObjectsAsync(ObjectSearchFilter filter, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<SharpObject>(ct => GetFilteredObjectsCoreAsync(filter, ct));

	private async IAsyncEnumerable<SharpObject> GetFilteredObjectsCoreAsync(ObjectSearchFilter filter,
		[EnumeratorCancellation] CancellationToken ct)
	{
		var entries = filter.MinDbRef.HasValue
			? Store.RangeFromKeyAsync(Tables.Obj, Keys.Dbref(filter.MinDbRef.Value), ct: ct)
			: Store.RangeAsync(Tables.Obj, [], ct: ct);

		var skip = filter.Skip ?? 0;
		var skipped = 0;
		var yielded = 0;

		await foreach (var (key, value) in entries.WithCancellation(ct))
		{
			var dbref = Keys.ReadDbref(key);
			if (filter.MaxDbRef.HasValue && dbref > filter.MaxDbRef.Value)
			{
				yield break;
			}

			var record = Codec.Deserialize<ObjectRecord>(value);
			if (!Store.Read(tx => MatchesFilter(tx, dbref, record, filter)))
			{
				continue;
			}

			if (skipped < skip)
			{
				skipped++;
				continue;
			}

			if (filter.Limit.HasValue && yielded >= filter.Limit.Value)
			{
				yield break;
			}

			yielded++;
			yield return MapToSharpObject(dbref, record);
		}
	}

	/// <summary>
	/// Evaluates every populated predicate of <paramref name="filter"/> against one already-decoded row, inside
	/// <c>GetFilteredObjectsAsync</c> predicate-for-predicate; see that method and the doc comment on
	/// <c>IObjectStore.GetFilteredObjectsAsync</c> for why each predicate means what it means.
	/// </summary>
	private static bool MatchesFilter(ITx tx, long dbref, ObjectRecord record, ObjectSearchFilter filter)
	{
		if (filter.Types is { Length: > 0 } types && !types.Contains(record.Type))
		{
			return false;
		}

		if (!string.IsNullOrEmpty(filter.NamePattern))
		{
			var nameMatches = filter.UseRegex
				? Regex.IsMatch(record.Name, filter.NamePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
				: record.Name.Contains(filter.NamePattern, StringComparison.OrdinalIgnoreCase);
			if (!nameMatches)
			{
				return false;
			}
		}

		if (filter.Owner.HasValue && GetSingleEdge(tx, Tables.Owner.Forward, dbref) != filter.Owner.Value.Number)
		{
			return false;
		}

		if (filter.Zone.HasValue && GetSingleEdge(tx, Tables.Zone.Forward, dbref) != filter.Zone.Value.Number)
		{
			return false;
		}

		if (filter.Parent.HasValue && GetSingleEdge(tx, Tables.Parent.Forward, dbref) != filter.Parent.Value.Number)
		{
			return false;
		}

		if (!string.IsNullOrEmpty(filter.HasFlag))
		{
			var hasFlag = string.Equals(record.Type, filter.HasFlag, StringComparison.OrdinalIgnoreCase)
				|| ReadObjectFlags(tx, dbref).Any(flag =>
					string.Equals(flag.Name, filter.HasFlag, StringComparison.OrdinalIgnoreCase)
					|| (flag.Aliases?.Any(alias => string.Equals(alias, filter.HasFlag, StringComparison.OrdinalIgnoreCase)) ?? false));
			if (!hasFlag)
			{
				return false;
			}
		}

		if (!string.IsNullOrEmpty(filter.HasPower))
		{
			var hasPower = ReadObjectPowers(tx, dbref).Any(power =>
				string.Equals(power.Name, filter.HasPower, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(power.Alias, filter.HasPower, StringComparison.OrdinalIgnoreCase));
			if (!hasPower)
			{
				return false;
			}
		}

		return true;
	}

	public IAsyncEnumerable<SharpPlayer> GetAllPlayersAsync(CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<SharpPlayer>(ct => GetAllPlayersCoreAsync(ct));

	private async IAsyncEnumerable<SharpPlayer> GetAllPlayersCoreAsync([EnumeratorCancellation] CancellationToken ct)
	{
		await foreach (var (key, value) in Store.RangeAsync(Tables.Obj, [], ct: ct))
		{
			var record = Codec.Deserialize<ObjectRecord>(value);
			if (record.Type != DatabaseConstants.TypePlayer)
			{
				continue;
			}

			var dbref = Keys.ReadDbref(key);
			yield return Hydrate(dbref, record).AsPlayer;
		}
	}

	#endregion

	#region Object Properties

	public async ValueTask SetObjectName(AnySharpObject obj, MString value, CancellationToken cancellationToken = default)
	{
		var dbref = (long)obj.Object().Key;
		var plain = value.ToPlainText();
		await Store.WriteAsync(tx =>
		{
			var found = ReadObject(tx, dbref) ?? throw new InvalidOperationException($"Object #{dbref} not found");
			tx.Delete(Tables.ObjName, Keys.Lower(found.Record.Name), Keys.Dbref(dbref));
			tx.Put(Tables.Obj, Keys.Dbref(dbref), Codec.Serialize(found.Record with { Name = plain }));
			tx.Put(Tables.ObjName, Keys.Lower(plain), Keys.Dbref(dbref));
		}, cancellationToken);
	}

	public async ValueTask SetContentHome(AnySharpContent obj, AnySharpContainer home, CancellationToken cancellationToken = default)
	{
		var dbref = (long)obj.Object().Key;
		var homeKey = (long)home.Object().Key;
		await Store.WriteAsync(tx => SetSingleEdge(tx, Tables.Home, dbref, homeKey), cancellationToken);
	}

	public async ValueTask SetContentLocation(AnySharpContent obj, AnySharpContainer location, CancellationToken cancellationToken = default)
	{
		var dbref = (long)obj.Object().Key;
		var locKey = (long)location.Object().Key;
		await Store.WriteAsync(tx => SetSingleEdge(tx, Tables.Location, dbref, locKey), cancellationToken);
	}

	public async ValueTask SetObjectParent(AnySharpObject obj, AnySharpObject? parent, CancellationToken cancellationToken = default)
	{
		var dbref = (long)obj.Object().Key;
		long? parentKey = parent is null ? null : (long)parent.Object().Key;
		await Store.WriteAsync(tx => SetSingleEdge(tx, Tables.Parent, dbref, parentKey), cancellationToken);
	}

	public ValueTask UnsetObjectParent(AnySharpObject obj, CancellationToken cancellationToken = default)
		=> SetObjectParent(obj, null, cancellationToken);

	public async ValueTask SetObjectZone(AnySharpObject obj, AnySharpObject? zone, CancellationToken cancellationToken = default)
	{
		var dbref = (long)obj.Object().Key;
		long? zoneKey = zone is null ? null : (long)zone.Object().Key;
		await Store.WriteAsync(tx => SetSingleEdge(tx, Tables.Zone, dbref, zoneKey), cancellationToken);
	}

	public ValueTask UnsetObjectZone(AnySharpObject obj, CancellationToken cancellationToken = default)
		=> SetObjectZone(obj, null, cancellationToken);

	public async ValueTask SetObjectOwner(AnySharpObject obj, SharpPlayer owner, CancellationToken cancellationToken = default)
	{
		var dbref = (long)obj.Object().Key;
		var ownerKey = (long)owner.Object.Key;
		await Store.WriteAsync(tx => SetSingleEdge(tx, Tables.Owner, dbref, ownerKey), cancellationToken);
	}

	public async ValueTask SetObjectWarnings(AnySharpObject obj, WarningType warnings, CancellationToken cancellationToken = default)
	{
		var dbref = (long)obj.Object().Key;
		await Store.WriteAsync(tx =>
		{
			var found = ReadObject(tx, dbref) ?? throw new InvalidOperationException($"Object #{dbref} not found");
			tx.Put(Tables.Obj, Keys.Dbref(dbref), Codec.Serialize(found.Record with { Warnings = ((uint)warnings).ToString() }));
		}, cancellationToken);
	}

	#endregion

	#region Object Deletion

	public async ValueTask<bool> DeleteObjectAsync(DBRef dbref, CancellationToken cancellationToken = default)
	{
		return await Store.WriteAsync(tx =>
		{
			var n = (long)dbref.Number;
			var key = Keys.Dbref(n);

			var found = ReadObject(tx, n);
			if (found is null)
			{
				return false;
			}

			if (dbref.CreationMilliseconds is not null && found.Value.Record.CreationTime != dbref.CreationMilliseconds)
			{
				return false;
			}

			var record = found.Value.Record;

			// obj.name: the object's own name and every alias.
			tx.Delete(Tables.ObjName, Keys.Lower(record.Name), key);
			foreach (var alias in record.Aliases)
			{
				tx.Delete(Tables.ObjName, Keys.Lower(alias), key);
			}

			// Attributes: metadata and values live under the same dbref-prefixed key range.
			tx.DeletePrefix(Tables.AttrMeta, Keys.AttrPrefix(n));
			tx.DeletePrefix(Tables.AttrVal, Keys.AttrPrefix(n));

			// Expanded per-object data (dbref + 0x00 + type).
			tx.DeletePrefix(Tables.ExpandedObj, Keys.Composite(n, ""));

			// Mail received by this object dies with it (PennMUSH clear_player -> do_mail_purge): the
			// mail row, its sent-index entry (found directly via the row's own Sender field rather than
			// a full-table scan), and the box entry itself. Mail it sent to others survives with a
			// dangling sender (MapRecordToMail resolves that to None), so only that mail's now-meaningless
			// "sent by n" index rows are dropped, not the mail row or the recipient's box entry.
			foreach (var (mailBoxKey, _) in tx.Range(Tables.MailBox, key).ToList())
			{
				var mailId = Keys.ReadDbref(mailBoxKey.AsSpan(mailBoxKey.Length - 8, 8));
				var mailIdKey = Keys.Dbref(mailId);

				if (tx.TryGet(Tables.Mail, mailIdKey, out var mailBytes))
				{
					var mailRecord = Codec.Deserialize<MailRecord>(mailBytes);
					tx.Delete(Tables.MailSent, MailSentKey(mailRecord.Sender, mailId));
				}

				tx.Delete(Tables.Mail, mailIdKey);
				tx.Delete(Tables.MailBox, mailBoxKey);
			}

			tx.DeletePrefix(Tables.MailSent, key);

			// Channel membership.
			foreach (var (_, chanNameBytes) in tx.Range(Tables.RevChanMember, key).ToList())
			{
				var chanName = Keys.ReadStr(chanNameBytes);
				tx.Delete(Tables.ChanMember, Keys.Composite(chanName, n));
				tx.Delete(Tables.RevChanMember, key, chanNameBytes);
			}

			// Every edge table, both directions: the object's own outbound edges (and their paired
			// inbound counterpart on the other side), plus every inbound edge some other object still
			// holds toward it (and that edge's paired outbound counterpart) — the graph-shaped
			// equivalent of PennMUSH's free_object() db_top sweep that nulls out dangling
			// Zone/Parent/Home/Location references.
			foreach (var (forward, reverse) in Tables.EdgePairs)
			{
				foreach (var to in tx.Dups(forward, key).ToList())
				{
					tx.Delete(reverse, to, key);
				}
				tx.DeletePrefix(forward, key);

				foreach (var from in tx.Dups(reverse, key).ToList())
				{
					tx.Delete(forward, from, key);
				}
				tx.DeletePrefix(reverse, key);
			}

			tx.Delete(Tables.Obj, key);

			_logger.LogInformation("Deleted object #{DbRef} ({Name}) from the database", n, record.Name);
			return true;
		}, cancellationToken);
	}

	#endregion

	#region Reachability

	public ValueTask<bool> IsReachableViaParentOrZoneAsync(AnySharpObject startObject, AnySharpObject targetObject,
		int maxDepth = 100, CancellationToken cancellationToken = default)
	{
		var start = (long)startObject.Object().Key;
		var target = (long)targetObject.Object().Key;

		var result = Store.Read(tx =>
		{
			// maxDepth <= 0 leaves no traversal budget, not even the zero-hop start == target
			// check below — matching the SurrealDB walk this ports, which never enters its
			// depth-guarded loop in that case either.
			if (maxDepth <= 0)
			{
				return false;
			}

			// reference traversal (`uniqueVertices: 'global', order: 'bfs'` over has_parent and
			// has_zone). A single-path parent-precedence walk can dead-end down the parent chain
			// and miss a target that's only reachable by branching through a zone somewhere
			// along the way.
			var visited = new HashSet<long> { start };
			var queue = new Queue<(long Key, int Depth)>();
			queue.Enqueue((start, 0));

			while (queue.Count > 0)
			{
				var (current, depth) = queue.Dequeue();

				if (current == target)
				{
					return true;
				}

				if (depth >= maxDepth)
				{
					continue;
				}

				var parentKey = GetSingleEdge(tx, Tables.Parent.Forward, current);
				var zoneKey = GetSingleEdge(tx, Tables.Zone.Forward, current);

				if (parentKey is long p && visited.Add(p))
				{
					queue.Enqueue((p, depth + 1));
				}

				if (zoneKey is long z && visited.Add(z))
				{
					queue.Enqueue((z, depth + 1));
				}
			}

			return false;
		});

		return ValueTask.FromResult(result);
	}

	#endregion

	#region Hydration

	/// <summary>Point read of an object's row. Returns <see langword="null"/> when no such dbref exists.</summary>
	internal (long Dbref, ObjectRecord Record)? ReadObject(ITx tx, long dbref)
		=> tx.TryGet(Tables.Obj, Keys.Dbref(dbref), out var bytes) ? (dbref, Codec.Deserialize<ObjectRecord>(bytes)) : null;

	/// <summary>
	/// Builds the typed Library model for an object already read from <see cref="Tables.Obj"/>. Every
	/// relation to another object (owner, location, home, parent, zone, flags, powers, children) is a
	/// lazy loader that opens its own <see cref="LightningStore.Read{T}"/> when a caller actually asks
	/// for it. Nothing here reads the store, so this takes no transaction: a caller already holding one
	/// passes nothing, and a scan hydrating row by row needs to open none.
	/// </summary>
	internal AnySharpObject Hydrate(long dbref, ObjectRecord record)
	{
		var sharpObj = MapToSharpObject(dbref, record);

		return record.Type.ToUpperInvariant() switch
		{
			DatabaseConstants.TypePlayer => BuildPlayer(dbref, record, sharpObj),
			DatabaseConstants.TypeRoom => BuildRoom(dbref, sharpObj),
			DatabaseConstants.TypeThing => BuildThing(dbref, sharpObj),
			DatabaseConstants.TypeExit => BuildExit(dbref, record, sharpObj),
			_ => throw new ArgumentException($"Invalid Object Type: '{record.Type}'")
		};
	}

	private SharpObject MapToSharpObject(long dbref, ObjectRecord record)
	{
		var type = record.Type;

		return new SharpObject
		{
			Id = dbref.ToString(),
			Key = (int)dbref,
			Name = record.Name,
			Type = type,
			CreationTime = record.CreationTime,
			ModifiedTime = record.ModifiedTime,
			Warnings = ParseWarnings(record.Warnings),
			Locks = MapLocks(record.Locks),
			Flags = FlagsOf(dbref, type),
			Powers = PowersOf(dbref),
			// Attributes: the object's own top level, and the whole tree, both streamed from the
			// dbref-prefixed attr.meta range (see LightningDatabase.Attributes.cs).
			Attributes = new(() => new FreshAsyncEnumerable<SharpAttribute>(ct => TopLevelAttributesCoreAsync(dbref, ct))),
			LazyAttributes = new(() => new FreshAsyncEnumerable<LazySharpAttribute>(ct => TopLevelLazyAttributesCoreAsync(dbref, ct))),
			AllAttributes = new(() => new FreshAsyncEnumerable<SharpAttribute>(ct => AllAttributesCoreAsync(dbref, ct))),
			LazyAllAttributes = new(() => new FreshAsyncEnumerable<LazySharpAttribute>(ct => AllLazyAttributesCoreAsync(dbref, ct))),
			Owner = new(ct => OwnerRelation(dbref, ct)),
			Parent = new(ct => ParentRelation(dbref, ct)),
			Zone = new(ct => ZoneRelation(dbref, ct)),
			Children = new(() => new FreshAsyncEnumerable<SharpObject>(ct => GetChildrenCoreAsync(dbref, ct)))
		};
	}

	private SharpPlayer BuildPlayer(long dbref, ObjectRecord record, SharpObject sharpObj) => new()
	{
		Id = dbref.ToString(),
		Object = sharpObj,
		Aliases = record.Aliases,
		PasswordHash = record.PasswordHash ?? "",
		PasswordSalt = record.PasswordSalt,
		Quota = (int)record.Quota,
		Location = new(ct => LocationRelation(dbref, ct)),
		Home = new(ct => HomeRelation(dbref, ct))
	};

	private SharpRoom BuildRoom(long dbref, SharpObject sharpObj) => new()
	{
		Id = dbref.ToString(),
		Object = sharpObj,
		// A room's "Location" is its drop-to, which reuses the home edge exactly as SurrealDB's
		// DropToOf/GetDropToAsync does — there is no distinct drop-to table.
		Location = new(ct => DropToRelation(dbref, ct))
	};

	private SharpThing BuildThing(long dbref, SharpObject sharpObj) => new()
	{
		Id = dbref.ToString(),
		Object = sharpObj,
		Location = new(ct => LocationRelation(dbref, ct)),
		Home = new(ct => HomeRelation(dbref, ct))
	};

	private SharpExit BuildExit(long dbref, ObjectRecord record, SharpObject sharpObj) => new()
	{
		Id = dbref.ToString(),
		Object = sharpObj,
		Aliases = record.Aliases,
		// Source room: the at_location edge, same as a player/thing's Location.
		Location = new(ct => LocationRelation(dbref, ct)),
		// Destination: the has_home edge, absent on a freshly @open'd or @unlink'd exit — same edge
		// SurrealDB's ExitDestinationOf reuses rather than a dedicated "e.dest" table.
		Home = new(ct => ExitDestinationRelation(dbref, ct))
	};

	/// <summary>
	/// The container an object sits in, through <see cref="IObjectRelationLoader"/> when the host supplied
	/// one so the answer comes from the object cache rather than a fresh hydration, and straight off the
	/// location edge otherwise (a staging world, which has no host cache to answer from).
	/// </summary>
	private Task<AnySharpContainer> LocationRelation(long dbref, CancellationToken ct)
		=> _relations is { } r
			? r.LocationOf(dbref.ToString(), dbref.ToString(), ct)
			: Task.FromResult(GetRequiredContainerRelation(Tables.Location.Forward, dbref));

	/// <summary>Same routing as <see cref="LocationRelation"/>, for the home edge.</summary>
	private Task<AnySharpContainer> HomeRelation(long dbref, CancellationToken ct)
		=> _relations is { } r
			? r.HomeOf(dbref.ToString(), dbref.ToString(), (int)dbref, ct)
			: Task.FromResult(GetRequiredContainerRelation(Tables.Home.Forward, dbref));

	/// <summary>Same routing as <see cref="LocationRelation"/>, for the owner edge.</summary>
	private Task<SharpPlayer> OwnerRelation(long dbref, CancellationToken ct)
		=> _relations is { } r
			? r.OwnerOf(dbref.ToString(), (int)dbref, ct)
			: Task.FromResult(GetOwnerCore(dbref));

	/// <summary>Same routing as <see cref="LocationRelation"/>, for the parent edge.</summary>
	private Task<AnyOptionalSharpObject> ParentRelation(long dbref, CancellationToken ct)
		=> _relations is { } r
			? r.ParentOf(dbref.ToString(), (int)dbref, ct)
			: Task.FromResult(GetOptionalRelatedCore(Tables.Parent.Forward, dbref));

	/// <summary>Same routing as <see cref="LocationRelation"/>, for the zone edge.</summary>
	private Task<AnyOptionalSharpObject> ZoneRelation(long dbref, CancellationToken ct)
		=> _relations is { } r
			? r.ZoneOf(dbref.ToString(), (int)dbref, ct)
			: Task.FromResult(GetOptionalRelatedCore(Tables.Zone.Forward, dbref));

	/// <summary>Same routing as <see cref="LocationRelation"/>, for a room's drop-to (reuses the home edge).</summary>
	private Task<AnyOptionalSharpContainer> DropToRelation(long dbref, CancellationToken ct)
		=> _relations is { } r
			? r.DropToOf(dbref.ToString(), dbref.ToString(), (int)dbref, ct)
			: Task.FromResult(GetOptionalContainerRelation(Tables.Home.Forward, dbref));

	/// <summary>Same routing as <see cref="LocationRelation"/>, for an exit's destination (reuses the home edge).</summary>
	private Task<AnyOptionalSharpContainer> ExitDestinationRelation(long dbref, CancellationToken ct)
		=> _relations is { } r
			? r.ExitDestinationOf(dbref.ToString(), dbref.ToString(), (int)dbref, ct)
			: Task.FromResult(GetOptionalContainerRelation(Tables.Home.Forward, dbref));

	private SharpPlayer GetOwnerCore(long dbref) => Store.Read(tx =>
	{
		var ownerDbref = GetSingleEdge(tx, Tables.Owner.Forward, dbref)
			?? throw new InvalidOperationException($"No owner found for #{dbref}");
		var found = ReadObject(tx, ownerDbref)
			?? throw new InvalidOperationException($"No object record found for owner of #{dbref}");
		return Hydrate(found.Dbref, found.Record).AsPlayer;
	});

	private AnyOptionalSharpObject GetOptionalRelatedCore(TableDef forward, long dbref) => Store.Read<AnyOptionalSharpObject>(tx =>
	{
		var relatedDbref = GetSingleEdge(tx, forward, dbref);
		if (relatedDbref is null)
		{
			return new None();
		}

		var found = ReadObject(tx, relatedDbref.Value);
		return found is null
			? new None()
			: Hydrate(found.Value.Dbref, found.Value.Record).WithNoneOption();
	});

	private AnySharpContainer GetRequiredContainerRelation(TableDef forward, long dbref) => Store.Read(tx =>
	{
		var destDbref = GetSingleEdge(tx, forward, dbref)
			?? throw new InvalidOperationException($"No location found for #{dbref}");
		var found = ReadObject(tx, destDbref)
			?? throw new InvalidOperationException($"No object record found for #{destDbref}");
		return Hydrate(found.Dbref, found.Record).AsContainer;
	});

	private AnyOptionalSharpContainer GetOptionalContainerRelation(TableDef forward, long dbref) => Store.Read<AnyOptionalSharpContainer>(tx =>
	{
		var destDbref = GetSingleEdge(tx, forward, dbref);
		if (destDbref is null)
		{
			return new None();
		}

		var found = ReadObject(tx, destDbref.Value);
		return found is null
			? new None()
			: Hydrate(found.Value.Dbref, found.Value.Record).AsContainer.WithNoneOption();
	});

	private async IAsyncEnumerable<SharpObject> GetChildrenCoreAsync(long dbref, [EnumeratorCancellation] CancellationToken ct)
	{
		var children = Store.Read(tx => tx.Dups(Tables.Parent.Reverse, Keys.Dbref(dbref))
			.Select(v => Keys.ReadDbref(v))
			.Select(child => ReadObject(tx, child))
			.Where(found => found is not null)
			.Select(found => MapToSharpObject(found!.Value.Dbref, found.Value.Record))
			.ToList());

		foreach (var child in children)
		{
			ct.ThrowIfCancellationRequested();
			yield return child;
		}
	}

	private Lazy<IAsyncEnumerable<SharpObjectFlag>> FlagsOf(long dbref, string type) => new(()
		=> new FreshAsyncEnumerable<SharpObjectFlag>(ct => GetFlagsCoreAsync(dbref, type, ct)));

	private async IAsyncEnumerable<SharpObjectFlag> GetFlagsCoreAsync(long dbref, string type, [EnumeratorCancellation] CancellationToken ct)
	{
		var flags = Store.Read(tx => ReadObjectFlags(tx, dbref).ToList());

		foreach (var flag in flags)
		{
			ct.ThrowIfCancellationRequested();
			yield return flag;
		}

		yield return ObjectTypeFlag.For(type.ToUpperInvariant());
	}

	private Lazy<IAsyncEnumerable<SharpPower>> PowersOf(long dbref) => new(()
		=> new FreshAsyncEnumerable<SharpPower>(ct => GetPowersCoreAsync(dbref, ct)));

	private async IAsyncEnumerable<SharpPower> GetPowersCoreAsync(long dbref, [EnumeratorCancellation] CancellationToken ct)
	{
		var powers = Store.Read(tx => ReadObjectPowers(tx, dbref).ToList());

		foreach (var power in powers)
		{
			ct.ThrowIfCancellationRequested();
			yield return power;
		}
	}

	private static IImmutableDictionary<string, SharpLockData> MapLocks(Dictionary<string, LockRecord> locks)
	{
		var builder = ImmutableDictionary.CreateBuilder<string, SharpLockData>();
		foreach (var (name, lockRecord) in locks)
		{
			var flags = Enum.TryParse<LockService.LockFlags>(lockRecord.Flags, out var parsed)
				? parsed
				: LockService.LockFlags.Default;
			builder[name] = new SharpLockData(lockRecord.LockString, flags);
		}
		return builder.ToImmutable();
	}

	private static WarningType ParseWarnings(string? raw)
		=> raw is not null && uint.TryParse(raw, out var value) ? (WarningType)value : WarningType.None;

	#endregion
}
