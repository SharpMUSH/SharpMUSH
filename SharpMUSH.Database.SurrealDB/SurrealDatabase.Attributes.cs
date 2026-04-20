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

		// Find the typed node for this object
		var typedResult = await ExecuteAsync(
			"SELECT * FROM player, room, thing, exit WHERE key = $key",
			new Dictionary<string, object?> { ["key"] = objKey }, cancellationToken);

		var typedRecords = typedResult.GetValue<List<PlayerRecord>>(0)!;
		if (typedRecords.Count == 0) yield break;

		// Walk the attribute tree step by step
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
					"SELECT * FROM attribute WHERE name = $attrName AND id IN (SELECT VALUE out FROM has_attribute WHERE in = type::thing('player', $key) OR in = type::thing('room', $key) OR in = type::thing('thing', $key) OR in = type::thing('exit', $key))",
					parameters, cancellationToken);
				isFirst = false;
			}
			else
			{
				var parameters = new Dictionary<string, object?> { ["key"] = currentParentKey!, ["attrName"] = attrName };
				stepResult = await ExecuteAsync(
					"SELECT * FROM attribute WHERE name = $attrName AND id IN (SELECT VALUE out FROM has_attribute WHERE in = type::thing('attribute', $key))",
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

		var regexPattern = $"(?i)^{pattern}$";

		// Get the typed ID for this object
		var parameters = new Dictionary<string, object?> { ["key"] = objKey };
		var objResult = await ExecuteAsync("SELECT * FROM object WHERE key = $key", parameters, cancellationToken);
		var objRecords = objResult.GetValue<List<ObjectRecord>>(0)!;
		if (objRecords.Count == 0) yield break;

		var typedId = GetTypedId(objRecords[0].type, objKey);

		// Recursively gather all attributes and filter
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
		var objResult = await ExecuteAsync("SELECT * FROM object WHERE key = $key", parameters, cancellationToken);
		var objRecords = objResult.GetValue<List<ObjectRecord>>(0)!;
		if (objRecords.Count == 0) yield break;

		var typedId = GetTypedId(objRecords[0].type, objKey);
		var fullPattern = ToFullMatchRegex(attributePattern.ToLower());

		// Recursively gather all attributes and filter
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

		var typedResult = await ExecuteAsync(
			"SELECT * FROM player, room, thing, exit WHERE key = $key",
			new Dictionary<string, object?> { ["key"] = objKey }, cancellationToken);

		var typedRecords = typedResult.GetValue<List<PlayerRecord>>(0)!;
		if (typedRecords.Count == 0) yield break;

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
					"SELECT * FROM attribute WHERE name = $attrName AND id IN (SELECT VALUE out FROM has_attribute WHERE in = type::thing('player', $key) OR in = type::thing('room', $key) OR in = type::thing('thing', $key) OR in = type::thing('exit', $key))",
					parameters, cancellationToken);
				isFirst = false;
			}
			else
			{
				var parameters = new Dictionary<string, object?> { ["key"] = currentParentKey!, ["attrName"] = attrName };
				stepResult = await ExecuteAsync(
					"SELECT * FROM attribute WHERE name = $attrName AND id IN (SELECT VALUE out FROM has_attribute WHERE in = type::thing('attribute', $key))",
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

		var regexPattern = $"(?i)^{pattern}$";

		var parameters = new Dictionary<string, object?> { ["key"] = objKey };
		var objResult = await ExecuteAsync("SELECT * FROM object WHERE key = $key", parameters, cancellationToken);
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
		var objResult = await ExecuteAsync("SELECT * FROM object WHERE key = $key", parameters, cancellationToken);
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

		// Verify the object exists
		var typedResult = await ExecuteAsync(
			"SELECT * FROM player, room, thing, exit WHERE key = $key",
			new Dictionary<string, object?> { ["key"] = objKey }, cancellationToken);

		var typedRecords = typedResult.GetValue<List<PlayerRecord>>(0)!;
		if (typedRecords.Count == 0) return false;

		var typedRecordKey = typedRecords[0].key;
		// Determine the SurrealDB record ID for this typed node
		var objParams2 = new Dictionary<string, object?> { ["key"] = objKey };
		var objResult2 = await ExecuteAsync("SELECT * FROM object WHERE key = $key", objParams2, cancellationToken);
		var objRecords2 = objResult2.GetValue<List<ObjectRecord>>(0)!;
		var typedRecordId = objRecords2.Count > 0
			? GetSurrealRecordId(objRecords2[0].type.ToLower(), objKey)
			: $"player:{objKey}";

		// Walk or create the attribute path
		string currentParentRecordId = typedRecordId;
		string? lastAttrKey = null;

		for (var i = 0; i < attribute.Length; i++)
		{
			var attrName = attribute[i];
			var longName = string.Join('`', attribute.Take(i + 1));
			var attrKey = $"{objKey}_{longName}";
			var isLast = i == attribute.Length - 1;
			var attrValue = isLast ? serializedValue : "";

			// Upsert the attribute
			var upsertParams = new Dictionary<string, object?>
			{
				["key"] = attrKey,
				["name"] = attrName,
				["longName"] = longName,
				["value"] = attrValue
			};

			await ExecuteAsync(
				"UPSERT attribute:⟨$key⟩ SET key = $key, name = $name, longName = $longName, value = $value",
				upsertParams, cancellationToken);

			// Ensure the has_attribute edge exists from parent to this attribute
			var edgeParams = new Dictionary<string, object?>
			{
				["parentId"] = currentParentRecordId,
				["childKey"] = attrKey
			};

			// Use a conditional query: create edge only if not existing
			if (i == 0)
			{
				// Parent is a typed object node (player/room/thing/exit)
				await ExecuteAsync(
					$"IF (SELECT * FROM has_attribute WHERE in = {currentParentRecordId} AND out = attribute:⟨$childKey⟩).len() == 0 {{ RELATE {currentParentRecordId}->has_attribute->attribute:⟨$childKey⟩ }}",
					edgeParams, cancellationToken);
			}
			else
			{
				var prevAttrKey = $"{objKey}_{string.Join('`', attribute.Take(i))}";
				var innerEdgeParams = new Dictionary<string, object?>
				{
					["parentKey"] = prevAttrKey,
					["childKey"] = attrKey
				};
				await ExecuteAsync(
					"IF (SELECT * FROM has_attribute WHERE in = attribute:⟨$parentKey⟩ AND out = attribute:⟨$childKey⟩).len() == 0 { RELATE attribute:⟨$parentKey⟩->has_attribute->attribute:⟨$childKey⟩ }",
					innerEdgeParams, cancellationToken);
			}

			currentParentRecordId = $"attribute:⟨{attrKey}⟩";
			lastAttrKey = attrKey;
		}

		if (lastAttrKey == null) return false;

		// Set ownership: remove old owner edge and create new one
		var ownerParams = new Dictionary<string, object?>
		{
			["attrKey"] = lastAttrKey,
			["ownerKey"] = ownerKey
		};

		await ExecuteAsync(
			"DELETE has_attribute_owner WHERE in = attribute:⟨$attrKey⟩",
			ownerParams, cancellationToken);

		await ExecuteAsync(
			"RELATE attribute:⟨$attrKey⟩->has_attribute_owner->player:$ownerKey",
			ownerParams, cancellationToken);

		// Handle attribute entry flags for newly created attributes
		for (var i = 0; i < attribute.Length; i++)
		{
			var longName = string.Join('`', attribute.Take(i + 1));
			var attrEntry = await GetSharpAttributeEntry(longName, cancellationToken);
			var flagNames = attrEntry?.DefaultFlags ?? [];

			var attrKey = $"{objKey}_{longName}";
			foreach (var flagName in flagNames)
			{
				var flagParams = new Dictionary<string, object?>
				{
					["attrKey"] = attrKey,
					["flagName"] = flagName
				};

				await ExecuteAsync(
					"RELATE attribute:⟨$attrKey⟩->has_attribute_flag->(SELECT VALUE id FROM attribute_flag WHERE string::uppercase(name) = string::uppercase($flagName) AND id NOT IN (SELECT VALUE out FROM has_attribute_flag WHERE in = attribute:⟨$attrKey⟩) LIMIT 1)",
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

		// Check for children
		var childParams = new Dictionary<string, object?> { ["key"] = attrKey };
		var childrenResult = await ExecuteAsync(
			"SELECT count() AS cnt FROM has_attribute WHERE in = type::thing('attribute', $key) GROUP ALL",
			childParams, cancellationToken);

		var records = childrenResult.GetValue<List<CountRecord>>(0)!;
		var hasChildren = records.Count > 0 && records[0].cnt > 0;

		if (hasChildren)
		{
			// Just clear the value
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
			// Remove the attribute entirely (edges are removed with DELETE on relations)
			var deleteParams = new Dictionary<string, object?> { ["key"] = attrKey };
			await ExecuteAsync("DELETE has_attribute WHERE out = attribute:⟨$key⟩", deleteParams, cancellationToken);
			await ExecuteAsync("DELETE has_attribute_flag WHERE in = attribute:⟨$key⟩", deleteParams, cancellationToken);
			await ExecuteAsync("DELETE has_attribute_owner WHERE in = attribute:⟨$key⟩", deleteParams, cancellationToken);
			await ExecuteAsync("DELETE has_attribute_entry WHERE in = attribute:⟨$key⟩", deleteParams, cancellationToken);
			await ExecuteAsync("DELETE attribute:⟨$key⟩", deleteParams, cancellationToken);
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

		// Delete all descendants recursively
		await WipeAttributeDescendantsAsync(attrKey, cancellationToken);

		// Delete the target itself
		var deleteParams = new Dictionary<string, object?> { ["key"] = attrKey };
		await ExecuteAsync("DELETE has_attribute WHERE out = attribute:⟨$key⟩", deleteParams, cancellationToken);
		await ExecuteAsync("DELETE has_attribute WHERE in = attribute:⟨$key⟩", deleteParams, cancellationToken);
		await ExecuteAsync("DELETE has_attribute_flag WHERE in = attribute:⟨$key⟩", deleteParams, cancellationToken);
		await ExecuteAsync("DELETE has_attribute_owner WHERE in = attribute:⟨$key⟩", deleteParams, cancellationToken);
		await ExecuteAsync("DELETE has_attribute_entry WHERE in = attribute:⟨$key⟩", deleteParams, cancellationToken);
		await ExecuteAsync("DELETE attribute:⟨$key⟩", deleteParams, cancellationToken);

		return true;
	}

	private async ValueTask WipeAttributeDescendantsAsync(string attrKey, CancellationToken ct)
	{
		var parameters = new Dictionary<string, object?> { ["key"] = attrKey };
		var result = await ExecuteAsync(
			"SELECT VALUE out.key FROM has_attribute WHERE in = type::thing('attribute', $key)",
			parameters, ct);

		var childKeys = result.GetValue<List<string>>(0)!;

		foreach (var childKey in childKeys)
		{
			if (string.IsNullOrEmpty(childKey)) continue;

			// Recurse into children first
			await WipeAttributeDescendantsAsync(childKey, ct);

			// Then delete this child
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
		var parentParams = new Dictionary<string, object?> { ["key"] = objKey };
		var parentChain = await GetParentChainAsync(objKey, cancellationToken);

		foreach (var parentKey in parentChain)
		{
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
		var allKeys = new List<int> { objKey };
		allKeys.AddRange(parentChain);

		foreach (var chainKey in allKeys)
		{
			var zoneParams = new Dictionary<string, object?> { ["key"] = chainKey };
			var zoneResult = await ExecuteAsync(
				"SELECT VALUE out.key FROM has_zone WHERE in = type::thing('object', $key)",
				zoneParams, cancellationToken);

			var zoneKeys = zoneResult.GetValue<List<int>>(0)!;

			foreach (var zoneKey in zoneKeys)
			{
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
				var flags = lastAttr.Flags.Where(f => f.Inheritable);
				yield return new LazyAttributeWithInheritance(parentAttrs, parentDbRef, AttributeSource.Parent, flags);
				yield break;
			}
		}

		// Try zones (on self and parents)
		var allKeys = new List<int> { objKey };
		allKeys.AddRange(parentChain);

		foreach (var chainKey in allKeys)
		{
			var zoneParams = new Dictionary<string, object?> { ["key"] = chainKey };
			var zoneResult = await ExecuteAsync(
				"SELECT VALUE out.key FROM has_zone WHERE in = type::thing('object', $key)",
				zoneParams, cancellationToken);

			var zoneKeys = zoneResult.GetValue<List<int>>(0)!;

			foreach (var zoneKey in zoneKeys)
			{
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

		// Find all attributes owned by old owner, reassign to new owner
		await ExecuteAsync("""
			LET $edges = (SELECT * FROM has_attribute_owner WHERE out = player:$oldKey);
			FOR $edge IN $edges {
				RELATE $edge.in->has_attribute_owner->player:$newKey;
			};
			DELETE has_attribute_owner WHERE out = player:$oldKey
			""", parameters, cancellationToken);
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
				"SELECT VALUE out.key FROM has_parent WHERE in = type::thing('object', $key)",
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
