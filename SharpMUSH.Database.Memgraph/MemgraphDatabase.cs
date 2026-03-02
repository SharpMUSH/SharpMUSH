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

public partial class MemgraphDatabase(
ILogger<MemgraphDatabase> logger,
IDriver driver,
IPasswordService passwordService
) : ISharpDatabase
{
	private readonly IPasswordService _passwordService = passwordService;
	private static readonly SemaphoreSlim MigrateLock = new(1, 1);
	private static volatile bool _migrated;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = null,
		WriteIndented = false
	};

	#region Helpers

	private string ObjectId(int key) => $"Object/{key}";
	private string PlayerId(int key) => $"Player/{key}";
	private string RoomId(int key) => $"Room/{key}";
	private string ThingId(int key) => $"Thing/{key}";
	private string ExitId(int key) => $"Exit/{key}";
	private string AttributeId(string key) => $"Attribute/{key}";
	private string ObjectFlagId(string name) => $"ObjectFlag/{name}";
	private string PowerId(string name) => $"Power/{name}";
	private string AttributeFlagId(string name) => $"AttributeFlag/{name}";
	private string AttributeEntryId(string name) => $"AttributeEntry/{name}";
	private string ChannelId(string name) => $"Channel/{name}";
	private string MailId(string key) => $"Mail/{key}";

	private static int ExtractKey(string id)
	{
		var parts = id.Split('/');
		if (parts.Length < 2 || !int.TryParse(parts[1], out var key))
			throw new ArgumentException($"Invalid ID format: '{id}'. Expected 'Label/numericKey'.", nameof(id));
		return key;
	}

	private static string ExtractKeyString(string id) => id.Split('/')[1];

	/// <summary>
	/// Converts a partial-match regex to a full-match regex for Memgraph.
	/// Memgraph's =~ operator does full-string matching (like re.fullmatch),
	/// while ArangoDB does partial matching (like re.search).
	/// This adds .* anchors as needed to simulate partial matching.
	/// </summary>
	private static string ToFullMatchRegex(string pattern)
	{
		if (!pattern.StartsWith("^") && !pattern.StartsWith(".*"))
			pattern = ".*" + pattern;
		if (!pattern.EndsWith("$") && !pattern.EndsWith(".*"))
			pattern += ".*";
		return pattern;
	}

	/// <summary>
	/// Checks if a DatabaseException is a transient transaction conflict that should be retried.
	/// Memgraph's IN_MEMORY_TRANSACTIONAL mode throws DatabaseException (not TransientException)
	/// for MVCC write-write conflicts. Per Memgraph docs, these must be retried on the driver side:
	/// https://memgraph.com/docs/help-center/errors/transactions
	/// </summary>
	private static bool IsTransientConflict(DatabaseException ex)
		=> ex.Message.Contains("Cannot resolve conflicting transactions")
			|| ex.Message.Contains("serialization error");

	/// <summary>
	/// Executes a Cypher query with automatic retry on transient MVCC conflicts.
	/// Wraps driver.ExecutableQuery().ExecuteAsync() with exponential backoff.
	/// </summary>
	private async ValueTask<EagerResult<IReadOnlyList<IRecord>>> ExecuteWithRetryAsync(
		string cypher,
		object? parameters = null,
		CancellationToken ct = default,
		int maxRetries = 8)
	{
		for (var attempt = 0; ; attempt++)
		{
			try
			{
				var query = driver.ExecutableQuery(cypher);
				if (parameters is not null)
					query = query.WithParameters(parameters);
				return await query.ExecuteAsync(ct);
			}
			catch (DatabaseException ex) when (attempt < maxRetries && IsTransientConflict(ex))
			{
				logger.LogDebug(ex, "Memgraph transient conflict on attempt {Attempt}, retrying", attempt + 1);
				// Exponential backoff with jitter to reduce contention thundering herd
				var baseDelay = Math.Min(25 * (1 << attempt), 500);
				var jitter = Random.Shared.Next(0, baseDelay / 2);
				await Task.Delay(baseDelay + jitter, ct);
			}
		}
	}


	private async ValueTask<int> GetNextObjectKeyAsync(CancellationToken ct = default)
	{
		var result = await ExecuteWithRetryAsync("""
MATCH (c:Counter {name: 'object_key'})
SET c.value = c.value + 1
RETURN c.value AS nextKey
""", ct: ct);
		return result.Result[0]["nextKey"].As<int>();
	}

	private static string SerializeLocks(IImmutableDictionary<string, SharpLockData>? locks)
	{
		if (locks == null || locks.Count == 0) return "{}";
		var dict = locks.ToDictionary(
		kvp => kvp.Key,
		kvp => new { kvp.Value.LockString, Flags = kvp.Value.Flags.ToString() });
		return JsonSerializer.Serialize(dict, JsonOptions);
	}

	private static IImmutableDictionary<string, SharpLockData> DeserializeLocks(string? json)
	{
		if (string.IsNullOrEmpty(json) || json == "{}")
			return ImmutableDictionary<string, SharpLockData>.Empty;
		try
		{
			var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOptions);
			if (dict == null) return ImmutableDictionary<string, SharpLockData>.Empty;
			var builder = ImmutableDictionary.CreateBuilder<string, SharpLockData>();
			foreach (var kvp in dict)
			{
				var lockString = kvp.Value.GetProperty("LockString").GetString() ?? "";
				var flagsStr = kvp.Value.TryGetProperty("Flags", out var flagsProp) ? flagsProp.GetString() : null;
				var flags = Library.Services.LockService.LockFlags.Default;
				if (!string.IsNullOrEmpty(flagsStr))
				{
					if (!Enum.TryParse<Library.Services.LockService.LockFlags>(flagsStr, out flags))
						flags = Library.Services.LockService.LockFlags.Default;
				}
				builder[kvp.Key] = new SharpLockData(lockString, flags);
			}
			return builder.ToImmutable();
		}
		catch
		{
			return ImmutableDictionary<string, SharpLockData>.Empty;
		}
	}

	private SharpObject MapRecordToSharpObject(IRecord record, string prefix = "o")
	{
		var node = record[prefix].As<INode>();
		return MapNodeToSharpObject(node);
	}

	private SharpObject MapNodeToSharpObject(INode node)
	{
		var key = node["key"].As<int>();
		var name = node["name"].As<string>();
		var type = node["type"].As<string>();
		var creationTime = node["creationTime"].As<long>();
		var modifiedTime = node["modifiedTime"].As<long>();
		var warnings = node.Properties.ContainsKey("warnings")
		? (WarningType)node["warnings"].As<int>()
		: WarningType.None;
		var locksJson = node.Properties.ContainsKey("locks") ? node["locks"].As<string?>() : null;
		var id = ObjectId(key);

		return new SharpObject
		{
			Id = id,
			Key = key,
			Name = name,
			Type = type,
			CreationTime = creationTime,
			ModifiedTime = modifiedTime,
			Warnings = warnings,
			Locks = DeserializeLocks(locksJson),
			Flags = new(() => GetObjectFlagsForIdAsync(id, type.ToUpper(), CancellationToken.None)),
			Powers = new(() => GetPowersForIdAsync(id, CancellationToken.None)),
			Attributes = new(() => GetTopLevelAttributesAsync(id, CancellationToken.None)),
			LazyAttributes = new(() => GetTopLevelLazyAttributesAsync(id, CancellationToken.None)),
			AllAttributes = new(() => GetAllAttributesForIdAsync(id, CancellationToken.None)),
			LazyAllAttributes = new(() => GetAllLazyAttributesForIdAsync(id, CancellationToken.None)),
			Owner = new(async ct => await GetObjectOwnerAsync(id, ct)),
			Parent = new(async ct => await GetParentAsync(id, ct)),
			Zone = new(async ct => await GetZoneAsync(id, ct)),
			Children = new(() => GetChildrenAsync(id, CancellationToken.None))
		};
	}

	private async ValueTask<AnyOptionalSharpObject> BuildTypedObjectFromObjectNode(INode objNode, CancellationToken ct)
	{
		var key = objNode["key"].As<int>();
		var type = objNode["type"].As<string>();
		var sharpObj = MapNodeToSharpObject(objNode);

		var typedResult = await ExecuteWithRetryAsync("MATCH (typed)-[:IS_OBJECT]->(o:Object {key: $key}) RETURN typed, labels(typed) AS lbl", new { key }, ct);

		if (typedResult.Result.Count == 0) return new None();

		var typedNode = typedResult.Result[0]["typed"].As<INode>();
		var labels = typedResult.Result[0]["lbl"].As<List<object>>().Select(x => x.ToString()!).ToList();
		var typedId = GetTypedId(labels, key, typedNode);

		return type switch
		{
			"PLAYER" => BuildPlayer(typedId, typedNode, sharpObj),
			"ROOM" => BuildRoom(typedId, sharpObj),
			"THING" => BuildThing(typedId, sharpObj),
			"EXIT" => BuildExit(typedId, typedNode, sharpObj),
			_ => throw new ArgumentException($"Invalid Object Type: '{type}'")
		};
	}

	private string GetTypedId(List<string> labels, int key, INode typedNode)
	{
		if (labels.Contains("Player")) return PlayerId(key);
		if (labels.Contains("Room")) return RoomId(key);
		if (labels.Contains("Thing")) return ThingId(key);
		if (labels.Contains("Exit")) return ExitId(key);
		throw new ArgumentException($"Unknown typed node labels: {string.Join(", ", labels)}");
	}

	private SharpPlayer BuildPlayer(string id, INode typedNode, SharpObject sharpObj)
	{
		var aliases = typedNode.Properties.ContainsKey("aliases")
		? typedNode["aliases"].As<List<object>>().Select(x => x.ToString()!).ToArray()
		: [];
		return new SharpPlayer
		{
			Id = id,
			Object = sharpObj,
			Aliases = aliases,
			PasswordHash = typedNode["passwordHash"].As<string>(),
			PasswordSalt = typedNode.Properties.ContainsKey("passwordSalt") ? typedNode["passwordSalt"].As<string?>() : null,
			Quota = typedNode["quota"].As<int>(),
			Location = new(async ct => await GetLocationForTypedAsync(id, ct)),
			Home = new(async ct => await GetHomeAsync(id, ct))
		};
	}

	private SharpRoom BuildRoom(string id, SharpObject sharpObj)
	{
		return new SharpRoom
		{
			Id = id,
			Object = sharpObj,
			Location = new(async ct => await GetDropToAsync(id, ct))
		};
	}

	private SharpThing BuildThing(string id, SharpObject sharpObj)
	{
		return new SharpThing
		{
			Id = id,
			Object = sharpObj,
			Location = new(async ct => await GetLocationForTypedAsync(id, ct)),
			Home = new(async ct => await GetHomeAsync(id, ct))
		};
	}

	private SharpExit BuildExit(string id, INode typedNode, SharpObject sharpObj)
	{
		var aliases = typedNode.Properties.ContainsKey("aliases")
		? typedNode["aliases"].As<List<object>>().Select(x => x.ToString()!).ToArray()
		: [];
		return new SharpExit
		{
			Id = id,
			Object = sharpObj,
			Aliases = aliases,
			Location = new(async ct => await GetLocationForTypedAsync(id, ct)),
			Home = new(async ct => await GetHomeAsync(id, ct))
		};
	}

	private async ValueTask<AnySharpContainer> GetLocationForTypedAsync(string typedId, CancellationToken ct)
	{
		var key = ExtractKey(typedId);
		var result = await ExecuteWithRetryAsync("""
MATCH (src {key: $key})-[:AT_LOCATION]->(dest)
MATCH (dest)-[:IS_OBJECT]->(destObj:Object)
RETURN destObj
""", new { key }, ct);

		if (result.Result.Count == 0)
			throw new InvalidOperationException($"No location found for {typedId}");

		var destObjNode = result.Result[0]["destObj"].As<INode>();
		var located = await BuildTypedObjectFromObjectNode(destObjNode, ct);
		return located.Match<AnySharpContainer>(
		player => player,
		room => room,
		_ => throw new Exception("Invalid location: Exit"),
		thing => thing,
		_ => throw new Exception("No location found"));
	}

	private async ValueTask<AnySharpContainer> GetHomeAsync(string typedId, CancellationToken ct)
	{
		var key = ExtractKey(typedId);
		var result = await ExecuteWithRetryAsync("""
MATCH (src {key: $key})-[:HAS_HOME]->(dest)
MATCH (dest)-[:IS_OBJECT]->(destObj:Object)
RETURN destObj
""", new { key }, ct);

		if (result.Result.Count == 0)
			throw new InvalidOperationException($"No home found for {typedId}");

		var destObjNode = result.Result[0]["destObj"].As<INode>();
		var homeObj = await BuildTypedObjectFromObjectNode(destObjNode, ct);
		return homeObj.Match<AnySharpContainer>(
		player => player,
		room => room,
		_ => throw new Exception("Invalid home: Exit"),
		thing => thing,
		_ => throw new Exception("No home found"));
	}

	private async ValueTask<AnyOptionalSharpContainer> GetDropToAsync(string roomId, CancellationToken ct)
	{
		var key = ExtractKey(roomId);
		var result = await ExecuteWithRetryAsync("""
MATCH (r:Room {key: $key})-[:HAS_HOME]->(dest)
MATCH (dest)-[:IS_OBJECT]->(destObj:Object)
RETURN destObj
""", new { key }, ct);

		if (result.Result.Count == 0) return new None();

		var destObjNode = result.Result[0]["destObj"].As<INode>();
		var dropToObj = await BuildTypedObjectFromObjectNode(destObjNode, ct);
		return dropToObj.Match<AnyOptionalSharpContainer>(
		player => player,
		room => room,
		_ => new None(),
		thing => thing,
		_ => new None());
	}

	private async ValueTask<SharpPlayer> GetObjectOwnerAsync(string objectId, CancellationToken ct)
	{
		var key = ExtractKey(objectId);
		var result = await ExecuteWithRetryAsync("""
MATCH (o:Object {key: $key})-[:HAS_OWNER]->(ownerTyped:Player)
MATCH (ownerTyped)-[:IS_OBJECT]->(ownerObj:Object)
RETURN ownerObj, ownerTyped
""", new { key }, ct);

		if (result.Result.Count == 0)
			throw new InvalidOperationException($"No owner found for {objectId}");

		var ownerObjNode = result.Result[0]["ownerObj"].As<INode>();
		var ownerTypedNode = result.Result[0]["ownerTyped"].As<INode>();
		var sharpObj = MapNodeToSharpObject(ownerObjNode);
		var ownerKey = ownerObjNode["key"].As<int>();
		return BuildPlayer(PlayerId(ownerKey), ownerTypedNode, sharpObj);
	}

	private async IAsyncEnumerable<SharpObjectFlag> GetObjectFlagsForIdAsync(string objectId, string type, [EnumeratorCancellation] CancellationToken ct = default)
	{
		var key = ExtractKey(objectId);
		var result = await ExecuteWithRetryAsync("MATCH (o:Object {key: $key})-[:HAS_FLAG]->(f:ObjectFlag) RETURN f", new { key }, ct);

		foreach (var record in result.Result)
		{
			yield return MapNodeToFlag(record["f"].As<INode>());
		}

		// Append the implicit type flag
		yield return new SharpObjectFlag
		{
			Name = type,
			SetPermissions = [],
			TypeRestrictions = [],
			Symbol = type[0].ToString(),
			System = true,
			UnsetPermissions = [],
			Id = null,
			Aliases = []
		};
	}

	private async IAsyncEnumerable<SharpPower> GetPowersForIdAsync(string objectId, [EnumeratorCancellation] CancellationToken ct = default)
	{
		var key = ExtractKey(objectId);
		var result = await ExecuteWithRetryAsync("MATCH (o:Object {key: $key})-[:HAS_POWER]->(p:Power) RETURN p", new { key }, ct);

		foreach (var record in result.Result)
		{
			yield return MapNodeToPower(record["p"].As<INode>());
		}
	}

	private static SharpObjectFlag MapNodeToFlag(INode node)
	{
		var name = node["name"].As<string>();
		return new SharpObjectFlag
		{
			Id = $"ObjectFlag/{name}",
			Name = name,
			Symbol = node.Properties.ContainsKey("symbol") ? node["symbol"].As<string>() : "",
			System = node.Properties.ContainsKey("system") && node["system"].As<bool>(),
			Disabled = node.Properties.ContainsKey("disabled") && node["disabled"].As<bool>(),
			Aliases = node.Properties.ContainsKey("aliases")
		? node["aliases"].As<List<object>>().Select(x => x.ToString()!).ToArray()
		: [],
			SetPermissions = node.Properties.ContainsKey("setPermissions")
		? node["setPermissions"].As<List<object>>().Select(x => x.ToString()!).ToArray()
		: [],
			UnsetPermissions = node.Properties.ContainsKey("unsetPermissions")
		? node["unsetPermissions"].As<List<object>>().Select(x => x.ToString()!).ToArray()
		: [],
			TypeRestrictions = node.Properties.ContainsKey("typeRestrictions")
		? node["typeRestrictions"].As<List<object>>().Select(x => x.ToString()!).ToArray()
		: []
		};
	}

	private static SharpPower MapNodeToPower(INode node)
	{
		var name = node["name"].As<string>();
		return new SharpPower
		{
			Id = $"Power/{name}",
			Name = name,
			Alias = node.Properties.ContainsKey("alias") ? node["alias"].As<string>() : "",
			System = node.Properties.ContainsKey("system") && node["system"].As<bool>(),
			Disabled = node.Properties.ContainsKey("disabled") && node["disabled"].As<bool>(),
			SetPermissions = node.Properties.ContainsKey("setPermissions")
		? node["setPermissions"].As<List<object>>().Select(x => x.ToString()!).ToArray()
		: [],
			UnsetPermissions = node.Properties.ContainsKey("unsetPermissions")
		? node["unsetPermissions"].As<List<object>>().Select(x => x.ToString()!).ToArray()
		: [],
			TypeRestrictions = node.Properties.ContainsKey("typeRestrictions")
		? node["typeRestrictions"].As<List<object>>().Select(x => x.ToString()!).ToArray()
		: []
		};
	}

	private static SharpAttributeFlag MapNodeToAttributeFlag(INode node)
	{
		var name = node["name"].As<string>();
		return new SharpAttributeFlag
		{
			Id = $"AttributeFlag/{name}",
			Key = name,
			Name = name,
			Symbol = node.Properties.ContainsKey("symbol") ? node["symbol"].As<string>() : "",
			System = node.Properties.ContainsKey("system") && node["system"].As<bool>(),
			Inheritable = node.Properties.ContainsKey("inheritable") && node["inheritable"].As<bool>()
		};
	}

	private static SharpAttributeEntry MapNodeToAttributeEntry(INode node)
	{
		return new SharpAttributeEntry
		{
			Id = $"AttributeEntry/{node["name"].As<string>()}",
			Name = node["name"].As<string>(),
			DefaultFlags = node.Properties.ContainsKey("defaultFlags")
		? node["defaultFlags"].As<List<object>>().Select(x => x.ToString()!).ToArray()
		: [],
			Limit = node.Properties.ContainsKey("lim") ? node["lim"].As<string?>() : null,
			Enum = node.Properties.ContainsKey("enumValues")
		? node["enumValues"].As<List<object>>().Select(x => x.ToString()!).ToArray()
		: null
		};
	}

	private async IAsyncEnumerable<SharpAttributeFlag> GetAttributeFlagsForAttrAsync(string attrId, [EnumeratorCancellation] CancellationToken ct = default)
	{
		var attrKey = ExtractKeyString(attrId);
		var result = await ExecuteWithRetryAsync("MATCH (a:Attribute {key: $key})-[:HAS_ATTRIBUTE_FLAG]->(f:AttributeFlag) RETURN f", new { key = attrKey }, ct);

		foreach (var record in result.Result)
		{
			yield return MapNodeToAttributeFlag(record["f"].As<INode>());
		}
	}

	private async ValueTask<SharpPlayer?> GetAttributeOwnerAsync(string attrId, CancellationToken ct)
	{
		var attrKey = ExtractKeyString(attrId);
		var result = await ExecuteWithRetryAsync("""
MATCH (a:Attribute {key: $key})-[:HAS_ATTRIBUTE_OWNER]->(p:Player)
MATCH (p)-[:IS_OBJECT]->(o:Object)
RETURN o, p
""", new { key = attrKey }, ct);

		if (result.Result.Count == 0) return null;

		var objNode = result.Result[0]["o"].As<INode>();
		var playerNode = result.Result[0]["p"].As<INode>();
		var sharpObj = MapNodeToSharpObject(objNode);
		var pKey = objNode["key"].As<int>();
		return BuildPlayer(PlayerId(pKey), playerNode, sharpObj);
	}

	private async ValueTask<SharpAttributeEntry?> GetRelatedAttributeEntryAsync(string attrId, CancellationToken ct)
	{
		var attrKey = ExtractKeyString(attrId);
		var result = await ExecuteWithRetryAsync("MATCH (a:Attribute {key: $key})-[:HAS_ATTRIBUTE_ENTRY]->(e:AttributeEntry) RETURN e", new { key = attrKey }, ct);

		if (result.Result.Count == 0) return null;
		return MapNodeToAttributeEntry(result.Result[0]["e"].As<INode>());
	}

	private async ValueTask<SharpAttribute> MapToSharpAttribute(INode node, CancellationToken ct)
	{
		var key = node["key"].As<string>();
		var id = AttributeId(key);
		var flags = await GetAttributeFlagsForAttrAsync(id, ct).ToArrayAsync(ct);
		return new SharpAttribute(
		id,
		key,
		node["name"].As<string>(),
		flags,
		null,
		node.Properties.ContainsKey("longName") ? node["longName"].As<string?>() : null,
		new AsyncLazy<IAsyncEnumerable<SharpAttribute>>(innerCt => Task.FromResult(GetTopLevelAttributesAsync(id, innerCt))),
		new AsyncLazy<SharpPlayer?>(async innerCt => await GetAttributeOwnerAsync(id, innerCt)),
		new AsyncLazy<SharpAttributeEntry?>(async innerCt => await GetRelatedAttributeEntryAsync(id, innerCt)))
		{
			Value = MarkupStringModule.deserialize(
		node.Properties.ContainsKey("value") ? node["value"].As<string>() ?? "" : "")
		};
	}

	private async ValueTask<LazySharpAttribute> MapToLazySharpAttribute(INode node, CancellationToken ct)
	{
		var key = node["key"].As<string>();
		var id = AttributeId(key);
		var flags = await GetAttributeFlagsForAttrAsync(id, ct).ToArrayAsync(ct);
		return new LazySharpAttribute(
		id,
		key,
		node["name"].As<string>(),
		flags,
		null,
		node.Properties.ContainsKey("longName") ? node["longName"].As<string?>() : null,
		new AsyncLazy<IAsyncEnumerable<LazySharpAttribute>>(innerCt => Task.FromResult(GetTopLevelLazyAttributesAsync(id, innerCt))),
		new AsyncLazy<SharpPlayer?>(async innerCt => await GetAttributeOwnerAsync(id, innerCt)),
		new AsyncLazy<SharpAttributeEntry?>(async innerCt => await GetRelatedAttributeEntryAsync(id, innerCt)),
		Value: new AsyncLazy<MString>(innerCt =>
		Task.FromResult(MarkupStringModule.deserialize(
		node.Properties.ContainsKey("value") ? node["value"].As<string>() ?? "" : ""))));
	}

	private async IAsyncEnumerable<SharpAttribute> GetTopLevelAttributesAsync(string parentId, [EnumeratorCancellation] CancellationToken ct = default)
	{
		string cypher;
		string key;
		if (parentId.StartsWith("Attribute"))
		{
			key = ExtractKeyString(parentId);
			cypher = "MATCH (parent:Attribute {key: $key})-[:HAS_ATTRIBUTE]->(child:Attribute) RETURN child";
		}
		else
		{
			// parentId is an Object ID — find the typed node first
			var objKey = ExtractKey(parentId);
			var typedResult = await ExecuteWithRetryAsync("MATCH (typed)-[:IS_OBJECT]->(o:Object {key: $key}) RETURN typed.key AS tkey", new { key = objKey }, ct);
			if (typedResult.Result.Count == 0) yield break;
			key = typedResult.Result[0]["tkey"].As<int>().ToString();
			cypher = "MATCH (parent {key: toInteger($key)})-[:HAS_ATTRIBUTE]->(child:Attribute) RETURN child";
		}

		var result = await ExecuteWithRetryAsync(cypher, new { key }, ct);

		foreach (var record in result.Result)
		{
			yield return await MapToSharpAttribute(record["child"].As<INode>(), ct);
		}
	}

	private async IAsyncEnumerable<LazySharpAttribute> GetTopLevelLazyAttributesAsync(string parentId, [EnumeratorCancellation] CancellationToken ct = default)
	{
		string cypher;
		string key;
		if (parentId.StartsWith("Attribute"))
		{
			key = ExtractKeyString(parentId);
			cypher = "MATCH (parent:Attribute {key: $key})-[:HAS_ATTRIBUTE]->(child:Attribute) RETURN child";
		}
		else
		{
			var objKey = ExtractKey(parentId);
			var typedResult = await ExecuteWithRetryAsync("MATCH (typed)-[:IS_OBJECT]->(o:Object {key: $key}) RETURN typed.key AS tkey", new { key = objKey }, ct);
			if (typedResult.Result.Count == 0) yield break;
			key = typedResult.Result[0]["tkey"].As<int>().ToString();
			cypher = "MATCH (parent {key: toInteger($key)})-[:HAS_ATTRIBUTE]->(child:Attribute) RETURN child";
		}

		var result = await ExecuteWithRetryAsync(cypher, new { key }, ct);

		foreach (var record in result.Result)
		{
			yield return await MapToLazySharpAttribute(record["child"].As<INode>(), ct);
		}
	}

	private async IAsyncEnumerable<SharpAttribute> GetAllAttributesForIdAsync(string parentId, [EnumeratorCancellation] CancellationToken ct = default)
	{
		int startKey;
		if (parentId.StartsWith("Attribute"))
		{
			// For attribute, find all descendants
			var attrKey = ExtractKeyString(parentId);
			var result = await ExecuteWithRetryAsync("MATCH (start:Attribute {key: $key})-[:HAS_ATTRIBUTE*1..999]->(child:Attribute) RETURN child", new { key = attrKey }, ct);
			foreach (var record in result.Result)
				yield return await MapToSharpAttribute(record["child"].As<INode>(), ct);
			yield break;
		}

		startKey = ExtractKey(parentId);
		var typedResult = await ExecuteWithRetryAsync("MATCH (typed)-[:IS_OBJECT]->(o:Object {key: $key}) RETURN typed.key AS tkey", new { key = startKey }, ct);
		if (typedResult.Result.Count == 0) yield break;
		var tkey = typedResult.Result[0]["tkey"].As<int>();

		var allResult = await ExecuteWithRetryAsync("MATCH (start {key: $tkey})-[:HAS_ATTRIBUTE*1..999]->(child:Attribute) RETURN child", new { tkey }, ct);

		foreach (var record in allResult.Result)
		{
			yield return await MapToSharpAttribute(record["child"].As<INode>(), ct);
		}
	}

	private async IAsyncEnumerable<LazySharpAttribute> GetAllLazyAttributesForIdAsync(string parentId, [EnumeratorCancellation] CancellationToken ct = default)
	{
		int startKey;
		if (parentId.StartsWith("Attribute"))
		{
			var attrKey = ExtractKeyString(parentId);
			var result = await ExecuteWithRetryAsync("MATCH (start:Attribute {key: $key})-[:HAS_ATTRIBUTE*1..999]->(child:Attribute) RETURN child", new { key = attrKey }, ct);
			foreach (var record in result.Result)
				yield return await MapToLazySharpAttribute(record["child"].As<INode>(), ct);
			yield break;
		}

		startKey = ExtractKey(parentId);
		var typedResult = await ExecuteWithRetryAsync("MATCH (typed)-[:IS_OBJECT]->(o:Object {key: $key}) RETURN typed.key AS tkey", new { key = startKey }, ct);
		if (typedResult.Result.Count == 0) yield break;
		var tkey = typedResult.Result[0]["tkey"].As<int>();

		var allResult = await ExecuteWithRetryAsync("MATCH (start {key: $tkey})-[:HAS_ATTRIBUTE*1..999]->(child:Attribute) RETURN child", new { tkey }, ct);

		foreach (var record in allResult.Result)
		{
			yield return await MapToLazySharpAttribute(record["child"].As<INode>(), ct);
		}
	}

	private IAsyncEnumerable<SharpObject>? GetChildrenAsync(string objectId, CancellationToken ct = default)
	{
		return GetChildrenAsyncInner(objectId, ct);
	}

	private async IAsyncEnumerable<SharpObject> GetChildrenAsyncInner(string objectId, [EnumeratorCancellation] CancellationToken ct = default)
	{
		var key = ExtractKey(objectId);
		var result = await ExecuteWithRetryAsync("MATCH (child:Object)-[:HAS_PARENT]->(parent:Object {key: $key}) RETURN child", new { key }, ct);

		foreach (var record in result.Result)
		{
			yield return MapNodeToSharpObject(record["child"].As<INode>());
		}
	}

	private async ValueTask<AnyOptionalSharpObject> GetZoneAsync(string objectId, CancellationToken ct)
	{
		var key = ExtractKey(objectId);
		var result = await ExecuteWithRetryAsync("MATCH (o:Object {key: $key})-[:HAS_ZONE]->(z:Object) RETURN z", new { key }, ct);

		if (result.Result.Count == 0) return new None();

		var zoneObjNode = result.Result[0]["z"].As<INode>();
		return await BuildTypedObjectFromObjectNode(zoneObjNode, ct);
	}

	[GeneratedRegex(@"\*\*|[.*+?^${}()|[\]/]")]
	private static partial Regex WildcardToRegex();

	#endregion

	#region Migration

	public async ValueTask Migrate(CancellationToken cancellationToken = default)
	{
		if (_migrated) return;
		await MigrateLock.WaitAsync(cancellationToken);
		try
		{
			if (_migrated) return;
			logger.LogInformation("Migrating Memgraph Database");

			// Storage mode IN_MEMORY_ANALYTICAL is set via container startup flags
			// (--storage-mode=IN_MEMORY_ANALYTICAL) to avoid MVCC transaction conflicts.

			// Create indexes (Memgraph uses CREATE INDEX ON syntax)
			var indexQueries = new[]
			{
"CREATE INDEX ON :Object(key)",
"CREATE INDEX ON :Object(type)",
"CREATE INDEX ON :Object(name)",
"CREATE INDEX ON :Player(key)",
"CREATE INDEX ON :Room(key)",
"CREATE INDEX ON :Thing(key)",
"CREATE INDEX ON :Exit(key)",
"CREATE INDEX ON :Attribute(key)",
"CREATE INDEX ON :ObjectFlag(name)",
"CREATE INDEX ON :Power(name)",
"CREATE INDEX ON :AttributeFlag(name)",
"CREATE INDEX ON :AttributeEntry(name)",
"CREATE INDEX ON :Channel(name)",
"CREATE INDEX ON :Counter(name)"
};

			foreach (var q in indexQueries)
			{
				try { await ExecuteWithRetryAsync(q, ct: cancellationToken); }
				catch { /* Index may already exist */ }
			}

			var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

			// Create Counter for auto-increment object keys
			await ExecuteWithRetryAsync("MERGE (c:Counter {name: 'object_key'}) ON CREATE SET c.value = 2", ct: cancellationToken);

			// Create Room Zero (key=0)
			await ExecuteWithRetryAsync("""
MERGE (o:Object {key: 0})
ON CREATE SET o.name = 'Room Zero', o.type = 'ROOM', o.creationTime = $now, o.modifiedTime = $now, o.locks = '{}', o.warnings = 0
MERGE (r:Room {key: 0})
ON CREATE SET r.aliases = []
MERGE (r)-[:IS_OBJECT]->(o)
""", new { now }, cancellationToken);

			// Create Player One - God (key=1)
			await ExecuteWithRetryAsync("""
MERGE (o:Object {key: 1})
ON CREATE SET o.name = 'God', o.type = 'PLAYER', o.creationTime = $now, o.modifiedTime = $now, o.locks = '{}', o.warnings = 0
MERGE (p:Player {key: 1})
ON CREATE SET p.passwordHash = '', p.passwordSalt = '', p.aliases = [], p.quota = 999999
MERGE (p)-[:IS_OBJECT]->(o)
""", new { now }, cancellationToken);

			// Create Room Two - Master Room (key=2)
			await ExecuteWithRetryAsync("""
MERGE (o:Object {key: 2})
ON CREATE SET o.name = 'Master Room', o.type = 'ROOM', o.creationTime = $now, o.modifiedTime = $now, o.locks = '{}', o.warnings = 0
MERGE (r:Room {key: 2})
ON CREATE SET r.aliases = []
MERGE (r)-[:IS_OBJECT]->(o)
""", new { now }, cancellationToken);

			// Player One at Room Zero
			await ExecuteWithRetryAsync("""
MATCH (p:Player {key: 1}), (r:Room {key: 0})
MERGE (p)-[:AT_LOCATION]->(r)
""", ct: cancellationToken);

			// Player One home is Room Zero
			await ExecuteWithRetryAsync("""
MATCH (p:Player {key: 1}), (r:Room {key: 0})
MERGE (p)-[:HAS_HOME]->(r)
""", ct: cancellationToken);

			// Ownership: Object nodes -> Player typed node
			await ExecuteWithRetryAsync("""
MATCH (ownerPlayer:Player {key: 1})
MATCH (o:Object) WHERE o.key IN [0, 1, 2]
MERGE (o)-[:HAS_OWNER]->(ownerPlayer)
""", ct: cancellationToken);

			// Create initial flags
			await CreateInitialFlags(cancellationToken);

			// Create initial attribute flags
			await CreateInitialAttributeFlags(cancellationToken);

			// Create initial powers
			await CreateInitialPowers(cancellationToken);

			// Create initial attribute entries
			await CreateInitialAttributeEntries(cancellationToken);

			// Give Player One the WIZARD flag
			await ExecuteWithRetryAsync("""
MATCH (o:Object {key: 1}), (f:ObjectFlag {name: 'WIZARD'})
MERGE (o)-[:HAS_FLAG]->(f)
""", ct: cancellationToken);

			logger.LogInformation("Memgraph Migration Completed");
			_migrated = true;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Memgraph Migration Failed");
			throw;
		}
		finally
		{
			MigrateLock.Release();
		}
	}

	private async Task CreateInitialFlags(CancellationToken ct)
	{
		var flags = new (string Name, string Symbol, string[]? Aliases, string[] SetPerms, string[] UnsetPerms, string[] TypeRestrictions)[]
		{
("WIZARD", "W", null, ["trusted","wizard","log"], ["trusted","wizard"], ["ROOM","PLAYER","EXIT","THING"]),
("ABODE", "A", null, [], [], ["ROOM"]),
("ANSI", "A", null, [], [], ["PLAYER"]),
("CHOWN_OK", "C", null, [], [], ["ROOM","PLAYER","THING"]),
("COLOR", "C", ["COLOUR"], [], [], ["PLAYER"]),
("DARK", "D", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
("FIXED", "F", null, ["wizard"], ["wizard"], ["PLAYER"]),
("FLOATING", "F", null, [], [], ["ROOM"]),
("HAVEN", "H", null, [], [], ["PLAYER"]),
("TRUST", "I", ["INHERIT"], ["trusted"], ["trusted"], ["ROOM","PLAYER","EXIT","THING"]),
("JUDGE", "J", null, ["royalty"], ["royalty"], ["PLAYER"]),
("JUMP_OK", "J", ["TEL-OK","TEL_OK","TELOK"], [], [], ["ROOM"]),
("LINK_OK", "L", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
("MONITOR", "M", ["LISTENER","WATCHER"], [], [], ["ROOM","PLAYER","THING"]),
("NO_LEAVE", "N", ["NOLEAVE"], [], [], ["THING"]),
("NO_TEL", "N", null, [], [], ["ROOM"]),
("OPAQUE", "O", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
("QUIET", "Q", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
("UNFINDABLE", "U", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
("VISUAL", "V", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
("SAFE", "X", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
("SHARED", "Z", ["ZONE"], [], [], ["PLAYER"]),
("Z_TEL", "Z", null, [], [], ["ROOM"]),
("LISTEN_PARENT", "^", ["^"], [], [], ["PLAYER"]),
("NOACCENTS", "~", null, [], [], ["PLAYER"]),
("UNREGISTERED", "?", null, ["royalty"], ["royalty"], ["PLAYER"]),
("NOSPOOF", "\"", null, ["odark"], ["odark"], ["ROOM","PLAYER","EXIT","THING"]),
("AUDIBLE", "a", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
("DEBUG", "b", ["TRACE"], [], [], ["ROOM","PLAYER","EXIT","THING"]),
("DESTROY_OK", "d", ["DEST_OK"], [], [], ["THING"]),
("ENTER_OK", "e", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
("GAGGED", "g", null, ["wizard"], ["wizard"], ["PLAYER"]),
("HALT", "h", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
("ORPHAN", "i", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
("JURY_OK", "j", ["JURYOK"], ["royalty"], ["royalty"], ["PLAYER"]),
("KEEPALIVE", "k", null, [], [], ["PLAYER"]),
("LIGHT", "l", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
("MISTRUST", "m", ["MYOPIC"], ["trusted"], ["trusted"], ["PLAYER","EXIT","THING"]),
("NO_COMMAND", "n", ["NOCOMMAND"], [], [], ["ROOM","PLAYER","EXIT","THING"]),
("ON_VACATION", "o", ["ONVACATION","ON-VACATION"], [], [], ["PLAYER"]),
("PUPPET", "P", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
("ROYALTY", "r", null, ["trusted","royalty","log"], ["trusted","royalty"], ["ROOM","PLAYER","EXIT","THING"]),
("SUSPECT", "s", null, ["wizard","mdark","log"], ["wizard","mdark"], ["ROOM","PLAYER","EXIT","THING"]),
("TRANSPARENT", "t", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
("VERBOSE", "v", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
("NO_WARN", "w", ["NOWARN"], [], [], ["ROOM","PLAYER","EXIT","THING"]),
("CLOUDY", "x", ["TERSE"], [], [], ["ROOM","PLAYER","EXIT","THING"]),
("CHAN_USEFIRSTMATCH", "", ["CHAN_FIRSTMATCH","CHAN_MATCHFIRST"], ["trusted"], ["trusted"], ["ROOM","PLAYER","EXIT","THING"]),
("HEAR_CONNECT", "", null, ["royalty"], [], ["ROOM","PLAYER","EXIT","THING"]),
("HEAVY", "", null, ["royalty"], [], ["ROOM","PLAYER","EXIT","THING"]),
("LOUD", "", null, ["royalty"], [], ["ROOM","PLAYER","EXIT","THING"]),
("NO_LOG", "", null, ["wizard","mdark","log"], ["wizard","mdark"], ["ROOM","PLAYER","EXIT","THING"]),
("PARANOID", "", null, ["odark"], ["odark"], ["ROOM","PLAYER","EXIT","THING"]),
("TRACK_MONEY", "", null, [], [], ["ROOM","PLAYER","EXIT","THING"]),
("XTERM256", "", ["XTERM","COLOR256"], [], [], ["PLAYER"]),
("MONIKER", "", null, ["royalty"], ["royalty"], ["ROOM","PLAYER","EXIT","THING"]),
("OPEN_OK", "", null, [], [], ["ROOM"]),
		};

		foreach (var f in flags)
		{
			await ExecuteWithRetryAsync("""
MERGE (f:ObjectFlag {name: $name})
ON CREATE SET f.symbol = $symbol, f.system = true, f.disabled = false,
f.aliases = $aliases, f.setPermissions = $setPerms, f.unsetPermissions = $unsetPerms,
f.typeRestrictions = $typeRestrictions
""", new
			{
				name = f.Name,
				symbol = f.Symbol,
				aliases = f.Aliases ?? Array.Empty<string>(),
				setPerms = f.SetPerms,
				unsetPerms = f.UnsetPerms,
				typeRestrictions = f.TypeRestrictions
			}, ct);
		}
	}

	private async Task CreateInitialAttributeFlags(CancellationToken ct)
	{
		var attrFlags = new (string Name, string Symbol, bool Inheritable)[]
		{
("no_command", "$", true),
("no_inherit", "i", true),
("no_clone", "c", true),
("mortal_dark", "m", true),
("wizard", "w", true),
("veiled", "V", true),
("nearby", "n", true),
("locked", "+", true),
("safe", "S", true),
("visual", "v", false),
("public", "p", false),
("debug", "b", true),
("no_debug", "B", true),
("regexp", "R", false),
("case", "C", false),
("nospace", "s", true),
("noname", "N", true),
("aahear", "A", false),
("amhear", "M", false),
("quiet", "Q", false),
("branch", "`", false),
("prefixmatch", "", false),
		};

		foreach (var af in attrFlags)
		{
			await ExecuteWithRetryAsync("""
MERGE (f:AttributeFlag {name: $name})
ON CREATE SET f.symbol = $symbol, f.system = true, f.inheritable = $inheritable
""", new { name = af.Name, symbol = af.Symbol, inheritable = af.Inheritable }, ct);
		}
	}

	private async Task CreateInitialPowers(CancellationToken ct)
	{
		var powers = new (string Name, string Alias, string[] SetPerms, string[] UnsetPerms)[]
		{
("Announce", "", ["wizard","log"], ["wizard"]),
("Boot", "", ["wizard","log"], ["wizard"]),
("Builder", "", ["wizard","log"], ["wizard"]),
("Can_Dark", "", ["wizard","log"], []),
("Can_HTTP", "", ["wizard","log"], []),
("Can_Spoof", "", ["wizard","log"], ["wizard"]),
("Chat_Privs", "", ["wizard","log"], ["wizard"]),
("Debit", "", ["wizard","log"], []),
("Functions", "", ["wizard","log"], ["wizard"]),
("Guest", "", ["wizard","log"], ["wizard"]),
("Halt", "", ["wizard","log"], ["wizard"]),
("Hide", "", ["wizard","log"], ["wizard"]),
("Hook", "", ["wizard","log"], []),
("Idle", "", ["wizard","log"], ["wizard"]),
("Immortal", "", ["wizard","log"], ["wizard"]),
("Link_Anywhere", "", ["wizard","log"], ["wizard"]),
("Login", "", ["wizard","log"], ["wizard"]),
("Long_Fingers", "", ["wizard","log"], ["wizard"]),
("Many_Attribs", "", ["wizard","log"], []),
("No_Pay", "", ["wizard","log"], ["wizard"]),
("No_Quota", "", ["wizard","log"], ["wizard"]),
("Open_Anywhere", "", ["wizard","log"], ["wizard"]),
("Pemit_All", "", ["wizard","log"], ["wizard"]),
("Pick_DBRefs", "", ["wizard","log"], ["wizard"]),
("Player_Create", "", ["wizard","log"], ["wizard"]),
("Poll", "", ["wizard","log"], ["wizard"]),
("Pueblo_Send", "", ["wizard","log"], ["wizard"]),
("Queue", "", ["wizard","log"], ["wizard"]),
("Search", "", ["wizard","log"], ["wizard"]),
("See_All", "", ["wizard","log"], ["wizard"]),
("See_Queue", "", ["wizard","log"], ["wizard"]),
("See_OOB", "", ["wizard","log"], ["wizard"]),
("SQL_OK", "", ["wizard","log"], ["wizard"]),
("Tport_Anything", "", ["wizard","log"], ["wizard"]),
("Tport_Anywhere", "", ["wizard","log"], ["wizard"]),
("Unkillable", "", ["wizard","log"], ["wizard"]),
		};

		foreach (var p in powers)
		{
			await ExecuteWithRetryAsync("""
MERGE (p:Power {name: $name})
ON CREATE SET p.alias = $alias, p.system = true, p.disabled = false,
p.setPermissions = $setPerms, p.unsetPermissions = $unsetPerms,
p.typeRestrictions = $typeRestrictions
""", new
			{
				name = p.Name,
				alias = p.Alias,
				setPerms = p.SetPerms,
				unsetPerms = p.UnsetPerms,
				typeRestrictions = new[] { "ROOM", "PLAYER", "EXIT", "THING" }
			}, ct);
		}
	}

	private async Task CreateInitialAttributeEntries(CancellationToken ct)
	{
		var entries = new (string Name, string[] DefaultFlags)[]
		{
("AAHEAR", ["no_command","prefixmatch"]),
("ABUY", ["no_command","prefixmatch"]),
("ACLONE", ["no_command","prefixmatch"]),
("ACONNECT", ["no_command","prefixmatch"]),
("ADEATH", ["no_command","prefixmatch"]),
("ADESCRIBE", ["no_command","prefixmatch"]),
("ADESTROY", ["no_inherit","no_clone","wizard","prefixmatch"]),
("ADISCONNECT", ["no_command","prefixmatch"]),
("ADROP", ["no_command","prefixmatch"]),
("AEFAIL", ["no_command","prefixmatch"]),
("AENTER", ["no_command","prefixmatch"]),
("AFAILURE", ["no_command","prefixmatch"]),
("AFOLLOW", ["no_command","prefixmatch"]),
("AGIVE", ["no_command","prefixmatch"]),
("AHEAR", ["no_command","prefixmatch"]),
("AIDESCRIBE", ["no_command","prefixmatch"]),
("ALEAVE", ["no_command","prefixmatch"]),
("ALFAIL", ["no_command","prefixmatch"]),
("ALIAS", ["no_command","visual","prefixmatch"]),
("AMAIL", ["wizard","prefixmatch"]),
("AMHEAR", ["no_command","prefixmatch"]),
("AMOVE", ["no_command","prefixmatch"]),
("ANAME", ["no_command","prefixmatch"]),
("APAYMENT", ["no_command","prefixmatch"]),
("ARECEIVE", ["no_command","prefixmatch"]),
("ASUCCESS", ["no_command","prefixmatch"]),
("ATPORT", ["no_command","prefixmatch"]),
("AUFAIL", ["no_command","prefixmatch"]),
("AUNFOLLOW", ["no_command","prefixmatch"]),
("AUSE", ["no_command","prefixmatch"]),
("AWAY", ["no_command","prefixmatch"]),
("AZENTER", ["no_command","prefixmatch"]),
("AZLEAVE", ["no_command","prefixmatch"]),
("BUY", ["no_command","prefixmatch"]),
("CHANALIAS", ["no_command"]),
("CHARGES", ["no_command","prefixmatch"]),
("CHATFORMAT", ["no_command","prefixmatch"]),
("COMMENT", ["no_command","no_clone","wizard","mortal_dark","prefixmatch"]),
("CONFORMAT", ["no_command","prefixmatch"]),
("COST", ["no_command","prefixmatch"]),
("DEATH", ["no_command","prefixmatch"]),
("DEBUGFORWARDLIST", ["no_command","no_inherit","prefixmatch"]),
("DESCFORMAT", ["no_command","prefixmatch"]),
("DESCRIBE", ["no_command","visual","prefixmatch","public","nearby"]),
("DESTINATION", ["no_command"]),
("DOING", ["no_command","no_inherit","visual","public"]),
("DROP", ["no_command","prefixmatch"]),
("EALIAS", ["no_command","prefixmatch"]),
("EFAIL", ["no_command","prefixmatch"]),
("ENTER", ["no_command","prefixmatch"]),
("EXITFORMAT", ["no_command","prefixmatch"]),
("EXITTO", ["no_command","prefixmatch"]),
("FAILURE", ["no_command","prefixmatch"]),
("FILTER", ["no_command","prefixmatch"]),
("FOLLOW", ["no_command","prefixmatch"]),
("FOLLOWERS", ["no_command","no_inherit","no_clone","wizard","prefixmatch"]),
("FOLLOWING", ["no_command","no_inherit","no_clone","wizard","prefixmatch"]),
("FORWARDLIST", ["no_command","no_inherit","prefixmatch"]),
("GIVE", ["no_command","prefixmatch"]),
("HAVEN", ["no_command","prefixmatch"]),
("IDESCFORMAT", ["no_command","prefixmatch"]),
("IDESCRIBE", ["no_command","prefixmatch"]),
("IDLE", ["no_command","prefixmatch"]),
("INFILTER", ["no_command","prefixmatch"]),
("INPREFIX", ["no_command","prefixmatch"]),
("INVFORMAT", ["no_command","prefixmatch"]),
("LALIAS", ["no_command","prefixmatch"]),
("LAST", ["no_clone","wizard","visual","locked","prefixmatch"]),
("LASTFAILED", ["no_clone","wizard","locked","prefixmatch"]),
("LASTIP", ["no_clone","wizard","locked","prefixmatch"]),
("LASTLOGOUT", ["no_clone","wizard","locked","prefixmatch"]),
("LASTPAGED", ["no_clone","wizard","locked","prefixmatch"]),
("LASTSITE", ["no_clone","wizard","locked","prefixmatch"]),
("LEAVE", ["no_command","prefixmatch"]),
("LFAIL", ["no_command","prefixmatch"]),
("LISTEN", ["no_command","prefixmatch"]),
("MAILCURF", ["no_command","no_clone","wizard","locked","prefixmatch"]),
("MAILFILTER", ["no_command","prefixmatch"]),
("MAILFILTERS", ["no_command","no_clone","wizard","locked","prefixmatch"]),
("MAILFOLDERS", ["no_command","no_clone","wizard","locked","prefixmatch"]),
("MAILFORWARDLIST", ["no_command","prefixmatch"]),
("MAILQUOTA", ["no_command","no_clone","wizard","locked"]),
("MAILSIGNATURE", ["no_command","prefixmatch"]),
("MONIKER", ["no_command","wizard","visual","locked"]),
("MOVE", ["no_command","prefixmatch"]),
("NAMEACCENT", ["no_command","visual","prefixmatch"]),
("NAMEFORMAT", ["no_command","prefixmatch"]),
("OBUY", ["no_command","prefixmatch"]),
("ODEATH", ["no_command","prefixmatch"]),
("ODESCRIBE", ["no_command","prefixmatch"]),
("ODROP", ["no_command","prefixmatch"]),
("OEFAIL", ["no_command","prefixmatch"]),
("OENTER", ["no_command","prefixmatch"]),
("OFAILURE", ["no_command","prefixmatch"]),
("OFOLLOW", ["no_command","prefixmatch"]),
("OGIVE", ["no_command","prefixmatch"]),
("OIDESCRIBE", ["no_command","prefixmatch"]),
("OLEAVE", ["no_command","prefixmatch"]),
("OLFAIL", ["no_command","prefixmatch"]),
("OMOVE", ["no_command","prefixmatch"]),
("ONAME", ["no_command","prefixmatch"]),
("OPAYMENT", ["no_command","prefixmatch"]),
("ORECEIVE", ["no_command","prefixmatch"]),
("OSUCCESS", ["no_command","prefixmatch"]),
("OTPORT", ["no_command","prefixmatch"]),
("OUFAIL", ["no_command","prefixmatch"]),
("OUNFOLLOW", ["no_command","prefixmatch"]),
("OUSE", ["no_command","prefixmatch"]),
("OUTPAGEFORMAT", ["no_command","prefixmatch"]),
("OXENTER", ["no_command","prefixmatch"]),
("OXLEAVE", ["no_command","prefixmatch"]),
("OXMOVE", ["no_command","prefixmatch"]),
("OXTPORT", ["no_command","prefixmatch"]),
("OZENTER", ["no_command","prefixmatch"]),
("OZLEAVE", ["no_command","prefixmatch"]),
("PAGEFORMAT", ["no_command","prefixmatch"]),
("PAYMENT", ["no_command","prefixmatch"]),
("PREFIX", ["no_command","prefixmatch"]),
("PRICELIST", ["no_command","prefixmatch"]),
("QUEUE", ["no_inherit","no_clone","wizard"]),
("RECEIVE", ["no_command","prefixmatch"]),
("REGISTERED_EMAIL", ["no_inherit","no_clone","wizard","locked"]),
("RQUOTA", ["mortal_dark","locked"]),
("RUNOUT", ["no_command","prefixmatch"]),
("SEMAPHORE", ["no_inherit","no_clone","locked"]),
("SEX", ["no_command","visual","prefixmatch"]),
("SPEECHMOD", ["no_command","prefixmatch"]),
("STARTUP", ["no_command","prefixmatch"]),
("SUCCESS", ["no_command","prefixmatch"]),
("TFPREFIX", ["no_command","no_inherit","no_clone","prefixmatch"]),
("TPORT", ["no_command","prefixmatch"]),
("TZ", ["no_command","visual"]),
("UFAIL", ["no_command","prefixmatch"]),
("UNFOLLOW", ["no_command","prefixmatch"]),
("USE", ["no_command","prefixmatch"]),
("VA", []), ("VB", []), ("VC", []), ("VD", []), ("VE", []), ("VF", []),
("VG", []), ("VH", []), ("VI", []), ("VJ", []), ("VK", []), ("VL", []),
("VM", []), ("VN", []), ("VO", []), ("VP", []), ("VQ", []), ("VR", []),
("VRML_URL", ["no_command","prefixmatch"]),
("VS", []), ("VT", []), ("VU", []), ("VV", []), ("VW", []), ("VX", []),
("VY", []), ("VZ", []),
("WA", []), ("WB", []), ("WC", []), ("WD", []), ("WE", []), ("WF", []),
("WG", []), ("WH", []), ("WI", []), ("WJ", []), ("WK", []), ("WL", []),
("WM", []), ("WN", []), ("WO", []), ("WP", []), ("WQ", []), ("WR", []),
("WS", []), ("WT", []), ("WU", []), ("WV", []), ("WW", []), ("WX", []),
("WY", []), ("WZ", []),
("XA", []), ("XB", []), ("XC", []), ("XD", []), ("XE", []), ("XF", []),
("XG", []), ("XH", []), ("XI", []), ("XJ", []), ("XK", []), ("XL", []),
("XM", []), ("XN", []), ("XO", []), ("XP", []), ("XQ", []), ("XR", []),
("XS", []), ("XT", []), ("XU", []), ("XV", []), ("XW", []), ("XX", []),
("XY", []), ("XZ", []),
("ZENTER", ["no_command","prefixmatch"]),
		};

		foreach (var e in entries)
		{
			await ExecuteWithRetryAsync("""
MERGE (e:AttributeEntry {name: $name})
ON CREATE SET e.defaultFlags = $defaultFlags, e.lim = '', e.enumValues = []
""", new { name = e.Name, defaultFlags = e.DefaultFlags }, ct);
		}
	}

	#endregion

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

	#region Flags and Powers

	public async ValueTask<SharpObjectFlag?> GetObjectFlagAsync(string name, CancellationToken cancellationToken = default)
	{
		var result = await ExecuteWithRetryAsync("MATCH (f:ObjectFlag {name: $name}) RETURN f", new { name }, cancellationToken);
		return result.Result.Count > 0 ? MapNodeToFlag(result.Result[0]["f"].As<INode>()) : null;
	}

	public async IAsyncEnumerable<SharpObjectFlag> GetObjectFlagsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var result = await ExecuteWithRetryAsync("MATCH (f:ObjectFlag) RETURN f", ct: cancellationToken);
		foreach (var record in result.Result)
			yield return MapNodeToFlag(record["f"].As<INode>());
	}

	public async ValueTask<SharpObjectFlag?> CreateObjectFlagAsync(string name, string[]? aliases, string symbol,
	bool system, string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions,
	CancellationToken cancellationToken = default)
	{
		await ExecuteWithRetryAsync("""
CREATE (f:ObjectFlag {name: $name, symbol: $symbol, system: $system, disabled: false,
aliases: $aliases, setPermissions: $setPerms, unsetPermissions: $unsetPerms, typeRestrictions: $typeRestrictions})
""", new
		{
			name,
			symbol,
			system,
			aliases = aliases ?? Array.Empty<string>(),
			setPerms = setPermissions,
			unsetPerms = unsetPermissions,
			typeRestrictions
		}, cancellationToken);

		return new SharpObjectFlag
		{
			Id = ObjectFlagId(name), Name = name, Aliases = aliases, Symbol = symbol,
			System = system, SetPermissions = setPermissions,
			UnsetPermissions = unsetPermissions, TypeRestrictions = typeRestrictions
		};
	}

	public async ValueTask<bool> DeleteObjectFlagAsync(string name, CancellationToken cancellationToken = default)
	{
		var flag = await GetObjectFlagAsync(name, cancellationToken);
		if (flag == null || flag.System) return false;

		await ExecuteWithRetryAsync("MATCH (f:ObjectFlag {name: $name}) DETACH DELETE f", new { name }, cancellationToken);
		return true;
	}

	public async ValueTask<bool> SetObjectFlagAsync(AnySharpObject dbref, SharpObjectFlag flag, CancellationToken cancellationToken = default)
	{
		var objKey = dbref.Object().Key;
		// Check if already set
		var existing = await ExecuteWithRetryAsync("""
MATCH (o:Object {key: $key})-[:HAS_FLAG]->(f:ObjectFlag {name: $fname})
RETURN count(f) AS cnt
""", new { key = objKey, fname = flag.Name }, cancellationToken);

		if (existing.Result.Count > 0 && existing.Result[0]["cnt"].As<long>() > 0) return false;

		await ExecuteWithRetryAsync("""
MATCH (o:Object {key: $key}), (f:ObjectFlag {name: $fname})
CREATE (o)-[:HAS_FLAG]->(f)
""", new { key = objKey, fname = flag.Name }, cancellationToken);
		return true;
	}

	public async ValueTask<bool> UnsetObjectFlagAsync(AnySharpObject dbref, SharpObjectFlag flag, CancellationToken cancellationToken = default)
	{
		var objKey = dbref.Object().Key;
		var result = await ExecuteWithRetryAsync("""
MATCH (o:Object {key: $key})-[r:HAS_FLAG]->(f:ObjectFlag {name: $fname})
DELETE r
RETURN count(r) AS cnt
""", new { key = objKey, fname = flag.Name }, cancellationToken);
		return result.Result.Count > 0 && result.Result[0]["cnt"].As<long>() > 0;
	}

	public async ValueTask<bool> SetObjectPowerAsync(AnySharpObject dbref, SharpPower power, CancellationToken cancellationToken = default)
	{
		var objKey = dbref.Object().Key;
		var existing = await ExecuteWithRetryAsync("""
MATCH (o:Object {key: $key})-[:HAS_POWER]->(p:Power {name: $pname})
RETURN count(p) AS cnt
""", new { key = objKey, pname = power.Name }, cancellationToken);

		if (existing.Result.Count > 0 && existing.Result[0]["cnt"].As<long>() > 0) return false;

		await ExecuteWithRetryAsync("""
MATCH (o:Object {key: $key}), (p:Power {name: $pname})
CREATE (o)-[:HAS_POWER]->(p)
""", new { key = objKey, pname = power.Name }, cancellationToken);
		return true;
	}

	public async ValueTask<bool> UnsetObjectPowerAsync(AnySharpObject dbref, SharpPower power, CancellationToken cancellationToken = default)
	{
		var objKey = dbref.Object().Key;
		var result = await ExecuteWithRetryAsync("""
MATCH (o:Object {key: $key})-[r:HAS_POWER]->(p:Power {name: $pname})
DELETE r
RETURN count(r) AS cnt
""", new { key = objKey, pname = power.Name }, cancellationToken);
		return result.Result.Count > 0 && result.Result[0]["cnt"].As<long>() > 0;
	}

	public async ValueTask<SharpPower?> CreatePowerAsync(string name, string alias, bool system,
	string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions,
	CancellationToken cancellationToken = default)
	{
		await ExecuteWithRetryAsync("""
CREATE (p:Power {name: $name, alias: $alias, system: $system, disabled: false,
setPermissions: $setPerms, unsetPermissions: $unsetPerms, typeRestrictions: $typeRestrictions})
""", new
		{
			name,
			alias,
			system,
			setPerms = setPermissions,
			unsetPerms = unsetPermissions,
			typeRestrictions
		}, cancellationToken);

		return new SharpPower
		{
			Id = PowerId(name), Name = name, Alias = alias, System = system,
			SetPermissions = setPermissions, UnsetPermissions = unsetPermissions, TypeRestrictions = typeRestrictions
		};
	}

	public async ValueTask<bool> DeletePowerAsync(string name, CancellationToken cancellationToken = default)
	{
		var power = await GetPowerAsync(name, cancellationToken);
		if (power == null || power.System) return false;

		await ExecuteWithRetryAsync("MATCH (p:Power {name: $name}) DETACH DELETE p", new { name }, cancellationToken);
		return true;
	}

	public async ValueTask<SharpPower?> GetPowerAsync(string name, CancellationToken cancellationToken = default)
	{
		var result = await ExecuteWithRetryAsync("MATCH (p:Power {name: $name}) RETURN p", new { name }, cancellationToken);
		return result.Result.Count > 0 ? MapNodeToPower(result.Result[0]["p"].As<INode>()) : null;
	}

	public async IAsyncEnumerable<SharpPower> GetObjectPowersAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var result = await ExecuteWithRetryAsync("MATCH (p:Power) RETURN p", ct: cancellationToken);
		foreach (var record in result.Result)
			yield return MapNodeToPower(record["p"].As<INode>());
	}

	public async ValueTask<bool> UpdateObjectFlagAsync(string name, string[]? aliases, string symbol,
	string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions,
	CancellationToken cancellationToken = default)
	{
		var flag = await GetObjectFlagAsync(name, cancellationToken);
		if (flag == null || flag.System) return false;

		await ExecuteWithRetryAsync("""
MATCH (f:ObjectFlag {name: $name})
SET f.aliases = $aliases, f.symbol = $symbol,
f.setPermissions = $setPerms, f.unsetPermissions = $unsetPerms,
f.typeRestrictions = $typeRestrictions
""", new
		{
			name,
			aliases = aliases ?? Array.Empty<string>(),
			symbol,
			setPerms = setPermissions,
			unsetPerms = unsetPermissions,
			typeRestrictions
		}, cancellationToken);
		return true;
	}

	public async ValueTask<bool> UpdatePowerAsync(string name, string alias,
	string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions,
	CancellationToken cancellationToken = default)
	{
		var power = await GetPowerAsync(name, cancellationToken);
		if (power == null || power.System) return false;

		await ExecuteWithRetryAsync("""
MATCH (p:Power {name: $name})
SET p.alias = $alias,
p.setPermissions = $setPerms, p.unsetPermissions = $unsetPerms,
p.typeRestrictions = $typeRestrictions
""", new { name, alias, setPerms = setPermissions, unsetPerms = unsetPermissions, typeRestrictions }, cancellationToken);
		return true;
	}

	public async ValueTask<bool> SetObjectFlagDisabledAsync(string name, bool disabled, CancellationToken cancellationToken = default)
	{
		var flag = await GetObjectFlagAsync(name, cancellationToken);
		if (flag == null || flag.System) return false;
		await ExecuteWithRetryAsync("MATCH (f:ObjectFlag {name: $name}) SET f.disabled = $disabled", new { name, disabled }, cancellationToken);
		return true;
	}

	public async ValueTask<bool> SetPowerDisabledAsync(string name, bool disabled, CancellationToken cancellationToken = default)
	{
		var power = await GetPowerAsync(name, cancellationToken);
		if (power == null || power.System) return false;
		await ExecuteWithRetryAsync("MATCH (p:Power {name: $name}) SET p.disabled = $disabled", new { name, disabled }, cancellationToken);
		return true;
	}

	public IAsyncEnumerable<SharpObjectFlag> GetObjectFlagsAsync(string id, string type, CancellationToken cancellationToken = default)
	=> GetObjectFlagsForIdAsync(id, type, cancellationToken);

	#endregion

	#region Parent/Zone Navigation

	public async ValueTask<AnyOptionalSharpObject> GetParentAsync(string id, CancellationToken cancellationToken = default)
	{
		var key = ExtractKey(id);
		var result = await ExecuteWithRetryAsync("MATCH (o:Object {key: $key})-[:HAS_PARENT]->(parent:Object) RETURN parent", new { key }, cancellationToken);
		if (result.Result.Count == 0) return new None();
		return await BuildTypedObjectFromObjectNode(result.Result[0]["parent"].As<INode>(), cancellationToken);
	}

	public async IAsyncEnumerable<SharpObject> GetParentsAsync(string id, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var key = ExtractKey(id);
		var result = await ExecuteWithRetryAsync("MATCH (o:Object {key: $key})-[:HAS_PARENT*1..999]->(parent:Object) RETURN parent", new { key }, cancellationToken);
		foreach (var record in result.Result)
			yield return MapNodeToSharpObject(record["parent"].As<INode>());
	}

	public async ValueTask<bool> IsReachableViaParentOrZoneAsync(AnySharpObject startObject, AnySharpObject targetObject,
	int maxDepth = 100, CancellationToken cancellationToken = default)
	{
		var startKey = startObject.Object().Key;
		var targetKey = targetObject.Object().Key;
		var result = await ExecuteWithRetryAsync("""
MATCH path = (start:Object {key: $startKey})-[:HAS_PARENT|HAS_ZONE*1..100]->(target:Object {key: $targetKey})
RETURN count(path) AS cnt
LIMIT 1
""", new { startKey, targetKey }, cancellationToken);
		return result.Result.Count > 0 && result.Result[0]["cnt"].As<long>() > 0;
	}

	public async IAsyncEnumerable<SharpObject> GetObjectsByZoneAsync(AnySharpObject zone, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var zoneKey = zone.Object().Key;
		var result = await ExecuteWithRetryAsync("MATCH (o:Object)-[:HAS_ZONE]->(z:Object {key: $key}) RETURN o", new { key = zoneKey }, cancellationToken);
		foreach (var record in result.Result)
			yield return MapNodeToSharpObject(record["o"].As<INode>());
	}

	#endregion

	#region Location/Contents/Exits

	public async ValueTask<AnyOptionalSharpContainer> GetLocationAsync(DBRef obj, int depth = 1, CancellationToken cancellationToken = default)
	{
		var baseObject = await GetObjectNodeAsync(obj, cancellationToken);
		if (baseObject.IsNone) return new None();

		var typedId = baseObject.Id()!;
		var key = ExtractKey(typedId);

		var maxHops = depth == -1 ? 999 : depth;
		var cypher = "MATCH path = (start {key: $key})-[:AT_LOCATION*0.." + maxHops + "]->(dest) " +
		"WITH dest, size(path) AS pathLen ORDER BY pathLen DESC LIMIT 1 " +
		"MATCH (dest)-[:IS_OBJECT]->(destObj:Object) RETURN destObj";
		var result = await ExecuteWithRetryAsync(cypher, new { key }, cancellationToken);

		if (result.Result.Count == 0) return new None();

		var destObjNode = result.Result[0]["destObj"].As<INode>();
		var located = await BuildTypedObjectFromObjectNode(destObjNode, cancellationToken);
		return located.Match<AnyOptionalSharpContainer>(
		player => player,
		room => room,
		_ => throw new Exception("Invalid Location: Exit"),
		thing => thing,
		_ => throw new Exception("Invalid Location: None"));
	}

	public async ValueTask<AnySharpContainer> GetLocationAsync(AnySharpObject obj, int depth = 1, CancellationToken cancellationToken = default)
	=> (await GetLocationAsync(obj.Object().DBRef, depth, cancellationToken)).WithoutNone();

	public async ValueTask<AnySharpContainer> GetLocationAsync(string id, int depth = 1, CancellationToken cancellationToken = default)
	{
		var key = ExtractKey(id);
		var maxHops = depth == -1 ? 999 : depth;
		var cypher2 = "MATCH path = (start {key: $key})-[:AT_LOCATION*0.." + maxHops + "]->(dest) " +
		"WITH dest, size(path) AS pathLen ORDER BY pathLen DESC LIMIT 1 " +
		"MATCH (dest)-[:IS_OBJECT]->(destObj:Object) RETURN destObj";
		var result = await ExecuteWithRetryAsync(cypher2, new { key }, cancellationToken);

		var destObjNode = result.Result[0]["destObj"].As<INode>();
		var located = await BuildTypedObjectFromObjectNode(destObjNode, cancellationToken);
		return located.Match<AnySharpContainer>(
		player => player,
		room => room,
		_ => throw new Exception("Invalid Location: Exit"),
		thing => thing,
		_ => throw new Exception("Invalid Location: None"));
	}

	public async IAsyncEnumerable<AnySharpContent> GetContentsAsync(DBRef obj, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var baseObject = await GetObjectNodeAsync(obj, cancellationToken);
		if (baseObject.IsNone) yield break;

		var typedKey = ExtractKey(baseObject.Id()!);
		var result = await ExecuteWithRetryAsync("""
MATCH (content)-[:AT_LOCATION]->(container {key: $key})
MATCH (content)-[:IS_OBJECT]->(contentObj:Object)
RETURN contentObj
""", new { key = typedKey }, cancellationToken);

		foreach (var record in result.Result)
		{
			var contentObjNode = record["contentObj"].As<INode>();
			var contentObj = await BuildTypedObjectFromObjectNode(contentObjNode, cancellationToken);
			yield return contentObj.Match<AnySharpContent>(
			player => player,
			_ => throw new Exception("Room cannot be content"),
			exit => exit,
			thing => thing,
			_ => throw new Exception("None cannot be content"));
		}
	}

	public async IAsyncEnumerable<AnySharpContent> GetContentsAsync(AnySharpContainer node, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var containerKey = ExtractKey(node.Id);
		var result = await ExecuteWithRetryAsync("""
MATCH (content)-[:AT_LOCATION]->(container {key: $key})
MATCH (content)-[:IS_OBJECT]->(contentObj:Object)
RETURN contentObj
""", new { key = containerKey }, cancellationToken);

		foreach (var record in result.Result)
		{
			var contentObjNode = record["contentObj"].As<INode>();
			var contentObj = await BuildTypedObjectFromObjectNode(contentObjNode, cancellationToken);
			yield return contentObj.Match<AnySharpContent>(
			player => player,
			_ => throw new Exception("Room cannot be content"),
			exit => exit,
			thing => thing,
			_ => throw new Exception("None cannot be content"));
		}
	}

	public async IAsyncEnumerable<SharpExit> GetExitsAsync(DBRef obj, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var baseObject = await GetObjectNodeAsync(obj, cancellationToken);
		if (baseObject.IsNone) yield break;

		var containerKey = ExtractKey(baseObject.Known.Id()!);
		var result = await ExecuteWithRetryAsync("""
MATCH (e:Exit)-[:AT_LOCATION]->(container {key: $key})
MATCH (e)-[:IS_OBJECT]->(o:Object {type: 'EXIT'})
RETURN o, e
""", new { key = containerKey }, cancellationToken);

		foreach (var record in result.Result)
		{
			var objNode = record["o"].As<INode>();
			var exitNode = record["e"].As<INode>();
			var sharpObj = MapNodeToSharpObject(objNode);
			var key = objNode["key"].As<int>();
			yield return BuildExit(ExitId(key), exitNode, sharpObj);
		}
	}

	public async IAsyncEnumerable<SharpExit> GetExitsAsync(AnySharpContainer node, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var containerKey = ExtractKey(node.Id);
		var result = await ExecuteWithRetryAsync("""
MATCH (e:Exit)-[:AT_LOCATION]->(container {key: $key})
MATCH (e)-[:IS_OBJECT]->(o:Object {type: 'EXIT'})
RETURN o, e
""", new { key = containerKey }, cancellationToken);

		foreach (var record in result.Result)
		{
			var objNode = record["o"].As<INode>();
			var exitNode = record["e"].As<INode>();
			var sharpObj = MapNodeToSharpObject(objNode);
			var key = objNode["key"].As<int>();
			yield return BuildExit(ExitId(key), exitNode, sharpObj);
		}
	}

	public async ValueTask MoveObjectAsync(AnySharpContent enactorObj, AnySharpContainer destination, CancellationToken cancellationToken = default)
	{
		var srcKey = ExtractKey(enactorObj.Id);
		var destKey = ExtractKey(destination.Id);
		await ExecuteWithRetryAsync("""
MATCH (src {key: $srcKey})-[r:AT_LOCATION]->()
DELETE r
WITH src
MATCH (dest {key: $destKey})
WHERE dest:Room OR dest:Player OR dest:Thing
CREATE (src)-[:AT_LOCATION]->(dest)
""", new { srcKey, destKey }, cancellationToken);
	}

	public async IAsyncEnumerable<AnySharpObject> GetNearbyObjectsAsync(DBRef obj, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var self = (await GetObjectNodeAsync(obj, cancellationToken)).WithoutNone();
		var location = await self.Where();

		yield return self;

		await foreach (var item in GetContentsAsync(self.Object().DBRef, cancellationToken))
			yield return item.WithRoomOption();

		await foreach (var item in GetContentsAsync(location.Object().DBRef, cancellationToken))
			yield return item.WithRoomOption();
	}

	public async IAsyncEnumerable<AnySharpObject> GetNearbyObjectsAsync(AnySharpObject obj, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var location = await obj.Where();

		yield return obj;

		await foreach (var item in GetContentsAsync(obj.Object().DBRef, cancellationToken))
			yield return item.WithRoomOption();

		await foreach (var item in GetContentsAsync(location.Object().DBRef, cancellationToken))
			yield return item.WithRoomOption();
	}

	#endregion

	#region Attributes

	public async IAsyncEnumerable<SharpAttribute> GetAttributeAsync(DBRef dbref, string[] attribute, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		attribute = attribute.Select(x => x.ToUpper()).ToArray();
		var objKey = dbref.Number;

		// Find the typed node for this object
		var typedResult = await ExecuteWithRetryAsync("MATCH (typed)-[:IS_OBJECT]->(o:Object {key: $key}) RETURN typed.key AS tkey", new { key = objKey }, cancellationToken);

		if (typedResult.Result.Count == 0) yield break;

		var tkey = typedResult.Result[0]["tkey"].As<int>();

		// Walk the attribute tree step by step
		var attrs = new List<INode>();
		var currentKey = (object)tkey;
		var isFirst = true;

		foreach (var attrName in attribute)
		{
			string cypher;
			if (isFirst)
			{
				cypher = "MATCH (parent {key: toInteger($parentKey)})-[:HAS_ATTRIBUTE]->(child:Attribute {name: $attrName}) RETURN child";
				isFirst = false;
			}
			else
			{
				cypher = "MATCH (parent:Attribute {key: $parentKey})-[:HAS_ATTRIBUTE]->(child:Attribute {name: $attrName}) RETURN child";
			}

			var stepResult = await ExecuteWithRetryAsync(cypher, new { parentKey = currentKey.ToString(), attrName }, cancellationToken);

			if (stepResult.Result.Count == 0) yield break;

			var childNode = stepResult.Result[0]["child"].As<INode>();
			attrs.Add(childNode);
			currentKey = childNode["key"].As<string>();
		}

		if (attrs.Count != attribute.Length) yield break;

		foreach (var node in attrs)
		{
			yield return await MapToSharpAttribute(node, cancellationToken);
		}
	}

	public async IAsyncEnumerable<SharpAttribute> GetAttributesAsync(DBRef dbref, string attributePattern, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var objKey = dbref.Number;
		var typedResult = await ExecuteWithRetryAsync("MATCH (typed)-[:IS_OBJECT]->(o:Object {key: $key}) RETURN typed.key AS tkey", new { key = objKey }, cancellationToken);

		if (typedResult.Result.Count == 0) yield break;
		var tkey = typedResult.Result[0]["tkey"].As<int>();

		var pattern = WildcardToRegex().Replace(attributePattern, m => m.Value switch
		{
			"**" => ".*",
			"*" => "[^`]*",
			"?" => ".",
			_ => $"\\{m.Value}"
		});

		var result = await ExecuteWithRetryAsync("""
MATCH (start {key: $tkey})-[:HAS_ATTRIBUTE*1..999]->(child:Attribute)
WHERE toLower(child.longName) =~ $pattern
RETURN child
""", new { tkey, pattern = $"^{pattern.ToLower()}$" }, cancellationToken);

		foreach (var record in result.Result)
		{
			yield return await MapToSharpAttribute(record["child"].As<INode>(), cancellationToken);
		}
	}

	public async IAsyncEnumerable<SharpAttribute> GetAttributesByRegexAsync(DBRef dbref, string attributePattern, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var objKey = dbref.Number;
		var typedResult = await ExecuteWithRetryAsync("MATCH (typed)-[:IS_OBJECT]->(o:Object {key: $key}) RETURN typed.key AS tkey", new { key = objKey }, cancellationToken);

		if (typedResult.Result.Count == 0) yield break;
		var tkey = typedResult.Result[0]["tkey"].As<int>();

		var result = await ExecuteWithRetryAsync("""
MATCH (start {key: $tkey})-[:HAS_ATTRIBUTE*1..999]->(child:Attribute)
WHERE toLower(child.longName) =~ $pattern
RETURN child ORDER BY child.longName
""", new { tkey, pattern = ToFullMatchRegex(attributePattern.ToLower()) }, cancellationToken);

		foreach (var record in result.Result)
		{
			yield return await MapToSharpAttribute(record["child"].As<INode>(), cancellationToken);
		}
	}

	public async IAsyncEnumerable<LazySharpAttribute> GetLazyAttributeAsync(DBRef dbref, string[] attribute, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		attribute = attribute.Select(x => x.ToUpper()).ToArray();
		var objKey = dbref.Number;

		var typedResult = await ExecuteWithRetryAsync("MATCH (typed)-[:IS_OBJECT]->(o:Object {key: $key}) RETURN typed.key AS tkey", new { key = objKey }, cancellationToken);

		if (typedResult.Result.Count == 0) yield break;
		var tkey = typedResult.Result[0]["tkey"].As<int>();

		var attrs = new List<INode>();
		var currentKey = (object)tkey;
		var isFirst = true;

		foreach (var attrName in attribute)
		{
			string cypher;
			if (isFirst)
			{
				cypher = "MATCH (parent {key: toInteger($parentKey)})-[:HAS_ATTRIBUTE]->(child:Attribute {name: $attrName}) RETURN child";
				isFirst = false;
			}
			else
			{
				cypher = "MATCH (parent:Attribute {key: $parentKey})-[:HAS_ATTRIBUTE]->(child:Attribute {name: $attrName}) RETURN child";
			}

			var stepResult = await ExecuteWithRetryAsync(cypher, new { parentKey = currentKey.ToString(), attrName }, cancellationToken);

			if (stepResult.Result.Count == 0) yield break;

			var childNode = stepResult.Result[0]["child"].As<INode>();
			attrs.Add(childNode);
			currentKey = childNode["key"].As<string>();
		}

		foreach (var node in attrs)
		{
			yield return await MapToLazySharpAttribute(node, cancellationToken);
		}
	}

	public async IAsyncEnumerable<LazySharpAttribute> GetLazyAttributesAsync(DBRef dbref, string attributePattern, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var objKey = dbref.Number;
		var typedResult = await ExecuteWithRetryAsync("MATCH (typed)-[:IS_OBJECT]->(o:Object {key: $key}) RETURN typed.key AS tkey", new { key = objKey }, cancellationToken);

		if (typedResult.Result.Count == 0) yield break;
		var tkey = typedResult.Result[0]["tkey"].As<int>();

		var pattern = WildcardToRegex().Replace(attributePattern, m => m.Value switch
		{
			"**" => ".*",
			"*" => "[^`]*",
			"?" => ".",
			_ => $"\\{m.Value}"
		});

		var result = await ExecuteWithRetryAsync("""
MATCH (start {key: $tkey})-[:HAS_ATTRIBUTE]->(child:Attribute)
WHERE toLower(child.longName) =~ $pattern
RETURN child
""", new { tkey, pattern = $"^{pattern.ToLower()}$" }, cancellationToken);

		foreach (var record in result.Result)
		{
			yield return await MapToLazySharpAttribute(record["child"].As<INode>(), cancellationToken);
		}
	}

	public async IAsyncEnumerable<LazySharpAttribute> GetLazyAttributesByRegexAsync(DBRef dbref, string attributePattern, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var objKey = dbref.Number;
		var typedResult = await ExecuteWithRetryAsync("MATCH (typed)-[:IS_OBJECT]->(o:Object {key: $key}) RETURN typed.key AS tkey", new { key = objKey }, cancellationToken);

		if (typedResult.Result.Count == 0) yield break;
		var tkey = typedResult.Result[0]["tkey"].As<int>();

		var result = await ExecuteWithRetryAsync("""
MATCH (start {key: $tkey})-[:HAS_ATTRIBUTE]->(child:Attribute)
WHERE toLower(child.longName) =~ $pattern
RETURN child ORDER BY child.longName
""", new { tkey, pattern = ToFullMatchRegex(attributePattern.ToLower()) }, cancellationToken);

		foreach (var record in result.Result)
		{
			yield return await MapToLazySharpAttribute(record["child"].As<INode>(), cancellationToken);
		}
	}

	public async ValueTask<bool> SetAttributeAsync(DBRef dbref, string[] attribute, MString value, SharpPlayer owner, CancellationToken cancellationToken = default)
	{
		return await SetAttributeAsyncCore(dbref, attribute, value, owner, cancellationToken);
	}

	private async ValueTask<bool> SetAttributeAsyncCore(DBRef dbref, string[] attribute, MString value, SharpPlayer owner, CancellationToken cancellationToken = default)
	{
		attribute = attribute.Select(x => x.ToUpper()).ToArray();
		var objKey = dbref.Number;
		var ownerKey = ExtractKey(owner.Id!);
		var serializedValue = MarkupStringModule.serialize(value);
		var emptyValue = "";

		// Build a single atomic MERGE query for the entire attribute path.
		// MERGE creates nodes/relationships if they don't exist, matches if they do.
		// This eliminates MVCC conflicts from multi-query read-then-write patterns.
		var sb = new System.Text.StringBuilder();
		var parameters = new Dictionary<string, object?>
		{
			["objKey"] = objKey,
			["ownerKey"] = ownerKey,
			["value"] = serializedValue,
			["emptyValue"] = emptyValue
		};

		// Start: find the typed node (Player/Room/Thing/Exit) for this object
		sb.AppendLine("MATCH (typed)-[:IS_OBJECT]->(o:Object {key: $objKey})");

		// Build MERGE chain for each attribute level
		for (var i = 0; i < attribute.Length; i++)
		{
			var parentAlias = i == 0 ? "typed" : $"a{i - 1}";
			var childAlias = $"a{i}";
			var nameParam = $"name{i}";
			var keyParam = $"key{i}";
			var longParam = $"long{i}";
			var isLast = i == attribute.Length - 1;

			var longName = string.Join('`', attribute.Take(i + 1));
			var attrKey = $"{objKey}_{longName}";

			parameters[nameParam] = attribute[i];
			parameters[keyParam] = attrKey;
			parameters[longParam] = longName;

			sb.AppendLine($"MERGE ({parentAlias})-[:HAS_ATTRIBUTE]->({childAlias}:Attribute {{name: ${nameParam}}})");
			if (isLast)
			{
				sb.AppendLine($"ON CREATE SET {childAlias}.key = ${keyParam}, {childAlias}.longName = ${longParam}, {childAlias}.value = $value");
				sb.AppendLine($"ON MATCH SET {childAlias}.value = $value");
			}
			else
			{
				sb.AppendLine($"ON CREATE SET {childAlias}.key = ${keyParam}, {childAlias}.longName = ${longParam}, {childAlias}.value = $emptyValue");
			}
		}

		var leafAlias = $"a{attribute.Length - 1}";

		// Remove old owner and set new owner atomically
		sb.AppendLine($"WITH {leafAlias}");
		sb.AppendLine($"OPTIONAL MATCH ({leafAlias})-[oldOwner:HAS_ATTRIBUTE_OWNER]->()");
		sb.AppendLine("DELETE oldOwner");
		sb.AppendLine($"WITH {leafAlias}");
		sb.AppendLine($"MATCH (p:Player {{key: $ownerKey}})");
		sb.AppendLine($"CREATE ({leafAlias})-[:HAS_ATTRIBUTE_OWNER]->(p)");
		sb.AppendLine($"RETURN {leafAlias}.key AS leafKey");

		var result = await ExecuteWithRetryAsync(sb.ToString(), parameters, cancellationToken);
		if (result.Result.Count == 0) return false;

		// Handle attribute entry flags for newly created attributes (post-MERGE)
		// Check each level for attribute entries with default flags
		for (var i = 0; i < attribute.Length; i++)
		{
			var longName = string.Join('`', attribute.Take(i + 1));
			var attrKey = $"{objKey}_{longName}";
			var attrEntry = await GetSharpAttributeEntry(longName, cancellationToken);
			var flagNames = attrEntry?.DefaultFlags ?? [];

			foreach (var flagName in flagNames)
			{
				// MERGE to avoid duplicate flag relationships
				await ExecuteWithRetryAsync("""
		MATCH (a:Attribute {key: $key}), (f:AttributeFlag)
		WHERE toUpper(f.name) = toUpper($fname)
		MERGE (a)-[:HAS_ATTRIBUTE_FLAG]->(f)
		""", new { key = attrKey, fname = flagName }, cancellationToken);
			}
		}

		return true;
	}

	public async ValueTask<bool> SetAttributeFlagAsync(SharpObject dbref, string[] attribute, SharpAttributeFlag flag, CancellationToken cancellationToken = default)
	{
		var attrs = GetAttributeAsync(dbref.DBRef, attribute, cancellationToken);
		var attr = await attrs.LastOrDefaultAsync(cancellationToken);
		if (attr is null) return false;
		await SetAttributeFlagAsync(attr, flag, cancellationToken);
		return true;
	}

	public async ValueTask SetAttributeFlagAsync(SharpAttribute attr, SharpAttributeFlag flag, CancellationToken cancellationToken = default)
	{
		var attrKey = ExtractKeyString(attr.Id);
		await ExecuteWithRetryAsync("""
MATCH (a:Attribute {key: $key}), (f:AttributeFlag {name: $fname})
CREATE (a)-[:HAS_ATTRIBUTE_FLAG]->(f)
""", new { key = attrKey, fname = flag.Name }, cancellationToken);
	}

	public async ValueTask<bool> UnsetAttributeFlagAsync(SharpObject dbref, string[] attribute, SharpAttributeFlag flag, CancellationToken cancellationToken = default)
	{
		var attrs = GetAttributeAsync(dbref.DBRef, attribute, cancellationToken);
		var attr = await attrs.LastOrDefaultAsync(cancellationToken);
		if (attr is null) return false;
		await UnsetAttributeFlagAsync(attr, flag, cancellationToken);
		return true;
	}

	public async ValueTask UnsetAttributeFlagAsync(SharpAttribute attr, SharpAttributeFlag flag, CancellationToken cancellationToken = default)
	{
		var attrKey = ExtractKeyString(attr.Id);
		await ExecuteWithRetryAsync("""
MATCH (a:Attribute {key: $key})-[r:HAS_ATTRIBUTE_FLAG]->(f:AttributeFlag {name: $fname})
DELETE r
""", new { key = attrKey, fname = flag.Name }, cancellationToken);
	}

	public async ValueTask<SharpAttributeFlag?> GetAttributeFlagAsync(string flagName, CancellationToken cancellationToken = default)
	{
		var result = await ExecuteWithRetryAsync("MATCH (f:AttributeFlag) WHERE toUpper(f.name) = toUpper($name) RETURN f", new { name = flagName }, cancellationToken);
		return result.Result.Count > 0 ? MapNodeToAttributeFlag(result.Result[0]["f"].As<INode>()) : null;
	}

	public async IAsyncEnumerable<SharpAttributeFlag> GetAttributeFlagsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var result = await ExecuteWithRetryAsync("MATCH (f:AttributeFlag) RETURN f", ct: cancellationToken);
		foreach (var record in result.Result)
			yield return MapNodeToAttributeFlag(record["f"].As<INode>());
	}

	public async ValueTask<bool> ClearAttributeAsync(DBRef dbref, string[] attribute, CancellationToken cancellationToken = default)
	{
		attribute = attribute.Select(x => x.ToUpper()).ToArray();
		var attrs = GetAttributeAsync(dbref, attribute, cancellationToken);
		var targetAttr = await attrs.LastOrDefaultAsync(cancellationToken);
		if (targetAttr is null) return false;

		var attrKey = ExtractKeyString(targetAttr.Id);

		// Check for children
		var childrenResult = await ExecuteWithRetryAsync("""
MATCH (a:Attribute {key: $key})-[:HAS_ATTRIBUTE]->(child:Attribute)
RETURN count(child) AS cnt
""", new { key = attrKey }, cancellationToken);

		var hasChildren = childrenResult.Result.Count > 0 && childrenResult.Result[0]["cnt"].As<long>() > 0;

		if (hasChildren)
		{
			await ExecuteWithRetryAsync("MATCH (a:Attribute {key: $key}) SET a.value = $value", new { key = attrKey, value = MarkupStringModule.serialize(MarkupStringModule.empty()) }, cancellationToken);
		}
		else
		{
			await ExecuteWithRetryAsync("MATCH (a:Attribute {key: $key}) DETACH DELETE a", new { key = attrKey }, cancellationToken);
		}

		return true;
	}

	public async ValueTask<bool> WipeAttributeAsync(DBRef dbref, string[] attribute, CancellationToken cancellationToken = default)
	{
		attribute = attribute.Select(x => x.ToUpper()).ToArray();
		var attrs = GetAttributeAsync(dbref, attribute, cancellationToken);
		var targetAttr = await attrs.LastOrDefaultAsync(cancellationToken);
		if (targetAttr is null) return false;

		var attrKey = ExtractKeyString(targetAttr.Id);

		// Delete all descendants first
		await ExecuteWithRetryAsync("""
MATCH (a:Attribute {key: $key})-[:HAS_ATTRIBUTE*1..999]->(descendant:Attribute)
DETACH DELETE descendant
""", new { key = attrKey }, cancellationToken);

		// Delete the target itself
		await ExecuteWithRetryAsync("MATCH (a:Attribute {key: $key}) DETACH DELETE a", new { key = attrKey }, cancellationToken);

		return true;
	}

	#endregion

	#region Attribute Entries

	public async IAsyncEnumerable<SharpAttributeEntry> GetAllAttributeEntriesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var result = await ExecuteWithRetryAsync("MATCH (e:AttributeEntry) RETURN e", ct: cancellationToken);
		foreach (var record in result.Result)
			yield return MapNodeToAttributeEntry(record["e"].As<INode>());
	}

	public async ValueTask<SharpAttributeEntry?> GetSharpAttributeEntry(string name, CancellationToken ct = default)
	{
		var result = await ExecuteWithRetryAsync("MATCH (e:AttributeEntry {name: $name}) RETURN e", new { name }, ct);
		return result.Result.Count > 0 ? MapNodeToAttributeEntry(result.Result[0]["e"].As<INode>()) : null;
	}

	public async ValueTask<SharpAttributeEntry?> CreateOrUpdateAttributeEntryAsync(string name, string[] defaultFlags,
	string? limit = null, string[]? enumValues = null, CancellationToken cancellationToken = default)
	{
		await ExecuteWithRetryAsync("""
MERGE (e:AttributeEntry {name: $name})
SET e.defaultFlags = $defaultFlags, e.lim = $lim, e.enumValues = $enumValues
""", new
		{
			name,
			defaultFlags,
			lim = limit ?? "",
			enumValues = enumValues ?? Array.Empty<string>()
		}, cancellationToken);

		return await GetSharpAttributeEntry(name, cancellationToken);
	}

	public async ValueTask<bool> DeleteAttributeEntryAsync(string name, CancellationToken cancellationToken = default)
	{
		var existing = await GetSharpAttributeEntry(name, cancellationToken);
		if (existing == null) return false;

		await ExecuteWithRetryAsync("MATCH (e:AttributeEntry {name: $name}) DETACH DELETE e", new { name }, cancellationToken);
		return true;
	}

	#endregion

	#region Attribute Inheritance

	public async IAsyncEnumerable<AttributeWithInheritance> GetAttributeWithInheritanceAsync(
	DBRef dbref, string[] attribute, bool checkParent = true,
	[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		attribute = attribute.Select(x => x.ToUpper()).ToArray();

		// Try self first
		var selfAttrs = await GetAttributeAsync(dbref, attribute, cancellationToken).ToArrayAsync(cancellationToken);
		if (selfAttrs.Length == attribute.Length)
		{
			var lastAttr = selfAttrs.Last();
			yield return new AttributeWithInheritance(selfAttrs, dbref, AttributeSource.Self, lastAttr.Flags);
			yield break;
		}

		if (!checkParent) yield break;

		// Try parents
		var objKey = dbref.Number;
		var parentResult = await ExecuteWithRetryAsync("MATCH (o:Object {key: $key})-[:HAS_PARENT*1..100]->(parent:Object) RETURN parent", new { key = objKey }, cancellationToken);

		foreach (var record in parentResult.Result)
		{
			var parentNode = record["parent"].As<INode>();
			var parentKey = parentNode["key"].As<int>();
			var parentDbRef = new DBRef(parentKey);
			var parentAttrs = await GetAttributeAsync(parentDbRef, attribute, cancellationToken).ToArrayAsync(cancellationToken);
			if (parentAttrs.Length == attribute.Length)
			{
				var lastAttr = parentAttrs.Last();
				var flags = lastAttr.Flags.Where(f => f.Inheritable);
				yield return new AttributeWithInheritance(parentAttrs, parentDbRef, AttributeSource.Parent, flags);
				yield break;
			}
		}

		// Try zones (on self and parents)
		var chainResult = await ExecuteWithRetryAsync("""
MATCH (o:Object {key: $key})
OPTIONAL MATCH (o)-[:HAS_PARENT*0..100]->(chainObj:Object)
WITH DISTINCT chainObj
MATCH (chainObj)-[:HAS_ZONE]->(zone:Object)
RETURN zone
""", new { key = objKey }, cancellationToken);

		foreach (var record in chainResult.Result)
		{
			var zoneNode = record["zone"].As<INode>();
			var zoneKey = zoneNode["key"].As<int>();
			var zoneDbRef = new DBRef(zoneKey);
			var zoneAttrs = await GetAttributeAsync(zoneDbRef, attribute, cancellationToken).ToArrayAsync(cancellationToken);
			if (zoneAttrs.Length == attribute.Length)
			{
				var lastAttr = zoneAttrs.Last();
				var flags = lastAttr.Flags.Where(f => f.Inheritable);
				yield return new AttributeWithInheritance(zoneAttrs, zoneDbRef, AttributeSource.Zone, flags);
				yield break;
			}
		}
	}

	public async IAsyncEnumerable<LazyAttributeWithInheritance> GetLazyAttributeWithInheritanceAsync(
	DBRef dbref, string[] attribute, bool checkParent = true,
	[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		attribute = attribute.Select(x => x.ToUpper()).ToArray();

		var selfAttrs = await GetLazyAttributeAsync(dbref, attribute, cancellationToken).ToArrayAsync(cancellationToken);
		if (selfAttrs.Length == attribute.Length)
		{
			var lastAttr = selfAttrs.Last();
			yield return new LazyAttributeWithInheritance(selfAttrs, dbref, AttributeSource.Self, lastAttr.Flags);
			yield break;
		}

		if (!checkParent) yield break;

		var objKey = dbref.Number;
		var parentResult = await ExecuteWithRetryAsync("MATCH (o:Object {key: $key})-[:HAS_PARENT*1..100]->(parent:Object) RETURN parent", new { key = objKey }, cancellationToken);

		foreach (var record in parentResult.Result)
		{
			var parentNode = record["parent"].As<INode>();
			var parentKey = parentNode["key"].As<int>();
			var parentDbRef = new DBRef(parentKey);
			var parentAttrs = await GetLazyAttributeAsync(parentDbRef, attribute, cancellationToken).ToArrayAsync(cancellationToken);
			if (parentAttrs.Length == attribute.Length)
			{
				var lastAttr = parentAttrs.Last();
				var flags = lastAttr.Flags.Where(f => f.Inheritable);
				yield return new LazyAttributeWithInheritance(parentAttrs, parentDbRef, AttributeSource.Parent, flags);
				yield break;
			}
		}

		var chainResult = await ExecuteWithRetryAsync("""
MATCH (o:Object {key: $key})
OPTIONAL MATCH (o)-[:HAS_PARENT*0..100]->(chainObj:Object)
WITH DISTINCT chainObj
MATCH (chainObj)-[:HAS_ZONE]->(zone:Object)
RETURN zone
""", new { key = objKey }, cancellationToken);

		foreach (var record in chainResult.Result)
		{
			var zoneNode = record["zone"].As<INode>();
			var zoneKey = zoneNode["key"].As<int>();
			var zoneDbRef = new DBRef(zoneKey);
			var zoneAttrs = await GetLazyAttributeAsync(zoneDbRef, attribute, cancellationToken).ToArrayAsync(cancellationToken);
			if (zoneAttrs.Length == attribute.Length)
			{
				var lastAttr = zoneAttrs.Last();
				var flags = lastAttr.Flags.Where(f => f.Inheritable);
				yield return new LazyAttributeWithInheritance(zoneAttrs, zoneDbRef, AttributeSource.Zone, flags);
				yield break;
			}
		}
	}

	#endregion

	#region Mail

	public async IAsyncEnumerable<SharpMail> GetIncomingMailsAsync(SharpPlayer id, string folder, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var playerKey = ExtractKey(id.Id!);
		var result = await ExecuteWithRetryAsync("""
MATCH (p:Player {key: $key})-[:RECEIVED_MAIL]->(m:Mail {folder: $folder})
RETURN m
""", new { key = playerKey, folder }, cancellationToken);

		foreach (var record in result.Result)
			yield return MapNodeToMail(record["m"].As<INode>());
	}

	public async IAsyncEnumerable<SharpMail> GetAllIncomingMailsAsync(SharpPlayer id, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var playerKey = ExtractKey(id.Id!);
		var result = await ExecuteWithRetryAsync("MATCH (p:Player {key: $key})-[:RECEIVED_MAIL]->(m:Mail) RETURN m", new { key = playerKey }, cancellationToken);

		foreach (var record in result.Result)
			yield return MapNodeToMail(record["m"].As<INode>());
	}

	public async ValueTask<SharpMail?> GetIncomingMailAsync(SharpPlayer id, string folder, int mail, CancellationToken cancellationToken = default)
	{
		var playerKey = ExtractKey(id.Id!);
		var result = await ExecuteWithRetryAsync("""
MATCH (p:Player {key: $key})-[:RECEIVED_MAIL]->(m:Mail {folder: $folder})
RETURN m
SKIP $skip LIMIT 1
""", new { key = playerKey, folder, skip = mail }, cancellationToken);

		return result.Result.Count > 0 ? MapNodeToMail(result.Result[0]["m"].As<INode>()) : null;
	}

	public async IAsyncEnumerable<SharpMail> GetSentMailsAsync(SharpObject sender, SharpPlayer recipient, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var senderKey = sender.Key;
		var recipientKey = ExtractKey(recipient.Id!);
		var result = await ExecuteWithRetryAsync("""
MATCH (p:Player {key: $recipientKey})-[:RECEIVED_MAIL]->(m:Mail)-[:SENT_MAIL]->(sObj:Object {key: $senderKey})
RETURN m
""", new { senderKey, recipientKey }, cancellationToken);

		foreach (var record in result.Result)
			yield return MapNodeToMail(record["m"].As<INode>());
	}

	public async IAsyncEnumerable<SharpMail> GetAllSentMailsAsync(SharpObject sender, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var senderKey = sender.Key;
		var result = await ExecuteWithRetryAsync("MATCH (m:Mail)-[:SENT_MAIL]->(sObj:Object {key: $key}) RETURN m", new { key = senderKey }, cancellationToken);

		foreach (var record in result.Result)
			yield return MapNodeToMail(record["m"].As<INode>());
	}

	public async ValueTask<SharpMail?> GetSentMailAsync(SharpObject sender, SharpPlayer recipient, int mail, CancellationToken cancellationToken = default)
	{
		var senderKey = sender.Key;
		var recipientKey = ExtractKey(recipient.Id!);
		var result = await ExecuteWithRetryAsync("""
MATCH (p:Player {key: $recipientKey})-[:RECEIVED_MAIL]->(m:Mail)-[:SENT_MAIL]->(sObj:Object {key: $senderKey})
RETURN m
SKIP $skip LIMIT 1
""", new { senderKey, recipientKey, skip = mail }, cancellationToken);

		return result.Result.Count > 0 ? MapNodeToMail(result.Result[0]["m"].As<INode>()) : null;
	}

	public async ValueTask<string[]> GetMailFoldersAsync(SharpPlayer id, CancellationToken cancellationToken = default)
	{
		var playerKey = ExtractKey(id.Id!);
		var result = await ExecuteWithRetryAsync("""
MATCH (p:Player {key: $key})-[:RECEIVED_MAIL]->(m:Mail)
RETURN DISTINCT m.folder AS folder
""", new { key = playerKey }, cancellationToken);

		return result.Result.Select(r => r["folder"].As<string>()).ToArray();
	}

	public async ValueTask SendMailAsync(SharpObject from, SharpPlayer to, SharpMail mail, CancellationToken cancellationToken = default)
	{
		var fromKey = from.Key;
		var toKey = ExtractKey(to.Id!);
		var mailKey = Guid.NewGuid().ToString("N");

		await ExecuteWithRetryAsync("""
CREATE (m:Mail {key: $mailKey, dateSent: $dateSent, fresh: $fresh, read: $read, tagged: $tagged,
urgent: $urgent, forwarded: $forwarded, cleared: $cleared, folder: $folder,
content: $content, subject: $subject})
""", new
		{
			mailKey,
			dateSent = mail.DateSent.ToUnixTimeMilliseconds(),
			fresh = mail.Fresh,
			read = mail.Read,
			tagged = mail.Tagged,
			urgent = mail.Urgent,
			forwarded = mail.Forwarded,
			cleared = mail.Cleared,
			folder = mail.Folder,
			content = MarkupStringModule.serialize(mail.Content),
			subject = MarkupStringModule.serialize(mail.Subject)
		}, cancellationToken);

		// RECEIVED_MAIL: Player -> Mail
		await ExecuteWithRetryAsync("""
MATCH (p:Player {key: $toKey}), (m:Mail {key: $mailKey})
CREATE (p)-[:RECEIVED_MAIL]->(m)
""", new { toKey, mailKey }, cancellationToken);

		// SENT_MAIL: Mail -> Object (sender's Object node)
		await ExecuteWithRetryAsync("""
MATCH (m:Mail {key: $mailKey}), (o:Object {key: $fromKey})
CREATE (m)-[:SENT_MAIL]->(o)
""", new { mailKey, fromKey }, cancellationToken);
	}

	public async ValueTask UpdateMailAsync(string mailId, MailUpdate commandMail, CancellationToken cancellationToken = default)
	{
		var mailKey = ExtractKeyString(mailId);
		switch (commandMail)
		{
			case { IsReadEdit: true }:
				await ExecuteWithRetryAsync("MATCH (m:Mail {key: $key}) SET m.read = $val, m.fresh = false", new { key = mailKey, val = commandMail.AsReadEdit }, cancellationToken);
				return;
			case { IsClearEdit: true }:
				await ExecuteWithRetryAsync("MATCH (m:Mail {key: $key}) SET m.cleared = $val", new { key = mailKey, val = commandMail.AsClearEdit }, cancellationToken);
				return;
			case { IsTaggedEdit: true }:
				await ExecuteWithRetryAsync("MATCH (m:Mail {key: $key}) SET m.tagged = $val", new { key = mailKey, val = commandMail.AsTaggedEdit }, cancellationToken);
				return;
			case { IsUrgentEdit: true }:
				await ExecuteWithRetryAsync("MATCH (m:Mail {key: $key}) SET m.urgent = $val", new { key = mailKey, val = commandMail.AsUrgentEdit }, cancellationToken);
				return;
		}
	}

	public async ValueTask DeleteMailAsync(string mailId, CancellationToken cancellationToken = default)
	{
		var mailKey = ExtractKeyString(mailId);
		await ExecuteWithRetryAsync("MATCH (m:Mail {key: $key}) DETACH DELETE m", new { key = mailKey }, cancellationToken);
	}

	public async ValueTask RenameMailFolderAsync(SharpPlayer player, string folder, string newFolder, CancellationToken cancellationToken = default)
	{
		var playerKey = ExtractKey(player.Id!);
		await ExecuteWithRetryAsync("""
MATCH (p:Player {key: $key})-[:RECEIVED_MAIL]->(m:Mail {folder: $folder})
SET m.folder = $newFolder
""", new { key = playerKey, folder, newFolder }, cancellationToken);
	}

	public async ValueTask MoveMailFolderAsync(string mailId, string newFolder, CancellationToken cancellationToken = default)
	{
		var mailKey = ExtractKeyString(mailId);
		await ExecuteWithRetryAsync("MATCH (m:Mail {key: $key}) SET m.folder = $newFolder", new { key = mailKey, newFolder }, cancellationToken);
	}

	public async IAsyncEnumerable<SharpMail> GetAllSystemMailAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var result = await ExecuteWithRetryAsync("MATCH (m:Mail) RETURN m", ct: cancellationToken);
		foreach (var record in result.Result)
			yield return MapNodeToMail(record["m"].As<INode>());
	}

	private SharpMail MapNodeToMail(INode node)
	{
		var mailKey = node["key"].As<string>();
		return new SharpMail
		{
			Id = MailId(mailKey),
			DateSent = DateTimeOffset.FromUnixTimeMilliseconds(node["dateSent"].As<long>()),
			Fresh = node["fresh"].As<bool>(),
			Read = node["read"].As<bool>(),
			Tagged = node["tagged"].As<bool>(),
			Urgent = node["urgent"].As<bool>(),
			Forwarded = node["forwarded"].As<bool>(),
			Cleared = node["cleared"].As<bool>(),
			Folder = node["folder"].As<string>(),
			Content = MarkupStringModule.deserialize(node["content"].As<string>()),
			Subject = MarkupStringModule.deserialize(node["subject"].As<string>()),
			From = new AsyncLazy<AnyOptionalSharpObject>(async ct => await MailFromAsync(mailKey, ct))
		};
	}

	private async ValueTask<AnyOptionalSharpObject> MailFromAsync(string mailKey, CancellationToken ct)
	{
		var result = await ExecuteWithRetryAsync("MATCH (m:Mail {key: $key})-[:SENT_MAIL]->(o:Object) RETURN o", new { key = mailKey }, ct);

		if (result.Result.Count == 0) return new None();
		var objNode = result.Result[0]["o"].As<INode>();
		return await BuildTypedObjectFromObjectNode(objNode, ct);
	}

	#endregion

	#region Expanded Data

	public async ValueTask SetExpandedObjectData(string sharpObjectId, string dataType, dynamic data, CancellationToken cancellationToken = default)
	{
		var objKey = ExtractKey(sharpObjectId);

		// Check if node exists
		var existing = await ExecuteWithRetryAsync("""
MATCH (o:Object {key: $key})-[:HAS_EXPANDED_DATA]->(d:ExpandedObjectData {sharpObjectId: $objId, dataType: $dataType})
RETURN d.data AS data
""", new { key = objKey, objId = sharpObjectId, dataType }, cancellationToken);

		string jsonData;
		if (existing.Result.Count > 0)
		{
			// Merge with existing data: non-null values from new data override existing
			var existingJson = existing.Result[0]["data"].As<string>();
			var existingDoc = JsonSerializer.Deserialize<JsonElement>(existingJson, JsonOptions);
			var newDoc = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize((object)data, JsonOptions), JsonOptions);

			var merged = new Dictionary<string, JsonElement>();
			foreach (var prop in existingDoc.EnumerateObject())
				merged[prop.Name] = prop.Value;
			foreach (var prop in newDoc.EnumerateObject())
			{
				if (prop.Value.ValueKind != JsonValueKind.Null)
					merged[prop.Name] = prop.Value;
			}
			jsonData = JsonSerializer.Serialize(merged, JsonOptions);

			await ExecuteWithRetryAsync("""
MATCH (o:Object {key: $key})-[:HAS_EXPANDED_DATA]->(d:ExpandedObjectData {sharpObjectId: $objId, dataType: $dataType})
SET d.data = $data
""", new { key = objKey, objId = sharpObjectId, dataType, data = jsonData }, cancellationToken);
		}
		else
		{
			jsonData = JsonSerializer.Serialize((object)data, JsonOptions);
			await ExecuteWithRetryAsync("""
MATCH (o:Object {key: $key})
CREATE (d:ExpandedObjectData {sharpObjectId: $objId, dataType: $dataType, data: $data})
CREATE (o)-[:HAS_EXPANDED_DATA]->(d)
""", new { key = objKey, objId = sharpObjectId, dataType, data = jsonData }, cancellationToken);
		}
	}

	public async ValueTask<T?> GetExpandedObjectData<T>(string sharpObjectId, string dataType, CancellationToken cancellationToken = default)
	{
		var objKey = ExtractKey(sharpObjectId);
		var result = await ExecuteWithRetryAsync("""
MATCH (o:Object {key: $key})-[:HAS_EXPANDED_DATA]->(d:ExpandedObjectData {sharpObjectId: $objId, dataType: $dataType})
RETURN d.data AS data
""", new { key = objKey, objId = sharpObjectId, dataType }, cancellationToken);

		if (result.Result.Count == 0) return default;
		var jsonData = result.Result[0]["data"].As<string>();
		if (string.IsNullOrEmpty(jsonData)) return default;
		return JsonSerializer.Deserialize<T>(jsonData, JsonOptions);
	}

	public async ValueTask SetExpandedServerData(string dataType, dynamic data, CancellationToken cancellationToken = default)
	{
		var jsonData = JsonSerializer.Serialize((object)data, JsonOptions);
		await ExecuteWithRetryAsync("""
MERGE (d:ExpandedServerData {dataType: $dataType})
SET d.data = $data
""", new { dataType, data = jsonData }, cancellationToken);
	}

	public async ValueTask<T?> GetExpandedServerData<T>(string dataType, CancellationToken cancellationToken = default)
	{
		try
		{
			var result = await ExecuteWithRetryAsync("MATCH (d:ExpandedServerData {dataType: $dataType}) RETURN d.data AS data", new { dataType }, cancellationToken);

			if (result.Result.Count == 0) return default;
			var jsonData = result.Result[0]["data"].As<string>();
			if (string.IsNullOrEmpty(jsonData)) return default;
			return JsonSerializer.Deserialize<T>(jsonData, JsonOptions);
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Failed to retrieve expanded server data for type '{DataType}'", dataType);
			return default;
		}
	}

	#endregion

	#region Channels

	public async IAsyncEnumerable<SharpChannel> GetAllChannelsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var result = await ExecuteWithRetryAsync("MATCH (c:Channel) RETURN c", ct: cancellationToken);
		foreach (var record in result.Result)
			yield return MapNodeToChannel(record["c"].As<INode>());
	}

	public async ValueTask<SharpChannel?> GetChannelAsync(string name, CancellationToken cancellationToken = default)
	{
		var result = await ExecuteWithRetryAsync("MATCH (c:Channel {name: $name}) RETURN c", new { name }, cancellationToken);
		return result.Result.Count > 0 ? MapNodeToChannel(result.Result[0]["c"].As<INode>()) : null;
	}

	public async IAsyncEnumerable<SharpChannel> GetMemberChannelsAsync(AnySharpObject obj, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var objKey = obj.Object().Key;
		var result = await ExecuteWithRetryAsync("""
MATCH (o:Object {key: $key})-[:ON_CHANNEL]->(c:Channel)
RETURN c
""", new { key = objKey }, cancellationToken);

		foreach (var record in result.Result)
			yield return MapNodeToChannel(record["c"].As<INode>());
	}

	public async ValueTask CreateChannelAsync(MString name, string[] privs, SharpPlayer owner, CancellationToken cancellationToken = default)
	{
		var channelName = name.ToPlainText();
		var serializedName = MarkupStringModule.serialize(name);
		var ownerObjKey = owner.Object.Key;

		// Single atomic query: create channel, link owner, add owner as member
		await ExecuteWithRetryAsync("""
MATCH (o:Object {key: $ownerKey})
CREATE (c:Channel {name: $name, markedUpName: $markedUpName, description: '', privs: $privs,
joinLock: '', speakLock: '', seeLock: '', hideLock: '', modLock: '',
buffer: 0, mogrifier: ''})
CREATE (c)-[:HAS_CHANNEL_OWNER]->(o)
CREATE (o)-[:ON_CHANNEL {combine: false, gagged: false, hide: false, mute: false, title: ''}]->(c)
""", new { name = channelName, markedUpName = serializedName, privs, ownerKey = ownerObjKey }, cancellationToken);
	}

	public async ValueTask UpdateChannelAsync(SharpChannel channel, MString? name, MString? description, string[]? privs,
	string? joinLock, string? speakLock, string? seeLock, string? hideLock, string? modLock,
	string? mogrifier, int? buffer, CancellationToken cancellationToken = default)
	{
		var channelName = channel.Name.ToPlainText();
		var newName = name is not null ? name.ToPlainText() : channelName;
		var newMarkedUpName = name is not null ? MarkupStringModule.serialize(name) : MarkupStringModule.serialize(channel.Name);
		var newDescription = description is not null ? MarkupStringModule.serialize(description) : MarkupStringModule.serialize(channel.Description);

		await ExecuteWithRetryAsync("""
MATCH (c:Channel {name: $oldName})
SET c.name = $newName, c.markedUpName = $markedUpName, c.description = $description,
c.privs = $privs, c.joinLock = $joinLock, c.speakLock = $speakLock,
c.seeLock = $seeLock, c.hideLock = $hideLock, c.modLock = $modLock,
c.buffer = $buffer, c.mogrifier = $mogrifier
""", new
		{
			oldName = channelName,
			newName,
			markedUpName = newMarkedUpName,
			description = newDescription,
			privs = privs ?? channel.Privs,
			joinLock = joinLock ?? channel.JoinLock ?? "",
			speakLock = speakLock ?? channel.SpeakLock ?? "",
			seeLock = seeLock ?? channel.SeeLock ?? "",
			hideLock = hideLock ?? channel.HideLock ?? "",
			modLock = modLock ?? channel.ModLock ?? "",
			buffer = buffer ?? channel.Buffer,
			mogrifier = mogrifier ?? channel.Mogrifier ?? ""
		}, cancellationToken);
	}

	public async ValueTask UpdateChannelOwnerAsync(SharpChannel channel, SharpPlayer newOwner, CancellationToken cancellationToken = default)
	{
		var channelName = channel.Name.ToPlainText();
		var ownerObjKey = newOwner.Object.Key;

		await ExecuteWithRetryAsync("""
MATCH (c:Channel {name: $name})-[r:HAS_CHANNEL_OWNER]->()
DELETE r
WITH c
MATCH (o:Object {key: $ownerKey})
CREATE (c)-[:HAS_CHANNEL_OWNER]->(o)
""", new { name = channelName, ownerKey = ownerObjKey }, cancellationToken);
	}

	public async ValueTask DeleteChannelAsync(SharpChannel channel, CancellationToken cancellationToken = default)
	{
		var channelName = channel.Name.ToPlainText();
		await ExecuteWithRetryAsync("MATCH (c:Channel {name: $name}) DETACH DELETE c", new { name = channelName }, cancellationToken);
	}

	public async ValueTask AddUserToChannelAsync(SharpChannel channel, AnySharpObject obj, CancellationToken cancellationToken = default)
	{
		var channelName = channel.Name.ToPlainText();
		var objKey = obj.Object().Key;
		await ExecuteWithRetryAsync("""
MATCH (o:Object {key: $key}), (c:Channel {name: $name})
CREATE (o)-[:ON_CHANNEL {combine: false, gagged: false, hide: false, mute: false, title: ''}]->(c)
""", new { key = objKey, name = channelName }, cancellationToken);
	}

	public async ValueTask RemoveUserFromChannelAsync(SharpChannel channel, AnySharpObject obj, CancellationToken cancellationToken = default)
	{
		var channelName = channel.Name.ToPlainText();
		var objKey = obj.Object().Key;
		await ExecuteWithRetryAsync("""
MATCH (o:Object {key: $key})-[r:ON_CHANNEL]->(c:Channel {name: $name})
DELETE r
""", new { key = objKey, name = channelName }, cancellationToken);
	}

	public async ValueTask UpdateChannelUserStatusAsync(SharpChannel channel, AnySharpObject obj, SharpChannelStatus status, CancellationToken cancellationToken = default)
	{
		var channelName = channel.Name.ToPlainText();
		var objKey = obj.Object().Key;

		var setClauses = new List<string>();
		var parameters = new Dictionary<string, object>
{
{ "key", objKey },
{ "name", channelName }
};

		if (status.Combine is { } combine)
		{
			setClauses.Add("r.combine = $combine");
			parameters["combine"] = combine;
		}
		if (status.Gagged is { } gagged)
		{
			setClauses.Add("r.gagged = $gagged");
			parameters["gagged"] = gagged;
		}
		if (status.Hide is { } hide)
		{
			setClauses.Add("r.hide = $hide");
			parameters["hide"] = hide;
		}
		if (status.Mute is { } mute)
		{
			setClauses.Add("r.mute = $mute");
			parameters["mute"] = mute;
		}
		if (status.Title is { } title)
		{
			setClauses.Add("r.title = $title");
			parameters["title"] = MarkupStringModule.serialize(title);
		}

		if (setClauses.Count == 0) return;

		var cypher = "MATCH (o:Object {key: $key})-[r:ON_CHANNEL]->(c:Channel {name: $name}) SET " +
		string.Join(", ", setClauses);

		await ExecuteWithRetryAsync(cypher, parameters, cancellationToken);
	}

	private SharpChannel MapNodeToChannel(INode node)
	{
		var channelName = node["name"].As<string>();
		var markedUpName = node.Properties.ContainsKey("markedUpName")
		? node["markedUpName"].As<string>()
		: channelName;
		var description = node.Properties.ContainsKey("description") ? node["description"].As<string>() : "";

		return new SharpChannel
		{
			Id = ChannelId(channelName),
			Name = MarkupStringModule.deserialize(markedUpName),
			Description = MarkupStringModule.deserialize(description),
			Privs = node.Properties.ContainsKey("privs")
		? node["privs"].As<List<object>>().Select(x => x.ToString()!).ToArray()
		: [],
			JoinLock = node.Properties.ContainsKey("joinLock") ? node["joinLock"].As<string>() : "",
			SpeakLock = node.Properties.ContainsKey("speakLock") ? node["speakLock"].As<string>() : "",
			SeeLock = node.Properties.ContainsKey("seeLock") ? node["seeLock"].As<string>() : "",
			HideLock = node.Properties.ContainsKey("hideLock") ? node["hideLock"].As<string>() : "",
			ModLock = node.Properties.ContainsKey("modLock") ? node["modLock"].As<string>() : "",
			Buffer = node.Properties.ContainsKey("buffer") ? node["buffer"].As<int>() : 0,
			Mogrifier = node.Properties.ContainsKey("mogrifier") ? node["mogrifier"].As<string>() : "",
			Owner = new AsyncLazy<SharpPlayer>(async ct => await GetChannelOwnerAsync(channelName, ct)),
			Members = new Lazy<IAsyncEnumerable<SharpChannel.MemberAndStatus>>(() =>
			GetChannelMembersAsync(channelName, CancellationToken.None))
		};
	}

	private async ValueTask<SharpPlayer> GetChannelOwnerAsync(string channelName, CancellationToken ct)
	{
		var result = await ExecuteWithRetryAsync("""
MATCH (c:Channel {name: $name})-[:HAS_CHANNEL_OWNER]->(o:Object)
RETURN o
""", new { name = channelName }, ct);

		var objNode = result.Result[0]["o"].As<INode>();
		var ownerObj = await BuildTypedObjectFromObjectNode(objNode, ct);
		return ownerObj.AsPlayer;
	}

	private async IAsyncEnumerable<SharpChannel.MemberAndStatus> GetChannelMembersAsync(string channelName, [EnumeratorCancellation] CancellationToken ct = default)
	{
		var result = await ExecuteWithRetryAsync("""
MATCH (o:Object)-[r:ON_CHANNEL]->(c:Channel {name: $name})
RETURN o, r
""", new { name = channelName }, ct);

		foreach (var record in result.Result)
		{
			var objNode = record["o"].As<INode>();
			var rel = record["r"].As<IRelationship>();
			var memberObj = await BuildTypedObjectFromObjectNode(objNode, ct);

			var status = new SharpChannelStatus(
			Combine: rel.Properties.ContainsKey("combine") ? rel["combine"].As<bool>() : false,
			Gagged: rel.Properties.ContainsKey("gagged") ? rel["gagged"].As<bool>() : false,
			Hide: rel.Properties.ContainsKey("hide") ? rel["hide"].As<bool>() : false,
			Mute: rel.Properties.ContainsKey("mute") ? rel["mute"].As<bool>() : false,
			Title: MarkupStringModule.deserialize(
			rel.Properties.ContainsKey("title") ? rel["title"].As<string>() ?? "" : ""));

			yield return new SharpChannel.MemberAndStatus(memberObj.Known(), status);
		}
	}

	#endregion
}
