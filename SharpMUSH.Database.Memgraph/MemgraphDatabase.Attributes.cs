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
	#region Attributes

	public async IAsyncEnumerable<SharpAttribute> GetAttributeAsync(DBRef dbref, string[] attribute, [EnumeratorCancellation] CancellationToken cancellationToken = default)
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

		// Trailing backtick means "direct children only" — e.g. FOO` → FOO`[^`]+
		if (pattern.EndsWith("`"))
		{
			pattern = pattern + "[^`]+";
		}

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

		// Trailing backtick means "direct children only" — e.g. FOO` → FOO`[^`]+
		if (pattern.EndsWith("`"))
		{
			pattern = pattern + "[^`]+";
		}

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
		var serializedValue = MModule.serialize(value);
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

		sb.AppendLine("MATCH (typed)-[:IS_OBJECT]->(o:Object {key: $objKey})");

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
				sb.AppendLine($"ON MATCH SET {childAlias}.longName = ${longParam}, {childAlias}.value = $value");
			}
			else
			{
				sb.AppendLine($"ON CREATE SET {childAlias}.key = ${keyParam}, {childAlias}.longName = ${longParam}, {childAlias}.value = $emptyValue");
				sb.AppendLine($"ON MATCH SET {childAlias}.longName = ${longParam}");
			}
		}

		// Own EVERY level — the leaf and every branch parent auto-created along the path — not just
		// the leaf. A branch parent (e.g. FOO when setting FOO`BAR) that is never owned comes back with
		// a null owner, and a reader (examine) crashes on it. This whole query is one implicit
		// transaction, so the reassignment is atomic to concurrent readers.
		var allAliases = string.Join(", ", Enumerable.Range(0, attribute.Length).Select(i => $"a{i}"));
		var leafAlias = $"a{attribute.Length - 1}";

		sb.AppendLine($"WITH {allAliases}");
		sb.AppendLine("MATCH (p:Player {key: $ownerKey})");
		sb.AppendLine($"WITH {allAliases}, p");
		for (var i = 0; i < attribute.Length; i++)
			sb.AppendLine($"OPTIONAL MATCH (a{i})-[oo{i}:HAS_ATTRIBUTE_OWNER]->()");
		sb.AppendLine($"DELETE {string.Join(", ", Enumerable.Range(0, attribute.Length).Select(i => $"oo{i}"))}");
		sb.AppendLine($"WITH {allAliases}, p");
		for (var i = 0; i < attribute.Length; i++)
			sb.AppendLine($"CREATE (a{i})-[:HAS_ATTRIBUTE_OWNER]->(p)");
		sb.AppendLine($"RETURN {leafAlias}.key AS leafKey");

		var result = await ExecuteWithRetryAsync(sb.ToString(), parameters, cancellationToken);
		if (result.Result.Count == 0) return false;

	// Set branch flag on parent attribute nodes (not the root typed node)
		for (var i = 0; i < attribute.Length - 1; i++)
		{
			var longName = string.Join('`', attribute.Take(i + 1));
			var attrKey = $"{objKey}_{longName}";
			// MERGE to avoid duplicate flag relationships
			await ExecuteWithRetryAsync("""
		MATCH (a:Attribute {key: $key}), (f:AttributeFlag)
		WHERE toUpper(f.name) = 'BRANCH'
		MERGE (a)-[:HAS_ATTRIBUTE_FLAG]->(f)
		""", new { key = attrKey }, cancellationToken);
		}

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
		var attrPath = await GetAttributeAsync(dbref, attribute, cancellationToken).ToListAsync(cancellationToken);
		var targetAttr = attrPath.LastOrDefault();
		if (targetAttr is null) return false;

		var attrKey = ExtractKeyString(targetAttr.Id);

		var childrenResult = await ExecuteWithRetryAsync("""
MATCH (a:Attribute {key: $key})-[:HAS_ATTRIBUTE]->(child:Attribute)
RETURN count(child) AS cnt
""", new { key = attrKey }, cancellationToken);

		var hasChildren = childrenResult.Result.Count > 0 && childrenResult.Result[0]["cnt"].As<long>() > 0;

		if (hasChildren)
		{
			await ExecuteWithRetryAsync("MATCH (a:Attribute {key: $key}) SET a.value = $value", new { key = attrKey, value = MModule.serialize(MModule.empty()) }, cancellationToken);
		}
		else
		{
			await ExecuteWithRetryAsync("MATCH (a:Attribute {key: $key}) DETACH DELETE a", new { key = attrKey }, cancellationToken);

			if (attrPath.Count >= 2)
			{
				var parentAttr = attrPath[^2];
				await RemoveBranchFlagIfNoChildrenAsync(ExtractKeyString(parentAttr.Id), cancellationToken);
			}
		}

		return true;
	}

public async ValueTask<bool> WipeAttributeAsync(DBRef dbref, string[] attribute, CancellationToken cancellationToken = default)
	{
		attribute = attribute.Select(x => x.ToUpper()).ToArray();
		var attrPath = await GetAttributeAsync(dbref, attribute, cancellationToken).ToListAsync(cancellationToken);
		var targetAttr = attrPath.LastOrDefault();
		if (targetAttr is null) return false;

		var attrKey = ExtractKeyString(targetAttr.Id);

		await ExecuteWithRetryAsync("""
MATCH (a:Attribute {key: $key})-[:HAS_ATTRIBUTE*1..999]->(descendant:Attribute)
DETACH DELETE descendant
""", new { key = attrKey }, cancellationToken);

		await ExecuteWithRetryAsync("MATCH (a:Attribute {key: $key}) DETACH DELETE a", new { key = attrKey }, cancellationToken);

		if (attrPath.Count >= 2)
		{
			var parentAttr = attrPath[^2];
			await RemoveBranchFlagIfNoChildrenAsync(ExtractKeyString(parentAttr.Id), cancellationToken);
		}

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

		var selfAttrs = await GetAttributeAsync(dbref, attribute, cancellationToken).ToArrayAsync(cancellationToken);
		if (selfAttrs.Length == attribute.Length)
		{
			var lastAttr = selfAttrs.Last();
			yield return new AttributeWithInheritance(selfAttrs, dbref, AttributeSource.Self, lastAttr.Flags);
			yield break;
		}

		if (!checkParent) yield break;

		var objKey = dbref.Number;
		var parentResult = await ExecuteWithRetryAsync("MATCH (o:Object {key: $key})-[:HAS_PARENT*1..100]->(parent:Object) RETURN parent", new { key = objKey }, cancellationToken);

	foreach (var parentNode in parentResult.Result.Select(r => r["parent"].As<INode>()))
		{
			var parentKey = parentNode["key"].As<int>();
			var parentDbRef = new DBRef(parentKey);
			var parentAttrs = await GetAttributeAsync(parentDbRef, attribute, cancellationToken).ToArrayAsync(cancellationToken);
			if (parentAttrs.Length == attribute.Length)
			{
				var lastAttr = parentAttrs.Last();
				// no_inherit flag prevents attribute from being visible to children
				if (lastAttr.Flags.Any(f => f.Name == "no_inherit"))
					yield break;
				var flags = lastAttr.Flags.Where(f => f.Inheritable);
				yield return new AttributeWithInheritance(parentAttrs, parentDbRef, AttributeSource.Parent, flags);
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

	foreach (var zoneNode in chainResult.Result.Select(r => r["zone"].As<INode>()))
		{
			var zoneKey = zoneNode["key"].As<int>();
			var zoneDbRef = new DBRef(zoneKey);
			var zoneAttrs = await GetAttributeAsync(zoneDbRef, attribute, cancellationToken).ToArrayAsync(cancellationToken);
			if (zoneAttrs.Length == attribute.Length)
			{
				var lastAttr = zoneAttrs.Last();
				// no_inherit flag prevents attribute from being visible to children
				if (lastAttr.Flags.Any(f => f.Name == "no_inherit"))
					yield break;
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

		foreach (var parentNode in parentResult.Result.Select(r => r["parent"].As<INode>()))
		{
			var parentKey = parentNode["key"].As<int>();
			var parentDbRef = new DBRef(parentKey);
			var parentAttrs = await GetLazyAttributeAsync(parentDbRef, attribute, cancellationToken).ToArrayAsync(cancellationToken);
		if (parentAttrs.Length == attribute.Length)
			{
				var lastAttr = parentAttrs.Last();
				// no_inherit flag prevents attribute from being visible to children
				if (lastAttr.Flags.Any(f => f.Name == "no_inherit"))
					yield break;
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

		foreach (var zoneNode in chainResult.Result.Select(r => r["zone"].As<INode>()))
		{
			var zoneKey = zoneNode["key"].As<int>();
			var zoneDbRef = new DBRef(zoneKey);
			var zoneAttrs = await GetLazyAttributeAsync(zoneDbRef, attribute, cancellationToken).ToArrayAsync(cancellationToken);
			if (zoneAttrs.Length == attribute.Length)
			{
				var lastAttr = zoneAttrs.Last();
				// no_inherit flag prevents attribute from being visible to children
				if (lastAttr.Flags.Any(f => f.Name == "no_inherit"))
					yield break;
				var flags = lastAttr.Flags.Where(f => f.Inheritable);
				yield return new LazyAttributeWithInheritance(zoneAttrs, zoneDbRef, AttributeSource.Zone, flags);
				yield break;
			}
		}
	}

	/// <summary>
	/// After removing a child attribute, check if the parent attribute still has children.
	/// If not, remove the branch flag from the parent.
	/// </summary>
	private async ValueTask RemoveBranchFlagIfNoChildrenAsync(string parentAttrKey, CancellationToken cancellationToken)
	{
		var remainingChildren = await ExecuteWithRetryAsync("""
MATCH (a:Attribute {key: $key})-[:HAS_ATTRIBUTE]->(child:Attribute)
RETURN count(child) AS cnt
""", new { key = parentAttrKey }, cancellationToken);

		var childCount = remainingChildren.Result.Count > 0 ? remainingChildren.Result[0]["cnt"].As<long>() : 0;
		if (childCount == 0)
		{
			await ExecuteWithRetryAsync("""
MATCH (a:Attribute {key: $key})-[r:HAS_ATTRIBUTE_FLAG]->(f:AttributeFlag)
WHERE toUpper(f.name) = 'BRANCH'
DELETE r
""", new { key = parentAttrKey }, cancellationToken);
		}
	}

	public async ValueTask ReassignAttributeOwnerAsync(SharpPlayer oldOwner, SharpPlayer newOwner, CancellationToken cancellationToken = default)
	{
		var oldKey = ExtractKey(oldOwner.Id!);
		var newKey = ExtractKey(newOwner.Id!);

		await ExecuteWithRetryAsync("""
MATCH (a:Attribute)-[r:HAS_ATTRIBUTE_OWNER]->(oldP:Player {key: $oldKey})
MATCH (newP:Player {key: $newKey})
CREATE (a)-[:HAS_ATTRIBUTE_OWNER]->(newP)
DELETE r
""", new { oldKey, newKey }, cancellationToken);
	}

	#endregion
}
