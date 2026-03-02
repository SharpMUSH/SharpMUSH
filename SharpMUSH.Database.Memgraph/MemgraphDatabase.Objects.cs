using DotNext.Threading;
using MarkupString;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;
using OneOf.Types;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using MString = MarkupString.MarkupStringModule.MarkupString;

namespace SharpMUSH.Database.Memgraph;

public partial class MemgraphDatabase
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

		// Single atomic query: create Object + Player nodes with all relationships
		await ExecuteWithRetryAsync("""
MATCH (loc {key: $locKey}) WHERE loc:Room OR loc:Player OR loc:Thing
MATCH (hm {key: $homeKey}) WHERE hm:Room OR hm:Player OR hm:Thing
CREATE (o:Object {key: $key, name: $name, type: 'PLAYER', creationTime: $now, modifiedTime: $now, locks: '{}', warnings: 0})
CREATE (p:Player {key: $key, passwordHash: $hash, passwordSalt: $salt, aliases: [], quota: $quota})
CREATE (p)-[:IS_OBJECT]->(o)
CREATE (o)-[:HAS_OWNER]->(p)
CREATE (p)-[:AT_LOCATION]->(loc)
CREATE (p)-[:HAS_HOME]->(hm)
""", new { key = nextKey, name, now, hash = hashedPassword, salt = salt ?? "", quota, locKey = location.Number, homeKey = home.Number }, cancellationToken);

		return new DBRef(nextKey, now);
	}

	public async ValueTask<DBRef> CreateRoomAsync(string name, SharpPlayer creator, CancellationToken cancellationToken = default)
	{
		var nextKey = await GetNextObjectKeyAsync(cancellationToken);
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var creatorKey = creator.Object.Key;

		// Single atomic query: create Object + Room nodes with relationships
		await ExecuteWithRetryAsync("""
MATCH (owner:Player {key: $ownerKey})
CREATE (o:Object {key: $key, name: $name, type: 'ROOM', creationTime: $now, modifiedTime: $now, locks: '{}', warnings: 0})
CREATE (r:Room {key: $key, aliases: []})
CREATE (r)-[:IS_OBJECT]->(o)
CREATE (o)-[:HAS_OWNER]->(owner)
""", new { key = nextKey, name, now, ownerKey = creatorKey }, cancellationToken);

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

		// Single atomic query: create Object + Thing nodes with all relationships
		await ExecuteWithRetryAsync("""
MATCH (loc {key: $locKey}) WHERE loc:Room OR loc:Player OR loc:Thing
MATCH (hm {key: $homeKey}) WHERE hm:Room OR hm:Player OR hm:Thing
MATCH (owner:Player {key: $ownerKey})
CREATE (o:Object {key: $key, name: $name, type: 'THING', creationTime: $now, modifiedTime: $now, locks: '{}', warnings: 0})
CREATE (t:Thing {key: $key, aliases: []})
CREATE (t)-[:IS_OBJECT]->(o)
CREATE (t)-[:AT_LOCATION]->(loc)
CREATE (t)-[:HAS_HOME]->(hm)
CREATE (o)-[:HAS_OWNER]->(owner)
""", new { key = nextKey, name, now, locKey, homeKey, ownerKey = creatorKey }, cancellationToken);

		return new DBRef(nextKey, now);
	}

	public async ValueTask<DBRef> CreateExitAsync(string name, string[] aliases, AnySharpContainer location,
	SharpPlayer creator, CancellationToken cancellationToken = default)
	{
		var nextKey = await GetNextObjectKeyAsync(cancellationToken);
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var creatorKey = creator.Object.Key;
		var locKey = ExtractKey(location.Id);

		// Single atomic query: create Object + Exit nodes with all relationships
		await ExecuteWithRetryAsync("""
MATCH (loc {key: $locKey}) WHERE loc:Room OR loc:Player OR loc:Thing
MATCH (owner:Player {key: $ownerKey})
CREATE (o:Object {key: $key, name: $name, type: 'EXIT', creationTime: $now, modifiedTime: $now, locks: '{}', warnings: 0})
CREATE (e:Exit {key: $key, aliases: $aliases})
CREATE (e)-[:IS_OBJECT]->(o)
CREATE (e)-[:AT_LOCATION]->(loc)
CREATE (o)-[:HAS_OWNER]->(owner)
""", new { key = nextKey, name, now, aliases, locKey, ownerKey = creatorKey }, cancellationToken);

		return new DBRef(nextKey, now);
	}

	#endregion

	#region Links, Locks, Player Operations

	public async ValueTask<bool> LinkExitAsync(SharpExit exit, AnySharpContainer location, CancellationToken cancellationToken = default)
	{
		var exitKey = ExtractKey(exit.Id!);
		var destKey = ExtractKey(location.Id);
		await ExecuteWithRetryAsync("""
MATCH (e:Exit {key: $exitKey}), (dest {key: $destKey})
WHERE dest:Room OR dest:Player OR dest:Thing
CREATE (e)-[:HAS_HOME]->(dest)
""", new { exitKey, destKey }, cancellationToken);
		return true;
	}

	public async ValueTask<bool> UnlinkExitAsync(SharpExit exit, CancellationToken cancellationToken = default)
	{
		var exitKey = ExtractKey(exit.Id!);
		var result = await ExecuteWithRetryAsync("""
MATCH (e:Exit {key: $key})-[r:HAS_HOME]->()
DELETE r
RETURN count(r) AS cnt
""", new { key = exitKey }, cancellationToken);
		return result.Result.Count > 0 && result.Result[0]["cnt"].As<long>() > 0;
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

		await ExecuteWithRetryAsync("""
MATCH (r:Room {key: $roomKey}), (dest {key: $destKey})
WHERE dest:Room OR dest:Player OR dest:Thing
CREATE (r)-[:HAS_HOME]->(dest)
""", new { roomKey, destKey }, cancellationToken);
		return true;
	}

	public async ValueTask<bool> UnlinkRoomAsync(SharpRoom room, CancellationToken cancellationToken = default)
	{
		var roomKey = ExtractKey(room.Id!);
		var result = await ExecuteWithRetryAsync("""
MATCH (r:Room {key: $key})-[rel:HAS_HOME]->()
DELETE rel
RETURN count(rel) AS cnt
""", new { key = roomKey }, cancellationToken);
		return result.Result.Count > 0 && result.Result[0]["cnt"].As<long>() > 0;
	}

	public async ValueTask SetLockAsync(SharpObject target, string lockName, SharpLockData lockData, CancellationToken cancellationToken = default)
	{
		var newLocks = target.Locks
		.SetItem(lockName, lockData);
		var locksJson = SerializeLocks(newLocks);
		await ExecuteWithRetryAsync("MATCH (o:Object {key: $key}) SET o.locks = $locks", new { key = target.Key, locks = locksJson }, cancellationToken);
	}

	public async ValueTask UnsetLockAsync(SharpObject target, string lockName, CancellationToken cancellationToken = default)
	{
		var newLocks = target.Locks.Remove(lockName);
		var locksJson = SerializeLocks(newLocks);
		await ExecuteWithRetryAsync("MATCH (o:Object {key: $key}) SET o.locks = $locks", new { key = target.Key, locks = locksJson }, cancellationToken);
	}

	public async ValueTask SetPlayerPasswordAsync(SharpPlayer player, string password, string? salt = null, CancellationToken cancellationToken = default)
	{
		var hashed = salt != null
		? password
		: _passwordService.HashPassword(player.Object.DBRef.ToString(), password);
		var playerKey = ExtractKey(player.Id!);
		await ExecuteWithRetryAsync("MATCH (p:Player {key: $key}) SET p.passwordHash = $hash, p.passwordSalt = $salt", new { key = playerKey, hash = hashed, salt = salt ?? "" }, cancellationToken);
	}

	public async ValueTask SetPlayerQuotaAsync(SharpPlayer player, int quota, CancellationToken cancellationToken = default)
	{
		var playerKey = ExtractKey(player.Id!);
		await ExecuteWithRetryAsync("MATCH (p:Player {key: $key}) SET p.quota = $quota", new { key = playerKey, quota }, cancellationToken);
	}

	public async ValueTask<int> GetOwnedObjectCountAsync(SharpPlayer player, CancellationToken cancellationToken = default)
	{
		var playerKey = ExtractKey(player.Id!);
		var result = await ExecuteWithRetryAsync("""
MATCH (o:Object)-[:HAS_OWNER]->(p:Player {key: $key})
RETURN count(o) AS cnt
""", new { key = playerKey }, cancellationToken);
		return result.Result.Count > 0 ? (int)result.Result[0]["cnt"].As<long>() : 0;
	}

	#endregion

	#region Object Retrieval

	public async ValueTask<AnyOptionalSharpObject> GetObjectNodeAsync(DBRef dbref, CancellationToken cancellationToken = default)
	{
		var result = await ExecuteWithRetryAsync("MATCH (o:Object {key: $key}) RETURN o", new { key = dbref.Number }, cancellationToken);

		if (result.Result.Count == 0) return new None();

		var objNode = result.Result[0]["o"].As<INode>();
		if (dbref.CreationMilliseconds is not null && objNode["creationTime"].As<long>() != dbref.CreationMilliseconds)
			return new None();

		return await BuildTypedObjectFromObjectNode(objNode, cancellationToken);
	}

	public async ValueTask<SharpObject?> GetBaseObjectNodeAsync(DBRef dbref, CancellationToken cancellationToken = default)
	{
		var result = await ExecuteWithRetryAsync("MATCH (o:Object {key: $key}) RETURN o", new { key = dbref.Number }, cancellationToken);

		if (result.Result.Count == 0) return null;

		var objNode = result.Result[0]["o"].As<INode>();
		if (dbref.CreationMilliseconds.HasValue && objNode["creationTime"].As<long>() != dbref.CreationMilliseconds)
			return null;

		return MapNodeToSharpObject(objNode);
	}

	public async IAsyncEnumerable<SharpPlayer> GetPlayerByNameOrAliasAsync(string name, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var result = await ExecuteWithRetryAsync("""
MATCH (o:Object {type: 'PLAYER'})
MATCH (p:Player)-[:IS_OBJECT]->(o)
WHERE o.name = $name OR $name IN p.aliases
RETURN o, p
""", new { name }, cancellationToken);

		foreach (var record in result.Result)
		{
			var objNode = record["o"].As<INode>();
			var playerNode = record["p"].As<INode>();
			var sharpObj = MapNodeToSharpObject(objNode);
			var key = objNode["key"].As<int>();
			yield return BuildPlayer(PlayerId(key), playerNode, sharpObj);
		}
	}

	public async IAsyncEnumerable<SharpObject> GetAllObjectsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var result = await ExecuteWithRetryAsync("MATCH (o:Object) RETURN o", ct: cancellationToken);

		foreach (var record in result.Result)
		{
			yield return MapNodeToSharpObject(record["o"].As<INode>());
		}
	}

	public async IAsyncEnumerable<SharpObject> GetFilteredObjectsAsync(ObjectSearchFilter filter, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var conditions = new List<string>();
		var parameters = new Dictionary<string, object>();

		if (filter.Types is { Length: > 0 })
		{
			conditions.Add("o.type IN $types");
			parameters["types"] = filter.Types;
		}
		if (!string.IsNullOrEmpty(filter.NamePattern))
		{
			if (filter.UseRegex)
				conditions.Add("toLower(o.name) =~ $namePattern");
			else
				conditions.Add("toLower(o.name) CONTAINS toLower($namePattern)");
			parameters["namePattern"] = filter.UseRegex ? ToFullMatchRegex(filter.NamePattern.ToLower()) : filter.NamePattern;
		}
		if (filter.MinDbRef.HasValue)
		{
			conditions.Add("o.key >= $minKey");
			parameters["minKey"] = filter.MinDbRef.Value;
		}
		if (filter.MaxDbRef.HasValue)
		{
			conditions.Add("o.key <= $maxKey");
			parameters["maxKey"] = filter.MaxDbRef.Value;
		}

		var whereClause = conditions.Count > 0 ? $"WHERE {string.Join(" AND ", conditions)}" : "";
		var limitClause = "";
		if (filter.Skip.HasValue || filter.Limit.HasValue)
		{
			var skip = filter.Skip ?? 0;
			if (filter.Limit.HasValue)
				limitClause = $"SKIP {skip} LIMIT {filter.Limit.Value}";
			else if (skip > 0)
				limitClause = $"SKIP {skip}";
		}

		var cypher = $"MATCH (o:Object) {whereClause} RETURN o {limitClause}";
		var result = await ExecuteWithRetryAsync(cypher, parameters, cancellationToken);

		foreach (var record in result.Result)
		{
			yield return MapNodeToSharpObject(record["o"].As<INode>());
		}
	}

	public async IAsyncEnumerable<SharpPlayer> GetAllPlayersAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var result = await ExecuteWithRetryAsync("""
MATCH (p:Player)-[:IS_OBJECT]->(o:Object {type: 'PLAYER'})
RETURN o, p
""", ct: cancellationToken);

		foreach (var record in result.Result)
		{
			var objNode = record["o"].As<INode>();
			var playerNode = record["p"].As<INode>();
			var sharpObj = MapNodeToSharpObject(objNode);
			var key = objNode["key"].As<int>();
			yield return BuildPlayer(PlayerId(key), playerNode, sharpObj);
		}
	}

	public async IAsyncEnumerable<SharpExit> GetEntrancesAsync(DBRef destination, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var result = await ExecuteWithRetryAsync("""
MATCH (e:Exit)-[:AT_LOCATION]->(dest {key: $destKey})
MATCH (e)-[:IS_OBJECT]->(o:Object {type: 'EXIT'})
RETURN o, e
""", new { destKey = destination.Number }, cancellationToken);

		foreach (var record in result.Result)
		{
			var objNode = record["o"].As<INode>();
			var exitNode = record["e"].As<INode>();
			var sharpObj = MapNodeToSharpObject(objNode);
			var key = objNode["key"].As<int>();
			yield return BuildExit(ExitId(key), exitNode, sharpObj);
		}
	}

	#endregion

	#region Object Properties

	public async ValueTask SetObjectName(AnySharpObject obj, MString value, CancellationToken cancellationToken = default)
	{
		await ExecuteWithRetryAsync("MATCH (o:Object {key: $key}) SET o.name = $name", new { key = obj.Object().Key, name = value }, cancellationToken);
	}

	public async ValueTask SetContentHome(AnySharpContent obj, AnySharpContainer home, CancellationToken cancellationToken = default)
	{
		var objKey = ExtractKey(obj.Id);
		var homeKey = ExtractKey(home.Id);
		await ExecuteWithRetryAsync("""
MATCH (src {key: $objKey})-[r:HAS_HOME]->()
DELETE r
WITH src
MATCH (dest {key: $homeKey})
WHERE dest:Room OR dest:Player OR dest:Thing
CREATE (src)-[:HAS_HOME]->(dest)
""", new { objKey, homeKey }, cancellationToken);
	}

	public async ValueTask SetContentLocation(AnySharpContent obj, AnySharpContainer location, CancellationToken cancellationToken = default)
	{
		var objKey = ExtractKey(obj.Id);
		var locKey = ExtractKey(location.Id);
		await ExecuteWithRetryAsync("""
MATCH (src {key: $objKey})-[r:AT_LOCATION]->()
DELETE r
WITH src
MATCH (dest {key: $locKey})
WHERE dest:Room OR dest:Player OR dest:Thing
CREATE (src)-[:AT_LOCATION]->(dest)
""", new { objKey, locKey }, cancellationToken);
	}

	public async ValueTask SetObjectParent(AnySharpObject obj, AnySharpObject? parent, CancellationToken cancellationToken = default)
	{
		var objKey = obj.Object().Key;
		// Remove existing parent edge
		await ExecuteWithRetryAsync("MATCH (o:Object {key: $key})-[r:HAS_PARENT]->() DELETE r", new { key = objKey }, cancellationToken);

		if (parent != null)
		{
			var parentKey = parent.Object().Key;
			await ExecuteWithRetryAsync("""
MATCH (o:Object {key: $key}), (p:Object {key: $parentKey})
CREATE (o)-[:HAS_PARENT]->(p)
""", new { key = objKey, parentKey }, cancellationToken);
		}
	}

	public async ValueTask UnsetObjectParent(AnySharpObject obj, CancellationToken cancellationToken = default)
	=> await SetObjectParent(obj, null, cancellationToken);

	public async ValueTask SetObjectZone(AnySharpObject obj, AnySharpObject? zone, CancellationToken cancellationToken = default)
	{
		var objKey = obj.Object().Key;
		await ExecuteWithRetryAsync("MATCH (o:Object {key: $key})-[r:HAS_ZONE]->() DELETE r", new { key = objKey }, cancellationToken);

		if (zone != null)
		{
			var zoneKey = zone.Object().Key;
			await ExecuteWithRetryAsync("""
MATCH (o:Object {key: $key}), (z:Object {key: $zoneKey})
CREATE (o)-[:HAS_ZONE]->(z)
""", new { key = objKey, zoneKey }, cancellationToken);
		}
	}

	public async ValueTask UnsetObjectZone(AnySharpObject obj, CancellationToken cancellationToken = default)
	=> await SetObjectZone(obj, null, cancellationToken);

	public async ValueTask SetObjectOwner(AnySharpObject obj, SharpPlayer owner, CancellationToken cancellationToken = default)
	{
		var objKey = obj.Object().Key;
		var ownerKey = ExtractKey(owner.Id!);
		await ExecuteWithRetryAsync("""
MATCH (o:Object {key: $key})-[r:HAS_OWNER]->()
DELETE r
WITH o
MATCH (p:Player {key: $ownerKey})
CREATE (o)-[:HAS_OWNER]->(p)
""", new { key = objKey, ownerKey }, cancellationToken);
	}

	public async ValueTask SetObjectWarnings(AnySharpObject obj, WarningType warnings, CancellationToken cancellationToken = default)
	{
		await ExecuteWithRetryAsync("MATCH (o:Object {key: $key}) SET o.warnings = $warnings", new { key = obj.Object().Key, warnings = (int)warnings }, cancellationToken);
	}

	#endregion
}
