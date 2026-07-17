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
	#region Attributes

	public async IAsyncEnumerable<SharpAttribute> GetAttributeAsync(DBRef dbref, string[] attribute, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		attribute = attribute.Select(x => x.ToUpper()).ToArray();
		var objKey = dbref.Number;

		var existResult = await ExecuteAsync(
			"SELECT key FROM object:$key",
			new Dictionary<string, object?> { ["key"] = objKey }, cancellationToken);

		var existRecords = existResult.GetValue<List<ObjectRecord>>(0)!;
		if (existRecords.Count == 0) yield break;

		var attrs = new List<AttributeRecord>();
		string? currentParentKey = null;
		var isFirst = true;

		foreach (var attrName in attribute)
		{
			SurrealDbResponse stepResult;
			if (isFirst)
			{
				var parameters = new Dictionary<string, object?> { ["key"] = objKey, ["attrName"] = attrName };
				stepResult = await ExecuteAsync(
					"SELECT * FROM attribute WHERE name = $attrName AND id IN (SELECT VALUE out FROM has_attribute WHERE in IN [player:$key, room:$key, thing:$key, exit:$key])",
					parameters, cancellationToken);
				isFirst = false;
			}
			else
			{
				var parameters = new Dictionary<string, object?> { ["key"] = currentParentKey!, ["attrName"] = attrName };
				stepResult = await ExecuteAsync(
					"SELECT * FROM attribute WHERE name = $attrName AND id IN (SELECT VALUE out FROM has_attribute WHERE in = attribute:⟨$key⟩)",
					parameters, cancellationToken);
			}

			var records = stepResult.GetValue<List<AttributeRecord>>(0)!;
			if (records.Count == 0) yield break;

			var childNode = records[0];
			attrs.Add(childNode);
			currentParentKey = childNode.key;
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

		var regexPattern = $"(?i)^{pattern}$";

		var parameters = new Dictionary<string, object?> { ["key"] = objKey };
		var objResult = await ExecuteAsync("SELECT * FROM object:$key", parameters, cancellationToken);
		var objRecords = objResult.GetValue<List<ObjectRecord>>(0)!;
		if (objRecords.Count == 0) yield break;

		var typedId = GetTypedId(objRecords[0].type, objKey);

		await foreach (var attr in GetAllAttributesForIdAsync(typedId, cancellationToken))
		{
			if (attr.LongName != null && Regex.IsMatch(attr.LongName, regexPattern, RegexOptions.IgnoreCase))
			{
				yield return attr;
			}
		}
	}

	public async IAsyncEnumerable<SharpAttribute> GetAttributesByRegexAsync(DBRef dbref, string attributePattern, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var objKey = dbref.Number;

		var parameters = new Dictionary<string, object?> { ["key"] = objKey };
		var objResult = await ExecuteAsync("SELECT * FROM object:$key", parameters, cancellationToken);
		var objRecords = objResult.GetValue<List<ObjectRecord>>(0)!;
		if (objRecords.Count == 0) yield break;

		var typedId = GetTypedId(objRecords[0].type, objKey);
		var fullPattern = ToFullMatchRegex(attributePattern.ToLower());

		await foreach (var attr in GetAllAttributesForIdAsync(typedId, cancellationToken))
		{
			if (attr.LongName != null && Regex.IsMatch(attr.LongName.ToLower(), fullPattern))
			{
				yield return attr;
			}
		}
	}

	public async IAsyncEnumerable<LazySharpAttribute> GetLazyAttributeAsync(DBRef dbref, string[] attribute, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		attribute = attribute.Select(x => x.ToUpper()).ToArray();
		var objKey = dbref.Number;

		var existResult = await ExecuteAsync(
			"SELECT key FROM object:$key",
			new Dictionary<string, object?> { ["key"] = objKey }, cancellationToken);

		var existRecords = existResult.GetValue<List<ObjectRecord>>(0)!;
		if (existRecords.Count == 0) yield break;

		var attrs = new List<AttributeRecord>();
		string? currentParentKey = null;
		var isFirst = true;

		foreach (var attrName in attribute)
		{
			SurrealDbResponse stepResult;
			if (isFirst)
			{
				var parameters = new Dictionary<string, object?> { ["key"] = objKey, ["attrName"] = attrName };
				stepResult = await ExecuteAsync(
					"SELECT * FROM attribute WHERE name = $attrName AND id IN (SELECT VALUE out FROM has_attribute WHERE in IN [player:$key, room:$key, thing:$key, exit:$key])",
					parameters, cancellationToken);
				isFirst = false;
			}
			else
			{
				var parameters = new Dictionary<string, object?> { ["key"] = currentParentKey!, ["attrName"] = attrName };
				stepResult = await ExecuteAsync(
					"SELECT * FROM attribute WHERE name = $attrName AND id IN (SELECT VALUE out FROM has_attribute WHERE in = attribute:⟨$key⟩)",
					parameters, cancellationToken);
			}

			var records = stepResult.GetValue<List<AttributeRecord>>(0)!;
			if (records.Count == 0) yield break;

			var childNode = records[0];
			attrs.Add(childNode);
			currentParentKey = childNode.key;
		}

		foreach (var node in attrs)
		{
			yield return await MapToLazySharpAttribute(node, cancellationToken);
		}
	}

	public async IAsyncEnumerable<LazySharpAttribute> GetLazyAttributesAsync(DBRef dbref, string attributePattern, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var objKey = dbref.Number;

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

		var regexPattern = $"(?i)^{pattern}$";

		var parameters = new Dictionary<string, object?> { ["key"] = objKey };
		var objResult = await ExecuteAsync("SELECT * FROM object:$key", parameters, cancellationToken);
		var objRecords = objResult.GetValue<List<ObjectRecord>>(0)!;
		if (objRecords.Count == 0) yield break;

		var typedId = GetTypedId(objRecords[0].type, objKey);

		await foreach (var attr in GetAllLazyAttributesForIdAsync(typedId, cancellationToken))
		{
			if (attr.LongName != null && Regex.IsMatch(attr.LongName, regexPattern, RegexOptions.IgnoreCase))
			{
				yield return attr;
			}
		}
	}

	public async IAsyncEnumerable<LazySharpAttribute> GetLazyAttributesByRegexAsync(DBRef dbref, string attributePattern, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var objKey = dbref.Number;

		var parameters = new Dictionary<string, object?> { ["key"] = objKey };
		var objResult = await ExecuteAsync("SELECT * FROM object:$key", parameters, cancellationToken);
		var objRecords = objResult.GetValue<List<ObjectRecord>>(0)!;
		if (objRecords.Count == 0) yield break;

		var typedId = GetTypedId(objRecords[0].type, objKey);
		var fullPattern = ToFullMatchRegex(attributePattern.ToLower());

		await foreach (var attr in GetAllLazyAttributesForIdAsync(typedId, cancellationToken))
		{
			if (attr.LongName != null && Regex.IsMatch(attr.LongName.ToLower(), fullPattern))
			{
				yield return attr;
			}
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

		var objParams = new Dictionary<string, object?> { ["key"] = objKey };
		var objResult = await ExecuteAsync("SELECT * FROM object:$key", objParams, cancellationToken);
		var objRecords = objResult.GetValue<List<ObjectRecord>>(0)!;
		if (objRecords.Count == 0) return false;

		var typedRecordId = GetSurrealRecordId(objRecords[0].type.ToLower(), objKey);

		string currentParentRecordId = typedRecordId;
		string? lastAttrKey = null;

		for (var i = 0; i < attribute.Length; i++)
		{
			var attrName = attribute[i];
			var longName = string.Join('`', attribute.Take(i + 1));
			var attrKey = $"{objKey}_{longName}";
			var isLast = i == attribute.Length - 1;

			if (isLast)
			{
				var upsertParams = new Dictionary<string, object?>
				{
					["key"] = attrKey,
					["name"] = attrName,
					["longName"] = longName,
					["value"] = serializedValue
				};

				await ExecuteAsync(
					"UPSERT attribute:⟨$key⟩ SET key = $key, name = $name, longName = $longName, value = $value",
					upsertParams, cancellationToken);
			}
			else
			{
				var upsertParams = new Dictionary<string, object?>
				{
					["key"] = attrKey,
					["name"] = attrName,
					["longName"] = longName
				};

				await ExecuteAsync(
					"UPSERT attribute:⟨$key⟩ SET key = $key, name = $name, longName = $longName, value = value ?? ''",
					upsertParams, cancellationToken);
			}

			if (i == 0)
			{
				var edgeId = $"{currentParentRecordId.Replace(":", "_")}__attr_{EscapeString(attrKey)}";
				var edgeParams = new Dictionary<string, object?>
				{
					["childKey"] = attrKey,
					["edgeId"] = edgeId
				};
				await ExecuteAsync(
					$"UPSERT has_attribute:⟨$edgeId⟩ SET in = {currentParentRecordId}, out = attribute:⟨$childKey⟩",
					edgeParams, cancellationToken);
			}
			else
			{
				var prevAttrKey = $"{objKey}_{string.Join('`', attribute.Take(i))}";
				var edgeId = $"attr_{EscapeString(prevAttrKey)}__attr_{EscapeString(attrKey)}";
				var innerEdgeParams = new Dictionary<string, object?>
				{
					["parentKey"] = prevAttrKey,
					["childKey"] = attrKey,
					["edgeId"] = edgeId
				};
				await ExecuteAsync(
					"UPSERT has_attribute:⟨$edgeId⟩ SET in = attribute:⟨$parentKey⟩, out = attribute:⟨$childKey⟩",
					innerEdgeParams, cancellationToken);
			}

			currentParentRecordId = $"attribute:⟨{attrKey}⟩";
			lastAttrKey = attrKey;
		}

		if (lastAttrKey == null) return false;

		// Every attribute node must have an owner — the leaf AND every branch parent auto-created
		// along the path (e.g. setting FOO`BAR creates FOO, which must be owned too). Previously only
		// the leaf was owned, so branch parents came back owner-less and any consumer that reads the
		// owner (examine) tripped over the missing edge.
		for (var level = 0; level < attribute.Length; level++)
		{
			var levelLongName = string.Join('`', attribute.Take(level + 1));
			var levelAttrKey = $"{objKey}_{levelLongName}";
			var ownerParams = new Dictionary<string, object?>
			{
				["attrKey"] = levelAttrKey,
				["ownerKey"] = ownerKey
			};

			await ExecuteAsync(
				"DELETE has_attribute_owner WHERE in = attribute:⟨$attrKey⟩",
				ownerParams, cancellationToken);

			await ExecuteAsync(
				"RELATE attribute:⟨$attrKey⟩->has_attribute_owner->player:$ownerKey",
				ownerParams, cancellationToken);
		}

	// Set branch flag on parent attribute nodes (not the root typed node)
	for (var i = 0; i < attribute.Length - 1; i++)
	{
		var longName = string.Join('`', attribute.Take(i + 1));
		var attrKey = $"{objKey}_{longName}";
		var escapedAttrKey = EscapeRecordId(attrKey);
		var branchParams = new Dictionary<string, object?>();
		await ExecuteAsync(
			$"RELATE attribute:⟨{escapedAttrKey}⟩->has_attribute_flag->(SELECT VALUE id FROM attribute_flag WHERE string::uppercase(name) = 'BRANCH' AND id NOT IN (SELECT VALUE out FROM has_attribute_flag WHERE in = attribute:⟨{escapedAttrKey}⟩) LIMIT 1)",
			branchParams, cancellationToken);
	}

	for (var i = 0; i < attribute.Length; i++)
	{
		var longName = string.Join('`', attribute.Take(i + 1));
		var attrEntry = await GetSharpAttributeEntry(longName, cancellationToken);
		var flagNames = attrEntry?.DefaultFlags ?? [];

		var attrKey = $"{objKey}_{longName}";
		var escapedAttrKey = EscapeRecordId(attrKey);
		foreach (var flagName in flagNames)
		{
			var flagParams = new Dictionary<string, object?>
			{
				["flagName"] = flagName
			};

			await ExecuteAsync(
				$"RELATE attribute:⟨{escapedAttrKey}⟩->has_attribute_flag->(SELECT VALUE id FROM attribute_flag WHERE string::uppercase(name) = string::uppercase($flagName) AND id NOT IN (SELECT VALUE out FROM has_attribute_flag WHERE in = attribute:⟨{escapedAttrKey}⟩) LIMIT 1)",
				flagParams, cancellationToken);
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
		var parameters = new Dictionary<string, object?>
		{
			["attrKey"] = attrKey,
			["flagName"] = flag.Name
		};

		await ExecuteAsync(
			"RELATE attribute:⟨$attrKey⟩->has_attribute_flag->(SELECT VALUE id FROM attribute_flag WHERE name = $flagName LIMIT 1)",
			parameters, cancellationToken);
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
		var parameters = new Dictionary<string, object?>
		{
			["attrKey"] = attrKey,
			["flagName"] = flag.Name
		};

		await ExecuteAsync(
			"DELETE has_attribute_flag WHERE in = attribute:⟨$attrKey⟩ AND out.name = $flagName",
			parameters, cancellationToken);
	}

	public async ValueTask<SharpAttributeFlag?> GetAttributeFlagAsync(string flagName, CancellationToken cancellationToken = default)
	{
		var parameters = new Dictionary<string, object?> { ["name"] = flagName };
		var result = await ExecuteAsync(
			"SELECT * FROM attribute_flag WHERE string::uppercase(name) = string::uppercase($name)",
			parameters, cancellationToken);

		var records = result.GetValue<List<AttributeFlagRecord>>(0)!;
		return records.Count > 0 ? MapRecordToAttributeFlag(records[0]) : null;
	}

	public async IAsyncEnumerable<SharpAttributeFlag> GetAttributeFlagsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var result = await ExecuteAsync("SELECT * FROM attribute_flag", cancellationToken);
		var records = result.GetValue<List<AttributeFlagRecord>>(0)!;
		foreach (var record in records)
			yield return MapRecordToAttributeFlag(record);
	}

	public async ValueTask<bool> ClearAttributeAsync(DBRef dbref, string[] attribute, CancellationToken cancellationToken = default)
	{
		attribute = attribute.Select(x => x.ToUpper()).ToArray();
		var attrs = GetAttributeAsync(dbref, attribute, cancellationToken);
		var targetAttr = await attrs.LastOrDefaultAsync(cancellationToken);
		if (targetAttr is null) return false;

		var attrKey = ExtractKeyString(targetAttr.Id);

		var childParams = new Dictionary<string, object?> { ["key"] = attrKey };
		var childrenResult = await ExecuteAsync(
			"SELECT count() AS cnt FROM has_attribute WHERE in = attribute:⟨$key⟩ GROUP ALL",
			childParams, cancellationToken);

		var records = childrenResult.GetValue<List<CountRecord>>(0)!;
		var hasChildren = records.Count > 0 && records[0].cnt > 0;

		if (hasChildren)
		{
			var clearParams = new Dictionary<string, object?>
			{
				["key"] = attrKey,
				["value"] = MModule.serialize(MModule.empty())
			};
			await ExecuteAsync(
				"UPDATE attribute:⟨$key⟩ SET value = $value",
				clearParams, cancellationToken);
		}
		else
		{
			var deleteParams = new Dictionary<string, object?> { ["key"] = attrKey };
			await ExecuteAsync("DELETE has_attribute WHERE out = attribute:⟨$key⟩", deleteParams, cancellationToken);
			await ExecuteAsync("DELETE has_attribute_flag WHERE in = attribute:⟨$key⟩", deleteParams, cancellationToken);
			await ExecuteAsync("DELETE has_attribute_owner WHERE in = attribute:⟨$key⟩", deleteParams, cancellationToken);
			await ExecuteAsync("DELETE has_attribute_entry WHERE in = attribute:⟨$key⟩", deleteParams, cancellationToken);
			await ExecuteAsync("DELETE attribute:⟨$key⟩", deleteParams, cancellationToken);

			if (attribute.Length > 1)
			{
				var parentLongName = string.Join('`', attribute.Take(attribute.Length - 1));
				var parentAttrKey = $"{dbref.Number}_{parentLongName}";
				await RemoveBranchFlagIfNoChildrenAsync(parentAttrKey, cancellationToken);
			}
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

		await WipeAttributeDescendantsAsync(attrKey, cancellationToken);

		var deleteParams = new Dictionary<string, object?> { ["key"] = attrKey };
		await ExecuteAsync("DELETE has_attribute WHERE out = attribute:⟨$key⟩", deleteParams, cancellationToken);
		await ExecuteAsync("DELETE has_attribute WHERE in = attribute:⟨$key⟩", deleteParams, cancellationToken);
		await ExecuteAsync("DELETE has_attribute_flag WHERE in = attribute:⟨$key⟩", deleteParams, cancellationToken);
		await ExecuteAsync("DELETE has_attribute_owner WHERE in = attribute:⟨$key⟩", deleteParams, cancellationToken);
		await ExecuteAsync("DELETE has_attribute_entry WHERE in = attribute:⟨$key⟩", deleteParams, cancellationToken);
		await ExecuteAsync("DELETE attribute:⟨$key⟩", deleteParams, cancellationToken);

		if (attribute.Length > 1)
		{
			var parentLongName = string.Join('`', attribute.Take(attribute.Length - 1));
			var parentAttrKey = $"{dbref.Number}_{parentLongName}";
			await RemoveBranchFlagIfNoChildrenAsync(parentAttrKey, cancellationToken);
		}

		return true;
	}

	private async ValueTask WipeAttributeDescendantsAsync(string attrKey, CancellationToken ct)
	{
		var parameters = new Dictionary<string, object?> { ["key"] = attrKey };
		var result = await ExecuteAsync(
			"SELECT VALUE key FROM attribute:⟨$key⟩->has_attribute->attribute",
			parameters, ct);

		var childKeys = result.GetValue<List<string>>(0)!;

		foreach (var childKey in childKeys)
		{
			if (string.IsNullOrEmpty(childKey)) continue;

			await WipeAttributeDescendantsAsync(childKey, ct);

			var deleteParams = new Dictionary<string, object?> { ["key"] = childKey };
			await ExecuteAsync("DELETE has_attribute WHERE out = attribute:⟨$key⟩", deleteParams, ct);
			await ExecuteAsync("DELETE has_attribute WHERE in = attribute:⟨$key⟩", deleteParams, ct);
			await ExecuteAsync("DELETE has_attribute_flag WHERE in = attribute:⟨$key⟩", deleteParams, ct);
			await ExecuteAsync("DELETE has_attribute_owner WHERE in = attribute:⟨$key⟩", deleteParams, ct);
			await ExecuteAsync("DELETE has_attribute_entry WHERE in = attribute:⟨$key⟩", deleteParams, ct);
			await ExecuteAsync("DELETE attribute:⟨$key⟩", deleteParams, ct);
		}
	}

	#endregion

	#region Attribute Entries

	public async IAsyncEnumerable<SharpAttributeEntry> GetAllAttributeEntriesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var result = await ExecuteAsync("SELECT * FROM attribute_entry", cancellationToken);
		var records = result.GetValue<List<AttributeEntryRecord>>(0)!;
		foreach (var record in records)
			yield return MapRecordToAttributeEntry(record);
	}

	public async ValueTask<SharpAttributeEntry?> GetSharpAttributeEntry(string name, CancellationToken ct = default)
	{
		var parameters = new Dictionary<string, object?> { ["name"] = name };
		var result = await ExecuteAsync(
			"SELECT * FROM attribute_entry WHERE name = $name",
			parameters, ct);

		var records = result.GetValue<List<AttributeEntryRecord>>(0)!;
		return records.Count > 0 ? MapRecordToAttributeEntry(records[0]) : null;
	}

	public async ValueTask<SharpAttributeEntry?> CreateOrUpdateAttributeEntryAsync(string name, string[] defaultFlags,
		string? limit = null, string[]? enumValues = null, CancellationToken cancellationToken = default)
	{
		var parameters = new Dictionary<string, object?>
		{
			["name"] = name,
			["defaultFlags"] = defaultFlags,
			["lim"] = limit ?? "",
			["enumValues"] = enumValues ?? Array.Empty<string>()
		};

		await ExecuteAsync(
			"UPSERT attribute_entry:⟨$name⟩ SET name = $name, defaultFlags = $defaultFlags, lim = $lim, enumValues = $enumValues",
			parameters, cancellationToken);

		return await GetSharpAttributeEntry(name, cancellationToken);
	}

	public async ValueTask<bool> DeleteAttributeEntryAsync(string name, CancellationToken cancellationToken = default)
	{
		var existing = await GetSharpAttributeEntry(name, cancellationToken);
		if (existing == null) return false;

		var parameters = new Dictionary<string, object?> { ["name"] = name };
		await ExecuteAsync("DELETE has_attribute_entry WHERE out = attribute_entry:⟨$name⟩", parameters, cancellationToken);
		await ExecuteAsync("DELETE attribute_entry:⟨$name⟩", parameters, cancellationToken);
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
		var parentParams = new Dictionary<string, object?> { ["key"] = objKey };
		var parentChain = await GetParentChainAsync(objKey, cancellationToken);

		foreach (var parentKey in parentChain)
		{
			var parentDbRef = new DBRef(parentKey);
			var parentAttrs = await GetAttributeAsync(parentDbRef, attribute, cancellationToken).ToArrayAsync(cancellationToken);
			if (parentAttrs.Length == attribute.Length)
			{
				var lastAttr = parentAttrs.Last();
				// no_inherit flag prevents attribute from being visible to children
				if (lastAttr.Flags.Any(f => f.Name.Equals("no_inherit", StringComparison.OrdinalIgnoreCase)))
					continue;
				var flags = lastAttr.Flags.Where(f => f.Inheritable);
				yield return new AttributeWithInheritance(parentAttrs, parentDbRef, AttributeSource.Parent, flags);
				yield break;
			}
		}

		var allKeys = new List<int> { objKey };
		allKeys.AddRange(parentChain);

		foreach (var chainKey in allKeys)
		{
			var zoneParams = new Dictionary<string, object?> { ["key"] = chainKey };
			var zoneResult = await ExecuteAsync(
				"SELECT VALUE key FROM object:$key->has_zone->object",
				zoneParams, cancellationToken);

			var zoneKeys = zoneResult.GetValue<List<int>>(0)!;

			foreach (var zoneKey in zoneKeys)
			{
				var zoneDbRef = new DBRef(zoneKey);
				var zoneAttrs = await GetAttributeAsync(zoneDbRef, attribute, cancellationToken).ToArrayAsync(cancellationToken);
				if (zoneAttrs.Length == attribute.Length)
				{
					var lastAttr = zoneAttrs.Last();
					// no_inherit flag prevents attribute from being visible to children
					if (lastAttr.Flags.Any(f => f.Name.Equals("no_inherit", StringComparison.OrdinalIgnoreCase)))
						continue;
					var flags = lastAttr.Flags.Where(f => f.Inheritable);
					yield return new AttributeWithInheritance(zoneAttrs, zoneDbRef, AttributeSource.Zone, flags);
					yield break;
				}
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
		var parentChain = await GetParentChainAsync(objKey, cancellationToken);

		foreach (var parentKey in parentChain)
		{
			var parentDbRef = new DBRef(parentKey);
			var parentAttrs = await GetLazyAttributeAsync(parentDbRef, attribute, cancellationToken).ToArrayAsync(cancellationToken);
			if (parentAttrs.Length == attribute.Length)
			{
				var lastAttr = parentAttrs.Last();
				// no_inherit flag prevents attribute from being visible to children
				if (lastAttr.Flags.Any(f => f.Name.Equals("no_inherit", StringComparison.OrdinalIgnoreCase)))
					continue;
				var flags = lastAttr.Flags.Where(f => f.Inheritable);
				yield return new LazyAttributeWithInheritance(parentAttrs, parentDbRef, AttributeSource.Parent, flags);
				yield break;
			}
		}

		var allKeys = new List<int> { objKey };
		allKeys.AddRange(parentChain);

		foreach (var chainKey in allKeys)
		{
			var zoneParams = new Dictionary<string, object?> { ["key"] = chainKey };
			var zoneResult = await ExecuteAsync(
				"SELECT VALUE key FROM object:$key->has_zone->object",
				zoneParams, cancellationToken);

			var zoneKeys = zoneResult.GetValue<List<int>>(0)!;

			foreach (var zoneKey in zoneKeys)
			{
				var zoneDbRef = new DBRef(zoneKey);
				var zoneAttrs = await GetLazyAttributeAsync(zoneDbRef, attribute, cancellationToken).ToArrayAsync(cancellationToken);
				if (zoneAttrs.Length == attribute.Length)
				{
					var lastAttr = zoneAttrs.Last();
					// no_inherit flag prevents attribute from being visible to children
					if (lastAttr.Flags.Any(f => f.Name.Equals("no_inherit", StringComparison.OrdinalIgnoreCase)))
						continue;
					var flags = lastAttr.Flags.Where(f => f.Inheritable);
					yield return new LazyAttributeWithInheritance(zoneAttrs, zoneDbRef, AttributeSource.Zone, flags);
					yield break;
				}
			}
		}
	}

	/// <summary>
	/// After removing a child attribute, check if the parent attribute still has children.
	/// If not, remove the branch flag from the parent.
	/// </summary>
	private async ValueTask RemoveBranchFlagIfNoChildrenAsync(string parentAttrKey, CancellationToken cancellationToken)
	{
		var checkParams = new Dictionary<string, object?> { ["key"] = parentAttrKey };
		var childResult = await ExecuteAsync(
			"SELECT count() AS cnt FROM has_attribute WHERE in = attribute:⟨$key⟩ GROUP ALL",
			checkParams, cancellationToken);

		var records = childResult.GetValue<List<CountRecord>>(0)!;
		var hasChildren = records.Count > 0 && records[0].cnt > 0;

		if (!hasChildren)
		{
			var escapedKey = EscapeRecordId(parentAttrKey);
			await ExecuteAsync(
				$"DELETE has_attribute_flag WHERE in = attribute:⟨{escapedKey}⟩ AND out IN (SELECT VALUE id FROM attribute_flag WHERE string::uppercase(name) = 'BRANCH')",
				new Dictionary<string, object?>(), cancellationToken);
		}
	}

	public async ValueTask ReassignAttributeOwnerAsync(SharpPlayer oldOwner, SharpPlayer newOwner, CancellationToken cancellationToken = default)
	{
		var oldKey = ExtractKey(oldOwner.Id!);
		var newKey = ExtractKey(newOwner.Id!);

		var parameters = new Dictionary<string, object?>
		{
			["oldKey"] = oldKey,
			["newKey"] = newKey
		};

		var response = await ExecuteAsync(
			"SELECT VALUE in.key FROM has_attribute_owner WHERE out = player:$oldKey",
			parameters, cancellationToken);
		var attrKeys = response.GetValue<List<string>>(0);

		if (attrKeys != null)
		{
			foreach (var attrKey in attrKeys)
			{
				if (string.IsNullOrEmpty(attrKey)) continue;
				var attrParams = new Dictionary<string, object?>
				{
					["attrKey"] = attrKey,
					["newKey"] = newKey
				};
				// Re-create the owner edge inside a transaction so the attribute is never observed
				// owner-less between the DELETE and the RELATE — a concurrent examine ->
				// GetAttributeOwnerAsync would otherwise see a null owner and crash. SurrealDB graph-edge
				// in/out cannot be UPDATEd, so the edge must be re-created; the transaction is what makes
				// that atomic to outside readers.
				await ExecuteAsync(
					"BEGIN TRANSACTION;" +
					"DELETE has_attribute_owner WHERE in = attribute:⟨$attrKey⟩;" +
					"RELATE attribute:⟨$attrKey⟩->has_attribute_owner->player:$newKey;" +
					"COMMIT TRANSACTION",
					attrParams, cancellationToken);
			}
		}

		await ExecuteAsync("DELETE has_attribute_owner WHERE out = player:$oldKey", parameters, cancellationToken);
	}

	#endregion

	#region Attribute Helpers

	/// <summary>
	/// Walks the parent chain for an object, returning all parent object keys.
	/// </summary>
	private async ValueTask<List<int>> GetParentChainAsync(int objKey, CancellationToken ct)
	{
		var parents = new List<int>();
		var currentKey = objKey;
		var visited = new HashSet<int> { objKey };

		for (var depth = 0; depth < 100; depth++)
		{
			var parameters = new Dictionary<string, object?> { ["key"] = currentKey };
			var result = await ExecuteAsync(
				"SELECT VALUE key FROM object:$key->has_parent->object",
				parameters, ct);

			var parentKeys = result.GetValue<List<int>>(0)!;
			if (parentKeys.Count == 0) break;

			var parentKey = parentKeys[0];
			if (!visited.Add(parentKey)) break; // Prevent cycles

			parents.Add(parentKey);
			currentKey = parentKey;
		}

		return parents;
	}

	#endregion
}
