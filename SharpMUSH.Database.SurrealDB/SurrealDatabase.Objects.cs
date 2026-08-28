using DotNext.Threading;
using MarkupString;
using Microsoft.Extensions.Logging;
using OneOf.Types;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SurrealDb.Net;
using SurrealDb.Net.Models.Response;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SharpMUSH.Database.SurrealDB;

public partial class SurrealDatabase
{
	#region Object CRUD

	public async ValueTask<DBRef> CreatePlayerAsync(string name, string password, DBRef location, DBRef home, int quota,
	string? salt = null, CancellationToken cancellationToken = default)
	{
		var nextKey = await GetNextObjectKeyAsync(cancellationToken);
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		var hashedPassword = salt != null
		? password
		: passwordService.HashPassword($"#{nextKey}:{now}", password);

		var parameters = new Dictionary<string, object?>
		{
			["key"] = nextKey,
			["name"] = name,
			["now"] = now,
			["hash"] = hashedPassword,
			["salt"] = salt ?? "",
			["quota"] = quota,
			["locKey"] = location.Number,
			["homeKey"] = home.Number
		};

		// One transaction so the object and its is_object/has_owner/at_location/has_home edges commit
		// together — otherwise the base record is visible before its owner/location/home edges, and a
		// concurrent reader resolving .Owner/.Location/.Home (all throw on a missing edge) crashes.
		await ExecuteAsync("""
			BEGIN TRANSACTION;
			CREATE object:$key SET key = $key, name = $name, type = 'PLAYER', creationTime = $now, modifiedTime = $now, locks = '{}', warnings = 0;
			CREATE player:$key SET key = $key, passwordHash = $hash, passwordSalt = $salt, aliases = [], quota = $quota;
			RELATE player:$key->is_object->object:$key;
			RELATE object:$key->has_owner->player:$key;
			RELATE player:$key->at_location->room:$locKey;
			RELATE player:$key->has_home->room:$homeKey;
			COMMIT TRANSACTION
			""", parameters, cancellationToken);

		return new DBRef(nextKey, now);
	}

	public async ValueTask<DBRef> CreateRoomAsync(string name, SharpPlayer creator, CancellationToken cancellationToken = default)
	{
		var nextKey = await GetNextObjectKeyAsync(cancellationToken);
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var creatorKey = creator.Object.Key;

		var parameters = new Dictionary<string, object?>
		{
			["key"] = nextKey,
			["name"] = name,
			["now"] = now,
			["ownerKey"] = creatorKey
		};

		// One transaction so the object and its is_object/has_owner edges commit together (a reader
		// resolving .Owner throws on a missing has_owner edge).
		await ExecuteAsync("""
			BEGIN TRANSACTION;
			CREATE object:$key SET key = $key, name = $name, type = 'ROOM', creationTime = $now, modifiedTime = $now, locks = '{}', warnings = 0;
			CREATE room:$key SET key = $key, aliases = [];
			RELATE room:$key->is_object->object:$key;
			RELATE object:$key->has_owner->player:$ownerKey;
			COMMIT TRANSACTION
			""", parameters, cancellationToken);

		return new DBRef(nextKey, now);
	}

	public async ValueTask<DBRef> CreateThingAsync(string name, AnySharpContainer location, SharpPlayer creator,
	AnySharpContainer home, CancellationToken cancellationToken = default)
	{
		var nextKey = await GetNextObjectKeyAsync(cancellationToken);
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var creatorKey = creator.Object.Key;
		var locKey = ExtractKey(location.Id);
		var homeKey = ExtractKey(home.Id);

		var locTable = GetContainerTable(location);
		var homeTable = GetContainerTable(home);

		var parameters = new Dictionary<string, object?>
		{
			["key"] = nextKey,
			["name"] = name,
			["now"] = now,
			["ownerKey"] = creatorKey,
			["locKey"] = locKey,
			["homeKey"] = homeKey,
			["emptyLocks"] = "{}"
		};

		// One transaction so the object and all its edges commit together — the has_owner edge is the
		// last statement, so without this the object is visible and owner-less across the whole create.
		await ExecuteAsync(
			$"BEGIN TRANSACTION;" +
			$"CREATE object:$key SET key = $key, name = $name, type = 'THING', creationTime = $now, modifiedTime = $now, locks = $emptyLocks, warnings = 0;" +
			$"CREATE thing:$key SET key = $key, aliases = [];" +
			$"RELATE thing:$key->is_object->object:$key;" +
			$"RELATE thing:$key->at_location->{locTable}:$locKey;" +
			$"RELATE thing:$key->has_home->{homeTable}:$homeKey;" +
			$"RELATE object:$key->has_owner->player:$ownerKey;" +
			$"COMMIT TRANSACTION",
			parameters, cancellationToken);

		return new DBRef(nextKey, now);
	}

	public async ValueTask<DBRef> CreateExitAsync(string name, string[] aliases, AnySharpContainer location,
	SharpPlayer creator, CancellationToken cancellationToken = default)
	{
		var nextKey = await GetNextObjectKeyAsync(cancellationToken);
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var creatorKey = creator.Object.Key;
		var locKey = ExtractKey(location.Id);

		var locTable = GetContainerTable(location);

		var parameters = new Dictionary<string, object?>
		{
			["key"] = nextKey,
			["name"] = name,
			["now"] = now,
			["aliases"] = aliases,
			["ownerKey"] = creatorKey,
			["locKey"] = locKey,
			["emptyLocks"] = "{}"
		};

		// One transaction so the object and all its edges commit together (has_owner is last).
		await ExecuteAsync(
			$"BEGIN TRANSACTION;" +
			$"CREATE object:$key SET key = $key, name = $name, type = 'EXIT', creationTime = $now, modifiedTime = $now, locks = $emptyLocks, warnings = 0;" +
			$"CREATE exit:$key SET key = $key, aliases = $aliases;" +
			$"RELATE exit:$key->is_object->object:$key;" +
			$"RELATE exit:$key->at_location->{locTable}:$locKey;" +
			$"RELATE object:$key->has_owner->player:$ownerKey;" +
			$"COMMIT TRANSACTION",
			parameters, cancellationToken);

		return new DBRef(nextKey, now);
	}

	#endregion

	#region Links, Locks, Player Operations

	public async ValueTask<bool> LinkExitAsync(SharpExit exit, AnySharpContainer location, CancellationToken cancellationToken = default)
	{
		var exitKey = ExtractKey(exit.Id!);
		var destKey = ExtractKey(location.Id);
		var destTable = GetContainerTable(location);

		var parameters = new Dictionary<string, object?>
		{
			["exitKey"] = exitKey,
			["destKey"] = destKey
		};

		// Relinking must replace the destination rather than accumulate a second has_home edge, and must
		// do so in one transaction like every other edge replacement here — otherwise a concurrent reader
		// can catch the exit detached, and a failure between the two statements strands it unlinked.
		await ExecuteAsync(
			$"BEGIN TRANSACTION;" +
			$"DELETE has_home WHERE in = exit:$exitKey;" +
			$"RELATE exit:$exitKey->has_home->{destTable}:$destKey;" +
			$"COMMIT TRANSACTION",
			parameters, cancellationToken);
		return true;
	}

	public async ValueTask<bool> UnlinkExitAsync(SharpExit exit, CancellationToken cancellationToken = default)
	{
		var exitKey = ExtractKey(exit.Id!);
		var parameters = new Dictionary<string, object?> { ["key"] = exitKey };

		var countResponse = await ExecuteAsync(
			"SELECT count() AS cnt FROM has_home WHERE in = exit:$key GROUP ALL",
			parameters, cancellationToken);
		var countResults = countResponse.GetValue<List<CountRecord>>(0)!;
		var existed = countResults.Count > 0 && countResults[0].cnt > 0;

		await ExecuteAsync("DELETE has_home WHERE in = exit:$key", parameters, cancellationToken);
		return existed;
	}

	public async ValueTask<bool> LinkRoomAsync(SharpRoom room, AnyOptionalSharpContainer location, CancellationToken cancellationToken = default)
	{
		if (location.IsNone) return await UnlinkRoomAsync(room, cancellationToken);

		await UnlinkRoomAsync(room, cancellationToken);

		var roomKey = ExtractKey(room.Id!);
		var destKey = location.Match(
		player => ExtractKey(player.Id!),
		rm => ExtractKey(rm.Id!),
		thing => ExtractKey(thing.Id!),
		_ => throw new InvalidOperationException());

		var destTable = location.Match(
		_ => "player",
		_ => "room",
		_ => "thing",
		_ => throw new InvalidOperationException());

		var parameters = new Dictionary<string, object?>
		{
			["roomKey"] = roomKey,
			["destKey"] = destKey
		};

		await ExecuteAsync(
			$"RELATE room:$roomKey->has_home->{destTable}:$destKey",
			parameters, cancellationToken);
		return true;
	}

	public async ValueTask<bool> UnlinkRoomAsync(SharpRoom room, CancellationToken cancellationToken = default)
	{
		var roomKey = ExtractKey(room.Id!);
		var parameters = new Dictionary<string, object?> { ["key"] = roomKey };

		var countResponse = await ExecuteAsync(
			"SELECT count() AS cnt FROM has_home WHERE in = room:$key GROUP ALL",
			parameters, cancellationToken);
		var countResults = countResponse.GetValue<List<CountRecord>>(0)!;
		var existed = countResults.Count > 0 && countResults[0].cnt > 0;

		await ExecuteAsync("DELETE has_home WHERE in = room:$key", parameters, cancellationToken);
		return existed;
	}

	public async ValueTask SetLockAsync(SharpObject target, string lockName, SharpLockData lockData, CancellationToken cancellationToken = default)
	{
		var newLocks = target.Locks
		.SetItem(lockName, lockData);
		var locksJson = SerializeLocks(newLocks);
		var parameters = new Dictionary<string, object?>
		{
			["key"] = target.Key,
			["locks"] = locksJson
		};
		await ExecuteAsync("UPDATE object:$key SET locks = $locks", parameters, cancellationToken);
	}

	public async ValueTask UnsetLockAsync(SharpObject target, string lockName, CancellationToken cancellationToken = default)
	{
		var newLocks = target.Locks.Remove(lockName);
		var locksJson = SerializeLocks(newLocks);
		var parameters = new Dictionary<string, object?>
		{
			["key"] = target.Key,
			["locks"] = locksJson
		};
		await ExecuteAsync("UPDATE object:$key SET locks = $locks", parameters, cancellationToken);
	}

	public async ValueTask SetPlayerPasswordAsync(SharpPlayer player, string password, string? salt = null, CancellationToken cancellationToken = default)
	{
		// Every caller passes an ALREADY-HASHED password (via PasswordService.HashPassword /
		// @password / @newpassword, or an imported PennMUSH hash when salt != null). Store it
		// verbatim; hashing here would double-hash and the character could never connect.
		var playerKey = ExtractKey(player.Id!);
		var parameters = new Dictionary<string, object?>
		{
			["key"] = playerKey,
			["hash"] = password,
			["salt"] = salt ?? ""
		};
		await ExecuteAsync("UPDATE player:$key SET passwordHash = $hash, passwordSalt = $salt", parameters, cancellationToken);
	}

	public async ValueTask SetPlayerQuotaAsync(SharpPlayer player, int quota, CancellationToken cancellationToken = default)
	{
		var playerKey = ExtractKey(player.Id!);
		var parameters = new Dictionary<string, object?>
		{
			["key"] = playerKey,
			["quota"] = quota
		};
		await ExecuteAsync("UPDATE player:$key SET quota = $quota", parameters, cancellationToken);
	}

	public async ValueTask<int> GetOwnedObjectCountAsync(SharpPlayer player, CancellationToken cancellationToken = default)
	{
		var playerKey = ExtractKey(player.Id!);
		var parameters = new Dictionary<string, object?> { ["key"] = playerKey };

		var response = await ExecuteAsync(
			"SELECT count() AS cnt FROM has_owner WHERE out = player:$key GROUP ALL",
			parameters, cancellationToken);

		var results = response.GetValue<List<CountRecord>>(0)!;
		if (results.Count == 0) return 0;
		return (int)results[0].cnt;
	}

	public async ValueTask<int> GetObjectCountAsync(CancellationToken cancellationToken = default)
	{
		var response = await ExecuteAsync("SELECT count() AS cnt FROM object GROUP ALL", cancellationToken);

		var results = response.GetValue<List<CountRecord>>(0)!;
		return results.Count == 0 ? 0 : (int)results[0].cnt;
	}

	#endregion

	#region Object Retrieval

	public async ValueTask<AnyOptionalSharpObject> GetObjectNodeAsync(DBRef dbref, CancellationToken cancellationToken = default)
	{
		var parameters = new Dictionary<string, object?> { ["key"] = dbref.Number };
		var response = await ExecuteAsync("SELECT * FROM object:$key", parameters, cancellationToken);

		var results = response.GetValue<List<ObjectRecord>>(0)!;
		if (results.Count == 0) return new None();

		var objRecord = results[0];
		if (dbref.CreationMilliseconds is not null && objRecord.creationTime != dbref.CreationMilliseconds)
			return new None();

		return await BuildTypedObjectFromObjectRecord(objRecord, cancellationToken);
	}

	public async ValueTask<SharpObject?> GetBaseObjectNodeAsync(DBRef dbref, CancellationToken cancellationToken = default)
	{
		var parameters = new Dictionary<string, object?> { ["key"] = dbref.Number };
		var response = await ExecuteAsync("SELECT * FROM object:$key", parameters, cancellationToken);

		var results = response.GetValue<List<ObjectRecord>>(0)!;
		if (results.Count == 0) return null;

		var objRecord = results[0];
		if (dbref.CreationMilliseconds.HasValue && objRecord.creationTime != dbref.CreationMilliseconds)
			return null;

		return MapRecordToSharpObject(objRecord);
	}

	public async IAsyncEnumerable<SharpPlayer> GetPlayerByNameOrAliasAsync(string name, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var parameters = new Dictionary<string, object?> { ["name"] = name };

		var objResponse = await ExecuteAsync(
			"SELECT * FROM object WHERE type = 'PLAYER' AND name = $name",
			parameters, cancellationToken);

		var objResults = objResponse.GetValue<List<ObjectRecord>>(0)!;

		var aliasResponse = await ExecuteAsync(
			"SELECT * FROM player WHERE $name IN aliases",
			parameters, cancellationToken);

		var aliasResults = aliasResponse.GetValue<List<PlayerRecord>>(0)!;

		var foundKeys = new HashSet<int>();
		foreach (var objRecord in objResults)
		{
			var key = objRecord.key;
			if (foundKeys.Add(key))
			{
				var sharpObj = MapRecordToSharpObject(objRecord);
				var playerParams = new Dictionary<string, object?> { ["key"] = key };
				var playerResponse = await ExecuteAsync("SELECT * FROM player:$key", playerParams, cancellationToken);
				var playerResults = playerResponse.GetValue<List<PlayerRecord>>(0)!;
				if (playerResults.Count > 0)
				{
					yield return BuildPlayer(PlayerId(key), playerResults[0], sharpObj);
				}
			}
		}

		foreach (var playerRecord in aliasResults)
		{
			var key = playerRecord.key;
			if (foundKeys.Add(key))
			{
				var objParams = new Dictionary<string, object?> { ["key"] = key };
				var objResp = await ExecuteAsync("SELECT * FROM object:$key", objParams, cancellationToken);
				var objRecs = objResp.GetValue<List<ObjectRecord>>(0)!;
				if (objRecs.Count > 0)
				{
					var sharpObj = MapRecordToSharpObject(objRecs[0]);
					yield return BuildPlayer(PlayerId(key), playerRecord, sharpObj);
				}
			}
		}
	}

	public async IAsyncEnumerable<SharpObject> GetAllObjectsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var response = await ExecuteAsync("SELECT * FROM object", cancellationToken);

		var results = response.GetValue<List<ObjectRecord>>(0)!;
		foreach (var record in results)
		{
			yield return MapRecordToSharpObject(record);
		}
	}

	public async IAsyncEnumerable<AnySharpObject> GetAllTypedObjectsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var objResponse = await ExecuteAsync("SELECT * FROM object", cancellationToken);
		var objResults = objResponse.GetValue<List<ObjectRecord>>(0)!;

		var playerResponse = await ExecuteAsync("SELECT * FROM player", cancellationToken);
		var playerResults = playerResponse.GetValue<List<PlayerRecord>>(0)!;
		var exitResponse = await ExecuteAsync("SELECT * FROM exit", cancellationToken);
		var exitResults = exitResponse.GetValue<List<ExitRecord>>(0)!;

		var playersByKey = playerResults.ToDictionary(p => p.key);
		var exitsByKey = exitResults.ToDictionary(e => e.key);

		foreach (var objRecord in objResults)
		{
			var key = objRecord.key;
			var type = objRecord.type;
			var sharpObj = MapRecordToSharpObject(objRecord);
			var typedId = GetTypedId(type, key);

			AnyOptionalSharpObject typed = type.ToUpper() switch
			{
				"PLAYER" => playersByKey.TryGetValue(key, out var pr) ? BuildPlayer(typedId, pr, sharpObj) : new None(),
				"ROOM" => BuildRoom(typedId, sharpObj),
				"THING" => BuildThing(typedId, sharpObj),
				"EXIT" => exitsByKey.TryGetValue(key, out var er) ? BuildExit(typedId, er, sharpObj) : new None(),
				_ => new None()
			};

			if (!typed.IsNone)
			{
				yield return typed.WithoutNone();
			}
		}
	}

	public async IAsyncEnumerable<SharpObject> GetFilteredObjectsAsync(ObjectSearchFilter filter, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var conditions = new List<string>();
		var parameters = new Dictionary<string, object?>();

		if (filter.Types is { Length: > 0 })
		{
			conditions.Add("type IN $types");
			parameters["types"] = filter.Types;
		}
		if (!string.IsNullOrEmpty(filter.NamePattern))
		{
			if (filter.UseRegex)
				conditions.Add("string::lowercase(name) ~ $namePattern");
			else
				conditions.Add("string::lowercase(name) CONTAINS string::lowercase($namePattern)");
			parameters["namePattern"] = filter.UseRegex ? ToFullMatchRegex(filter.NamePattern.ToLower()) : filter.NamePattern;
		}
		if (filter.MinDbRef.HasValue)
		{
			conditions.Add("key >= $minKey");
			parameters["minKey"] = filter.MinDbRef.Value;
		}
		if (filter.MaxDbRef.HasValue)
		{
			conditions.Add("key <= $maxKey");
			parameters["maxKey"] = filter.MaxDbRef.Value;
		}

		// Relationship and flag/power predicates. These were absent, and their absence was invisible:
		// with none of them contributing a condition, a caller asking for "objects owned by #7" got
		// `SELECT * FROM object` — the entire database, reported as a filtered result.
		//
		// Each relation lookup is LET-bound rather than inlined into the WHERE. SurrealDB re-evaluates
		// an inline `WHERE id IN (subquery)` once per row of the outer table, which makes the filter
		// quadratic in database size; LET evaluates it once. Measured on the embedded engine over
		// databases of 50 / 150 / 300 objects, the inline owner filter cost 23ms / 162ms / 599ms, and
		// on a CI runner that curve reached the driver's 30s query timeout — which the SurrealDB
		// embedded driver mishandles by completing an already-completed TaskCompletionSource from a
		// native callback, aborting the process. Bound, it is 3ms / 3ms / 5ms.
		//
		// Comparing `out` to a whole record id rather than reaching through it for `out.key` matters
		// too, since only the former can use the `out` indexes. See GetEntrancesAsync for the same
		// pair of fixes on the same shape.
		var bindings = new List<string>();

		if (filter.Owner.HasValue)
		{
			bindings.Add("LET $ownedIds = (SELECT VALUE in FROM has_owner WHERE out = player:$ownerKey)");
			conditions.Add("id IN $ownedIds");
			parameters["ownerKey"] = filter.Owner.Value.Number;
		}
		if (filter.Zone.HasValue)
		{
			bindings.Add("LET $zonedIds = (SELECT VALUE in FROM has_zone WHERE out = object:$zoneKey)");
			conditions.Add("id IN $zonedIds");
			parameters["zoneKey"] = filter.Zone.Value.Number;
		}
		if (filter.Parent.HasValue)
		{
			bindings.Add("LET $childIds = (SELECT VALUE in FROM has_parent WHERE out = object:$parentKey)");
			conditions.Add("id IN $childIds");
			parameters["parentKey"] = filter.Parent.Value.Number;
		}
		if (!string.IsNullOrEmpty(filter.HasFlag))
		{
			// Case-insensitive matching runs against the flag table (a hundred-odd rows, scanned once)
			// rather than through every has_flags edge. The type disjunct reproduces the type-named flag
			// GetObjectFlagsAsync synthesises, which no has_flags edge backs but HelperFunctions.HasFlag
			// still reports.
			bindings.Add("LET $matchingFlags = (SELECT VALUE id FROM object_flag "
				+ "WHERE string::lowercase(name) = $flagName)");
			bindings.Add("LET $flaggedIds = (SELECT VALUE in FROM has_flags WHERE out IN $matchingFlags)");
			conditions.Add("(string::lowercase(type) = $flagName OR id IN $flaggedIds)");
			parameters["flagName"] = filter.HasFlag.ToLowerInvariant();
		}
		if (!string.IsNullOrEmpty(filter.HasPower))
		{
			// Alias as well as name, mirroring HelperFunctions.HasPower.
			bindings.Add("LET $matchingPowers = (SELECT VALUE id FROM power "
				+ "WHERE string::lowercase(name) = $powerName OR string::lowercase(alias) = $powerName)");
			bindings.Add("LET $poweredIds = (SELECT VALUE in FROM has_powers WHERE out IN $matchingPowers)");
			conditions.Add("id IN $poweredIds");
			parameters["powerName"] = filter.HasPower.ToLowerInvariant();
		}

		var whereClause = conditions.Count > 0 ? $"WHERE {string.Join(" AND ", conditions)}" : "";

		var limitClause = "";
		if (filter.Skip.HasValue || filter.Limit.HasValue)
		{
			var skip = filter.Skip ?? 0;
			if (filter.Limit.HasValue)
				limitClause = $"START {skip} LIMIT {filter.Limit.Value}";
			else if (skip > 0)
				limitClause = $"START {skip}";
		}

		var prelude = bindings.Count > 0 ? string.Join(";", bindings) + ";" : "";
		var query = $"{prelude}SELECT * FROM object {whereClause} {limitClause}";
		var response = await ExecuteAsync(query, parameters, cancellationToken);

		// The SELECT is the last statement; each LET above occupies a response slot ahead of it.
		var results = response.GetValue<List<ObjectRecord>>(bindings.Count)!;
		foreach (var record in results)
		{
			yield return MapRecordToSharpObject(record);
		}
	}

	public async IAsyncEnumerable<SharpPlayer> GetAllPlayersAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var playerResponse = await ExecuteAsync("SELECT * FROM player", cancellationToken);
		var playerResults = playerResponse.GetValue<List<PlayerRecord>>(0)!;

		foreach (var playerRecord in playerResults)
		{
			var key = playerRecord.key;
			var objParams = new Dictionary<string, object?> { ["key"] = key };
			var objResponse = await ExecuteAsync("SELECT * FROM object:$key", objParams, cancellationToken);
			var objResults = objResponse.GetValue<List<ObjectRecord>>(0)!;
			if (objResults.Count > 0)
			{
				var sharpObj = MapRecordToSharpObject(objResults[0]);
				yield return BuildPlayer(PlayerId(key), playerRecord, sharpObj);
			}
		}
	}

	public async IAsyncEnumerable<AnySharpContent> GetHomedAtAsync(DBRef home,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		// has_home links content → its home. Rooms come back too (a room's drop-to reuses this edge)
		// and are dropped below, because a drop-to is not a home.
		//
		// Compares `out` to a whole record id rather than reaching through it for `out.key`, which
		// would dereference the linked record on every row of has_home. This one is a standalone
		// query, so it is linear either way rather than quadratic like the `WHERE ... IN (subquery)`
		// forms nearby — but object destruction calls it per object destroyed, and a room takes its
		// exits with it, so the constant matters.
		var homeNode = await GetObjectNodeAsync(home, cancellationToken);
		if (homeNode.IsNone) yield break;

		var homeTable = ExtractTable(homeNode.Known.Id()!);
		var response = await ExecuteAsync(
			$"SELECT VALUE in.key FROM has_home WHERE out = {homeTable}:$homeKey",
			new Dictionary<string, object?> { ["homeKey"] = home.Number }, cancellationToken);

		foreach (var key in response.GetValue<List<int>>(0) ?? [])
		{
			var candidate = await GetObjectNodeAsync(new DBRef(key), cancellationToken);
			if (candidate.IsNone || candidate.IsRoom) continue;

			yield return candidate.Known.AsContent;
		}
	}

	public async IAsyncEnumerable<SharpExit> GetEntrancesAsync(DBRef destination, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		// The destination is resolved first so the subquery can compare `out` to a whole record id, and
		// the subquery is LET-bound rather than inlined. Both matter, and the second matters more:
		// SurrealDB re-evaluates an inline `WHERE ... IN (subquery)` once per row of the outer table,
		// so this walked all of has_home for every exit in the database. Measured on the embedded
		// engine over databases of 50 / 150 / 300 objects, it cost 10ms / 71ms / 238ms while returning
		// nothing at all. Index-backed comparison alone only brought that to 179ms; the LET makes it
		// 2ms / 1ms / 2ms.
		var destinationNode = await GetObjectNodeAsync(destination, cancellationToken);
		if (destinationNode.IsNone) yield break;

		var destinationTable = ExtractTable(destinationNode.Known.Id()!);
		var parameters = new Dictionary<string, object?> { ["destKey"] = destination.Number };

		// Find exits whose destination (has_home) points to the target.
		// has_home links exit → destination; at_location links exit → source room.
		var response = await ExecuteAsync(
			$"LET $candidates = (SELECT VALUE in.key FROM has_home WHERE out = {destinationTable}:$destKey);"
			+ "SELECT VALUE key FROM exit WHERE key IN $candidates",
			parameters, cancellationToken);

		var exitKeys = response.GetValue<List<int>>(1) ?? [];

		foreach (var key in exitKeys)
		{
			var exitParams = new Dictionary<string, object?> { ["key"] = key };
			var exitResponse = await ExecuteAsync("SELECT * FROM exit:$key", exitParams, cancellationToken);
			var exitResults = exitResponse.GetValue<List<ExitRecord>>(0)!;
			if (exitResults.Count == 0) continue;

			var objResponse = await ExecuteAsync("SELECT * FROM object:$key", exitParams, cancellationToken);
			var objResults = objResponse.GetValue<List<ObjectRecord>>(0)!;
			if (objResults.Count > 0)
			{
				var sharpObj = MapRecordToSharpObject(objResults[0]);
				yield return BuildExit(ExitId(key), exitResults[0], sharpObj);
			}
		}
	}

	#endregion

	#region Object Properties

	public async ValueTask SetObjectName(AnySharpObject obj, MString value, CancellationToken cancellationToken = default)
	{
		var parameters = new Dictionary<string, object?>
		{
			["key"] = obj.Object().Key,
			["name"] = MModule.plainText(value)
		};
		await ExecuteAsync("UPDATE object:$key SET name = $name", parameters, cancellationToken);
	}

	public async ValueTask SetContentHome(AnySharpContent obj, AnySharpContainer home, CancellationToken cancellationToken = default)
	{
		var objKey = ExtractKey(obj.Id);
		var homeKey = ExtractKey(home.Id);
		var srcTable = GetContentTable(obj);
		var destTable = GetContainerTable(home);

		var parameters = new Dictionary<string, object?>
		{
			["objKey"] = objKey,
			["homeKey"] = homeKey
		};

		// One transaction so the object is never momentarily home-less between DELETE and RELATE
		// (GetHomeAsync throws on a missing has_home edge).
		await ExecuteAsync(
			$"BEGIN TRANSACTION;" +
			$"DELETE has_home WHERE in = {srcTable}:$objKey;" +
			$"RELATE {srcTable}:$objKey->has_home->{destTable}:$homeKey;" +
			$"COMMIT TRANSACTION",
			parameters, cancellationToken);
	}

	public async ValueTask SetContentLocation(AnySharpContent obj, AnySharpContainer location, CancellationToken cancellationToken = default)
	{
		var objKey = ExtractKey(obj.Id);
		var locKey = ExtractKey(location.Id);
		var srcTable = GetContentTable(obj);
		var destTable = GetContainerTable(location);

		var parameters = new Dictionary<string, object?>
		{
			["objKey"] = objKey,
			["locKey"] = locKey
		};

		// One transaction so the object is never momentarily location-less between DELETE and RELATE
		// (GetLocationForTypedAsync throws on a missing at_location edge).
		await ExecuteAsync(
			$"BEGIN TRANSACTION;" +
			$"DELETE at_location WHERE in = {srcTable}:$objKey;" +
			$"RELATE {srcTable}:$objKey->at_location->{destTable}:$locKey;" +
			$"COMMIT TRANSACTION",
			parameters, cancellationToken);
	}

	public async ValueTask SetObjectParent(AnySharpObject obj, AnySharpObject? parent, CancellationToken cancellationToken = default)
	{
		var objKey = obj.Object().Key;
		var parameters = new Dictionary<string, object?> { ["key"] = objKey };

		if (parent == null)
		{
			await ExecuteAsync("DELETE has_parent WHERE in = object:$key", parameters, cancellationToken);
			return;
		}

		// Replace the parent edge in one transaction so a concurrent reader never sees it detached
		// between the DELETE and the RELATE.
		parameters["parentKey"] = parent.Object().Key;
		await ExecuteAsync(
			"BEGIN TRANSACTION;" +
			"DELETE has_parent WHERE in = object:$key;" +
			"RELATE object:$key->has_parent->object:$parentKey;" +
			"COMMIT TRANSACTION",
			parameters, cancellationToken);
	}

	public async ValueTask UnsetObjectParent(AnySharpObject obj, CancellationToken cancellationToken = default)
	=> await SetObjectParent(obj, null, cancellationToken);

	public async ValueTask SetObjectZone(AnySharpObject obj, AnySharpObject? zone, CancellationToken cancellationToken = default)
	{
		var objKey = obj.Object().Key;
		var parameters = new Dictionary<string, object?> { ["key"] = objKey };

		if (zone == null)
		{
			await ExecuteAsync("DELETE has_zone WHERE in = object:$key", parameters, cancellationToken);
			return;
		}

		// Replace the zone edge in one transaction so a concurrent reader never sees it detached
		// between the DELETE and the RELATE.
		parameters["zoneKey"] = zone.Object().Key;
		await ExecuteAsync(
			"BEGIN TRANSACTION;" +
			"DELETE has_zone WHERE in = object:$key;" +
			"RELATE object:$key->has_zone->object:$zoneKey;" +
			"COMMIT TRANSACTION",
			parameters, cancellationToken);
	}

	public async ValueTask UnsetObjectZone(AnySharpObject obj, CancellationToken cancellationToken = default)
	=> await SetObjectZone(obj, null, cancellationToken);

	public async ValueTask SetObjectOwner(AnySharpObject obj, SharpPlayer owner, CancellationToken cancellationToken = default)
	{
		var objKey = obj.Object().Key;
		var ownerKey = ExtractKey(owner.Id!);
		var parameters = new Dictionary<string, object?>
		{
			["key"] = objKey,
			["ownerKey"] = ownerKey
		};

		// Replace the owner edge inside a single transaction so a concurrent reader never observes the
		// object owner-less between the DELETE and the RELATE (GetObjectOwnerAsync throws on no owner).
		// SurrealDB graph-edge in/out cannot be UPDATEd, so the edge must be re-created — the
		// transaction is what makes that atomic to outside readers.
		await ExecuteAsync(
			"BEGIN TRANSACTION;" +
			"DELETE has_owner WHERE in = object:$key;" +
			"RELATE object:$key->has_owner->player:$ownerKey;" +
			"COMMIT TRANSACTION",
			parameters, cancellationToken);
	}

	public async ValueTask SetObjectWarnings(AnySharpObject obj, WarningType warnings, CancellationToken cancellationToken = default)
	{
		var parameters = new Dictionary<string, object?>
		{
			["key"] = obj.Object().Key,
			["warnings"] = (int)warnings
		};
		await ExecuteAsync("UPDATE object:$key SET warnings = $warnings", parameters, cancellationToken);
	}

	/// <summary>
	/// Every relation table an object vertex can appear in, as <c>in</c> or as <c>out</c>. Object
	/// deletion sweeps all of them — the inbound half is what unsets other objects' parent/zone/
	/// home/location references to the deleted object, standing in for the <c>db_top</c> scan in
	/// PennMUSH's <c>free_object()</c> (<c>src/destroy.c</c>) that a graph store cannot do.
	/// </summary>
	private static readonly string[] ObjectRelationTables =
	[
		"account_has_role", "account_owns_character", "at_location", "has_attribute",
		"has_attribute_entry", "has_attribute_flag", "has_attribute_owner", "has_flags",
		"has_home", "has_owner", "has_parent", "has_powers", "has_zone", "is_object",
		"mail_sender", "member_of_channel", "owner_of_channel", "received_mail"
	];

	public async ValueTask<bool> DeleteObjectAsync(DBRef dbref, CancellationToken cancellationToken = default)
	{
		var node = await GetObjectNodeAsync(dbref, cancellationToken);
		if (node.IsNone)
		{
			return false;
		}

		var known = node.Known;
		var name = known.Object().Name;
		var table = ExtractTable(known.Id()!);
		var key = dbref.Number;

		// Attributes first, one subtree at a time, reusing the same leaf-before-branch walk @WIPE uses.
		var topLevelAttributes = await ExecuteAsync(
			"SELECT VALUE key FROM type::thing($table, $key)->has_attribute->attribute",
			new Dictionary<string, object?> { ["table"] = table, ["key"] = key }, cancellationToken);

		foreach (var attributeKey in topLevelAttributes.GetValue<List<string>>(0) ?? [])
		{
			if (string.IsNullOrEmpty(attributeKey)) continue;

			await WipeAttributeDescendantsAsync(attributeKey, cancellationToken);
			await ExecuteAsync(
				"BEGIN TRANSACTION;" +
				"DELETE has_attribute WHERE out = attribute:⟨$attributeKey⟩;" +
				"DELETE has_attribute WHERE in = attribute:⟨$attributeKey⟩;" +
				"DELETE has_attribute_flag WHERE in = attribute:⟨$attributeKey⟩;" +
				"DELETE has_attribute_owner WHERE in = attribute:⟨$attributeKey⟩;" +
				"DELETE has_attribute_entry WHERE in = attribute:⟨$attributeKey⟩;" +
				"DELETE attribute:⟨$attributeKey⟩;" +
				"COMMIT TRANSACTION",
				new Dictionary<string, object?> { ["attributeKey"] = attributeKey }, cancellationToken);
		}

		// Mail *received* dies with the object (PennMUSH clear_player -> do_mail_clear + do_mail_purge).
		// Mail it sent to others survives with a dangling sender; MailFromAsync already yields None there.
		// LET binds the ids up front because deleting received_mail would otherwise destroy the subquery.
		await ExecuteAsync(
			"BEGIN TRANSACTION;" +
			"LET $doomedMail = (SELECT VALUE out FROM received_mail WHERE in = type::thing($table, $key));" +
			"DELETE mail_sender WHERE in IN $doomedMail;" +
			"DELETE received_mail WHERE out IN $doomedMail;" +
			"DELETE mail WHERE id IN $doomedMail;" +
			"COMMIT TRANSACTION",
			new Dictionary<string, object?> { ["table"] = table, ["key"] = key }, cancellationToken);

		var sweep = string.Join(string.Empty, ObjectRelationTables.Select(relation =>
			$"DELETE {relation} WHERE in IN $doomed OR out IN $doomed;"));

		await ExecuteAsync(
			"BEGIN TRANSACTION;" +
			"LET $doomed = [type::thing($table, $key), object:$key];" +
			sweep +
			"DELETE object_data WHERE objectKey = $key;" +
			"DELETE type::thing($table, $key);" +
			"DELETE object:$key;" +
			"COMMIT TRANSACTION",
			new Dictionary<string, object?> { ["table"] = table, ["key"] = key }, cancellationToken);

		logger.LogInformation("Deleted object #{DbRef} ({Name}) from the database", key, name);

		return true;
	}

	#endregion

	#region Object Helpers

	private static string GetContainerTable(AnySharpContainer container)
	{
		return container.Match(
			_ => "player",
			_ => "room",
			_ => "thing");
	}

	private static string GetContentTable(AnySharpContent content)
	{
		return content.Match(
			_ => "player",
			_ => "exit",
			_ => "thing");
	}

	#endregion
}
