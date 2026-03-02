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
}
