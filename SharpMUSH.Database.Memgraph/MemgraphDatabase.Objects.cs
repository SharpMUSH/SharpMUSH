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

namespace SharpMUSH.Database.Memgraph;

public partial class MemgraphDatabase
{
	/// <summary>God, who inherits ownership that would otherwise be severed by a delete.</summary>
	private const int GodKey = 1;

	#region Object CRUD

	public async ValueTask<DBRef> CreatePlayerAsync(string name, string password, DBRef location, DBRef home, int quota,
	string? salt = null, CancellationToken cancellationToken = default)
	{
		var nextKey = await GetNextObjectKeyAsync(cancellationToken);
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		var hashedPassword = salt != null
		? password
		: passwordService.HashPassword($"#{nextKey}:{now}", password);

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

	public async ValueTask<bool> DeleteObjectAsync(DBRef dbref, CancellationToken cancellationToken = default)
	{
		var node = await GetObjectNodeAsync(dbref, cancellationToken);
		if (node.IsNone)
		{
			return false;
		}

		var name = node.Known.Object().Name;

		// A channel always has an owner, so ownership cannot be allowed to die with the owner. The game
		// layer hands a doomed player's channels to the probate judge before it gets here
		// (ObjectDestructionService.ClearPlayerAsync); this is the floor under that for every other way
		// an object can be deleted, and it hands them to God rather than letting DETACH DELETE sever the
		// edge and leave a channel nobody owns.
		await ExecuteWithRetryAsync("""
MATCH (c:Channel)-[r:HAS_CHANNEL_OWNER]->(o:Object {key: $key})
MATCH (heir:Object {key: $heirKey})
WHERE o.key <> $heirKey
DELETE r
CREATE (c)-[:HAS_CHANNEL_OWNER]->(heir)
""", new { key = dbref.Number, heirKey = GodKey }, cancellationToken);

		// DETACH DELETE drops every incident relationship with the node, so the inbound half -- another
		// object's HAS_PARENT / HAS_ZONE / HAS_HOME / AT_LOCATION pointing here -- is unset in the same
		// step. That is the graph-native equivalent of the db_top scan in PennMUSH's free_object()
		// (src/destroy.c). Mail *received* dies with the object (clear_player -> do_mail_clear +
		// do_mail_purge); mail it sent survives with a dangling sender, which MailFromAsync tolerates.
		await ExecuteWithRetryAsync("""
MATCH (o:Object {key: $key})
OPTIONAL MATCH (typed)-[:IS_OBJECT]->(o)
OPTIONAL MATCH (typed)-[:HAS_ATTRIBUTE*1..]->(attr:Attribute)
OPTIONAL MATCH (typed)-[:RECEIVED_MAIL]->(mail:Mail)
OPTIONAL MATCH (o)-[:HAS_EXPANDED_DATA]->(data:ExpandedObjectData)
DETACH DELETE attr, mail, data, typed, o
""", new { key = dbref.Number }, cancellationToken);

		logger.LogInformation("Deleted object #{DbRef} ({Name}) from the database", dbref.Number, name);

		return true;
	}

	public async ValueTask<DBRef> CreateExitAsync(string name, string[] aliases, AnySharpContainer location,
	SharpPlayer creator, CancellationToken cancellationToken = default)
	{
		var nextKey = await GetNextObjectKeyAsync(cancellationToken);
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var creatorKey = creator.Object.Key;
		var locKey = ExtractKey(location.Id);

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
		var destLabel = ExtractTypedLabel(location.Id);
		// Relinking must replace the destination, not accumulate a second HAS_HOME edge.
		await UnlinkExitAsync(exit, cancellationToken);
		await ExecuteWithRetryAsync("""
MATCH (e:Exit {key: $exitKey}), (dest:%DEST_LABEL% {key: $destKey})
CREATE (e)-[:HAS_HOME]->(dest)
""".Replace("%DEST_LABEL%", destLabel), new { exitKey, destKey }, cancellationToken);
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
		var destinationId = location.Match(
		player => player.Id!,
		rm => rm.Id!,
		thing => thing.Id!,
		_ => throw new InvalidOperationException());
		var destKey = ExtractKey(destinationId);
		var destLabel = ExtractTypedLabel(destinationId);

		await ExecuteWithRetryAsync("""
MATCH (r:Room {key: $roomKey}), (dest:%DEST_LABEL% {key: $destKey})
CREATE (r)-[:HAS_HOME]->(dest)
""".Replace("%DEST_LABEL%", destLabel), new { roomKey, destKey }, cancellationToken);
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
		// Every caller passes an ALREADY-HASHED password (via PasswordService.HashPassword /
		// @password / @newpassword, or an imported PennMUSH hash when salt != null). Store it
		// verbatim; hashing here would double-hash and the character could never connect.
		var playerKey = ExtractKey(player.Id!);
		await ExecuteWithRetryAsync("MATCH (p:Player {key: $key}) SET p.passwordHash = $hash, p.passwordSalt = $salt", new { key = playerKey, hash = password, salt = salt ?? "" }, cancellationToken);
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

	public async ValueTask<int> GetObjectCountAsync(CancellationToken cancellationToken = default)
	{
		var result = await ExecuteWithRetryAsync("MATCH (o:Object) RETURN count(o) AS cnt",
			ct: cancellationToken);
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

	public async IAsyncEnumerable<AnySharpObject> GetAllTypedObjectsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		// Single Cypher query that fetches all Object nodes together with their type-specific
		// nodes (Player/Room/Thing/Exit) in one round-trip. This avoids the N+1 pattern of calling
		// GetObjectNodeAsync per object and, crucially, bypasses the per-object FusionCache lock
		// so that a full-database scan does not contend with concurrent player commands.
		var result = await ExecuteWithRetryAsync(
			"MATCH (typed)-[:IS_OBJECT]->(o:Object) RETURN o, typed, labels(typed) AS lbl",
			ct: cancellationToken);

		foreach (var record in result.Result)
		{
			var objNode = record["o"].As<INode>();
			var typedNode = record["typed"].As<INode>();
			var labels = record["lbl"].As<List<object>>().Select(x => x.ToString()!).ToList();
			var sharpObj = MapNodeToSharpObject(objNode);
			var typedId = GetTypedId(labels, objNode["key"].As<int>(), typedNode);

			AnyOptionalSharpObject typed = sharpObj.Type switch
			{
				"PLAYER" => BuildPlayer(typedId, typedNode, sharpObj),
				"ROOM" => BuildRoom(typedId, sharpObj),
				"THING" => BuildThing(typedId, sharpObj),
				"EXIT" => BuildExit(typedId, typedNode, sharpObj),
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

		// Relationship predicates as inner MATCHes: the object must have the edge, which is exactly
		// what an inner join gives. These were absent, and their absence was invisible — with no
		// condition contributed, a caller asking for "objects owned by #7" got `MATCH (o:Object)
		// RETURN o`, the entire database, reported as a filtered result.
		var joins = new List<string>();

		if (filter.Owner.HasValue)
		{
			joins.Add("MATCH (o)-[:HAS_OWNER]->(:Player {key: $ownerKey})");
			parameters["ownerKey"] = filter.Owner.Value.Number;
		}
		if (filter.Zone.HasValue)
		{
			joins.Add("MATCH (o)-[:HAS_ZONE]->(:Object {key: $zoneKey})");
			parameters["zoneKey"] = filter.Zone.Value.Number;
		}
		if (filter.Parent.HasValue)
		{
			joins.Add("MATCH (o)-[:HAS_PARENT]->(:Object {key: $parentKey})");
			parameters["parentKey"] = filter.Parent.Value.Number;
		}

		// Flags and powers need OPTIONAL MATCH + collect rather than an inner join, because both match
		// case-insensitively and the flag predicate also has to accept a type name — GetObjectFlagsAsync
		// synthesises a type-named flag that no HAS_FLAG edge backs, and HelperFunctions.HasFlag sees it.
		var stages = new List<string>();

		if (!string.IsNullOrEmpty(filter.HasFlag))
		{
			// Aliases as well as names, as HelperFunctions.HasFlag does. A flag's aliases are a list, so
			// they are flattened with reduce() rather than appended the way the single-valued power
			// alias below is; coalesce() covers a flag stored with no alias list at all.
			stages.Add("WITH DISTINCT o "
				+ "OPTIONAL MATCH (o)-[:HAS_FLAG]->(flag:ObjectFlag) "
				+ "WITH o, collect(toLower(flag.name)) AS flagNames, "
				+ "collect([a IN coalesce(flag.aliases, []) | toLower(a)]) AS aliasLists "
				+ "WITH o, flagNames, reduce(acc = [], l IN aliasLists | acc + l) AS aliasNames "
				+ "WHERE $flagName IN flagNames OR $flagName IN aliasNames OR toLower(o.type) = $flagName");
			parameters["flagName"] = filter.HasFlag.ToLowerInvariant();
		}
		if (!string.IsNullOrEmpty(filter.HasPower))
		{
			// Alias as well as name, mirroring HelperFunctions.HasPower. collect() drops nulls, so a
			// power with no alias contributes nothing rather than a null entry.
			stages.Add("WITH DISTINCT o "
				+ "OPTIONAL MATCH (o)-[:HAS_POWER]->(power:Power) "
				+ "WITH o, collect(toLower(power.name)) + collect(toLower(power.alias)) AS powerNames "
				+ "WHERE $powerName IN powerNames");
			parameters["powerName"] = filter.HasPower.ToLowerInvariant();
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

		var cypher = $"MATCH (o:Object) {string.Join(" ", joins)} {whereClause} "
			+ $"{string.Join(" ", stages)} RETURN DISTINCT o {limitClause}";
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

	public async IAsyncEnumerable<AnySharpContent> GetHomedAtAsync(DBRef home,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		// HAS_HOME links content → its home. Rooms are excluded in the match: a room reuses this
		// relationship for its drop-to, which is not a home.
		var result = await ExecuteWithRetryAsync("""
MATCH (c)-[:HAS_HOME]->(dest {key: $homeKey})
WHERE c:Player OR c:Thing OR c:Exit
RETURN c.key AS key
""", new { homeKey = home.Number }, cancellationToken);

		foreach (var record in result.Result)
		{
			var candidate = await GetObjectNodeAsync(new DBRef(record["key"].As<int>()), cancellationToken);
			if (candidate.IsNone || candidate.IsRoom) continue;

			yield return candidate.Known.AsContent;
		}
	}

	public async IAsyncEnumerable<SharpExit> GetEntrancesAsync(DBRef destination,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		// An exit's destination is HAS_HOME (AT_LOCATION is its *source* room), so entrances are the
		// exit-shaped subset of what is homed here. Matching on AT_LOCATION returned the exits leading
		// OUT of the room instead — the exact opposite set.
		await foreach (var content in GetHomedAtAsync(destination, cancellationToken))
		{
			if (content.IsExit)
			{
				yield return content.AsExit;
			}
		}
	}

	#endregion

	#region Object Properties

	public async ValueTask SetObjectName(AnySharpObject obj, MString value, CancellationToken cancellationToken = default)
	{
		await ExecuteWithRetryAsync("MATCH (o:Object {key: $key}) SET o.name = $name", new { key = obj.Object().Key, name = MModule.plainText(value) }, cancellationToken);
	}

	public async ValueTask SetContentHome(AnySharpContent obj, AnySharpContainer home, CancellationToken cancellationToken = default)
	{
		var objKey = ExtractKey(obj.Id);
		var homeKey = ExtractKey(home.Id);
		var objLabel = ExtractTypedLabel(obj.Id);
		var homeLabel = ExtractTypedLabel(home.Id);
		await ExecuteWithRetryAsync("""
MATCH (src:%SRC_LABEL% {key: $objKey})-[r:HAS_HOME]->()
DELETE r
WITH src
MATCH (dest:%DEST_LABEL% {key: $homeKey})
CREATE (src)-[:HAS_HOME]->(dest)
""".Replace("%SRC_LABEL%", objLabel).Replace("%DEST_LABEL%", homeLabel), new { objKey, homeKey }, cancellationToken);
	}

	public async ValueTask SetContentLocation(AnySharpContent obj, AnySharpContainer location, CancellationToken cancellationToken = default)
	{
		var objKey = ExtractKey(obj.Id);
		var locKey = ExtractKey(location.Id);
		var objLabel = ExtractTypedLabel(obj.Id);
		var locationLabel = ExtractTypedLabel(location.Id);
		await ExecuteWithRetryAsync("""
MATCH (src:%SRC_LABEL% {key: $objKey})-[r:AT_LOCATION]->()
DELETE r
WITH src
MATCH (dest:%DEST_LABEL% {key: $locKey})
CREATE (src)-[:AT_LOCATION]->(dest)
""".Replace("%SRC_LABEL%", objLabel).Replace("%DEST_LABEL%", locationLabel), new { objKey, locKey }, cancellationToken);
	}

	public async ValueTask SetObjectParent(AnySharpObject obj, AnySharpObject? parent, CancellationToken cancellationToken = default)
	{
		var objKey = obj.Object().Key;
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
