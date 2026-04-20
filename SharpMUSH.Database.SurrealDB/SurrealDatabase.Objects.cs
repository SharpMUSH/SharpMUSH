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
		: _passwordService.HashPassword($"#{nextKey}:{now}", password);

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

		await ExecuteAsync("""
			CREATE object:$key SET key = $key, name = $name, type = 'PLAYER', creationTime = $now, modifiedTime = $now, locks = '{}', warnings = 0;
			CREATE player:$key SET key = $key, passwordHash = $hash, passwordSalt = $salt, aliases = [], quota = $quota;
			RELATE player:$key->is_object->object:$key;
			RELATE object:$key->has_owner->player:$key;
			RELATE player:$key->at_location->room:$locKey;
			RELATE player:$key->has_home->room:$homeKey
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

		await ExecuteAsync("""
			CREATE object:$key SET key = $key, name = $name, type = 'ROOM', creationTime = $now, modifiedTime = $now, locks = '{}', warnings = 0;
			CREATE room:$key SET key = $key, aliases = [];
			RELATE room:$key->is_object->object:$key;
			RELATE object:$key->has_owner->player:$ownerKey
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

		await ExecuteAsync(
			$"CREATE object:$key SET key = $key, name = $name, type = 'THING', creationTime = $now, modifiedTime = $now, locks = $emptyLocks, warnings = 0;" +
			$"CREATE thing:$key SET key = $key, aliases = [];" +
			$"RELATE thing:$key->is_object->object:$key;" +
			$"RELATE thing:$key->at_location->{locTable}:$locKey;" +
			$"RELATE thing:$key->has_home->{homeTable}:$homeKey;" +
			$"RELATE object:$key->has_owner->player:$ownerKey",
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

		await ExecuteAsync(
			$"CREATE object:$key SET key = $key, name = $name, type = 'EXIT', creationTime = $now, modifiedTime = $now, locks = $emptyLocks, warnings = 0;" +
			$"CREATE exit:$key SET key = $key, aliases = $aliases;" +
			$"RELATE exit:$key->is_object->object:$key;" +
			$"RELATE exit:$key->at_location->{locTable}:$locKey;" +
			$"RELATE object:$key->has_owner->player:$ownerKey",
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

		await ExecuteAsync(
			$"RELATE exit:$exitKey->has_home->{destTable}:$destKey",
			parameters, cancellationToken);
		return true;
	}

	public async ValueTask<bool> UnlinkExitAsync(SharpExit exit, CancellationToken cancellationToken = default)
	{
		var exitKey = ExtractKey(exit.Id!);
		var parameters = new Dictionary<string, object?> { ["key"] = exitKey };

		var response = await ExecuteAsync(
			"DELETE has_home WHERE in = exit:$key RETURN BEFORE",
			parameters, cancellationToken);

		var results = response.GetValue<List<ObjectRecord>>(0)!;
		return results.Count > 0;
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

		var response = await ExecuteAsync(
			"DELETE has_home WHERE in = room:$key RETURN BEFORE",
			parameters, cancellationToken);

		var results = response.GetValue<List<ObjectRecord>>(0)!;
		return results.Count > 0;
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
		var hashed = salt != null
		? password
		: _passwordService.HashPassword(player.Object.DBRef.ToString(), password);
		var playerKey = ExtractKey(player.Id!);
		var parameters = new Dictionary<string, object?>
		{
			["key"] = playerKey,
			["hash"] = hashed,
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

	#endregion

	#region Object Retrieval

	public async ValueTask<AnyOptionalSharpObject> GetObjectNodeAsync(DBRef dbref, CancellationToken cancellationToken = default)
	{
		var parameters = new Dictionary<string, object?> { ["key"] = dbref.Number };
		var response = await ExecuteAsync("SELECT * FROM object WHERE key = $key", parameters, cancellationToken);

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
		var response = await ExecuteAsync("SELECT * FROM object WHERE key = $key", parameters, cancellationToken);

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

		// Also search by alias
		var aliasResponse = await ExecuteAsync(
			"SELECT * FROM player WHERE $name IN aliases",
			parameters, cancellationToken);

		var aliasResults = aliasResponse.GetValue<List<PlayerRecord>>(0)!;

		// Collect keys from name matches
		var foundKeys = new HashSet<int>();
		foreach (var objRecord in objResults)
		{
			var key = objRecord.key;
			if (foundKeys.Add(key))
			{
				var sharpObj = MapRecordToSharpObject(objRecord);
				var playerParams = new Dictionary<string, object?> { ["key"] = key };
				var playerResponse = await ExecuteAsync("SELECT * FROM player WHERE key = $key", playerParams, cancellationToken);
				var playerResults = playerResponse.GetValue<List<PlayerRecord>>(0)!;
				if (playerResults.Count > 0)
				{
					yield return BuildPlayer(PlayerId(key), playerResults[0], sharpObj);
				}
			}
		}

		// Collect keys from alias matches
		foreach (var playerRecord in aliasResults)
		{
			var key = playerRecord.key;
			if (foundKeys.Add(key))
			{
				var objParams = new Dictionary<string, object?> { ["key"] = key };
				var objResp = await ExecuteAsync("SELECT * FROM object WHERE key = $key", objParams, cancellationToken);
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
		// Fetch all objects
		var objResponse = await ExecuteAsync("SELECT * FROM object", cancellationToken);
		var objResults = objResponse.GetValue<List<ObjectRecord>>(0)!;

		// Fetch all typed records
		var playerResponse = await ExecuteAsync("SELECT * FROM player", cancellationToken);
		var playerResults = playerResponse.GetValue<List<PlayerRecord>>(0)!;
		var exitResponse = await ExecuteAsync("SELECT * FROM exit", cancellationToken);
		var exitResults = exitResponse.GetValue<List<ExitRecord>>(0)!;

		// Index typed records by key for efficient lookup
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

		var query = $"SELECT * FROM object {whereClause} {limitClause}";
		var response = await ExecuteAsync(query, parameters, cancellationToken);

		var results = response.GetValue<List<ObjectRecord>>(0)!;
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
			var objResponse = await ExecuteAsync("SELECT * FROM object WHERE key = $key", objParams, cancellationToken);
			var objResults = objResponse.GetValue<List<ObjectRecord>>(0)!;
			if (objResults.Count > 0)
			{
				var sharpObj = MapRecordToSharpObject(objResults[0]);
				yield return BuildPlayer(PlayerId(key), playerRecord, sharpObj);
			}
		}
	}

	public async IAsyncEnumerable<SharpExit> GetEntrancesAsync(DBRef destination, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var parameters = new Dictionary<string, object?> { ["destKey"] = destination.Number };

		// Find exits that have an at_location edge pointing to the destination
		var response = await ExecuteAsync(
			"SELECT VALUE in FROM at_location WHERE out.key = $destKey AND in.id LIKE 'exit:%'",
			parameters, cancellationToken);

		var results = response.GetValue<List<ExitRecord>>(0)!;

		foreach (var exitRecord in results)
		{
			var key = exitRecord.key;
			var objParams = new Dictionary<string, object?> { ["key"] = key };
			var objResponse = await ExecuteAsync("SELECT * FROM object WHERE key = $key", objParams, cancellationToken);
			var objResults = objResponse.GetValue<List<ObjectRecord>>(0)!;
			if (objResults.Count > 0)
			{
				var sharpObj = MapRecordToSharpObject(objResults[0]);
				yield return BuildExit(ExitId(key), exitRecord, sharpObj);
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

		await ExecuteAsync(
			$"DELETE has_home WHERE in = {srcTable}:$objKey;" +
			$"RELATE {srcTable}:$objKey->has_home->{destTable}:$homeKey",
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

		await ExecuteAsync(
			$"DELETE at_location WHERE in = {srcTable}:$objKey;" +
			$"RELATE {srcTable}:$objKey->at_location->{destTable}:$locKey",
			parameters, cancellationToken);
	}

	public async ValueTask SetObjectParent(AnySharpObject obj, AnySharpObject? parent, CancellationToken cancellationToken = default)
	{
		var objKey = obj.Object().Key;
		var parameters = new Dictionary<string, object?> { ["key"] = objKey };

		// Remove existing parent edge
		await ExecuteAsync("DELETE has_parent WHERE in = object:$key", parameters, cancellationToken);

		if (parent != null)
		{
			var parentKey = parent.Object().Key;
			var parentParams = new Dictionary<string, object?>
			{
				["key"] = objKey,
				["parentKey"] = parentKey
			};
			await ExecuteAsync(
				"RELATE object:$key->has_parent->object:$parentKey",
				parentParams, cancellationToken);
		}
	}

	public async ValueTask UnsetObjectParent(AnySharpObject obj, CancellationToken cancellationToken = default)
	=> await SetObjectParent(obj, null, cancellationToken);

	public async ValueTask SetObjectZone(AnySharpObject obj, AnySharpObject? zone, CancellationToken cancellationToken = default)
	{
		var objKey = obj.Object().Key;
		var parameters = new Dictionary<string, object?> { ["key"] = objKey };

		await ExecuteAsync("DELETE has_zone WHERE in = object:$key", parameters, cancellationToken);

		if (zone != null)
		{
			var zoneKey = zone.Object().Key;
			var zoneParams = new Dictionary<string, object?>
			{
				["key"] = objKey,
				["zoneKey"] = zoneKey
			};
			await ExecuteAsync(
				"RELATE object:$key->has_zone->object:$zoneKey",
				zoneParams, cancellationToken);
		}
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

		await ExecuteAsync(
			"DELETE has_owner WHERE in = object:$key;" +
			"RELATE object:$key->has_owner->player:$ownerKey",
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
