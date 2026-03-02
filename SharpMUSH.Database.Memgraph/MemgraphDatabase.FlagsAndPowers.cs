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
name, symbol, system,
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
name, alias, system,
setPerms = setPermissions, unsetPerms = unsetPermissions, typeRestrictions
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
}
