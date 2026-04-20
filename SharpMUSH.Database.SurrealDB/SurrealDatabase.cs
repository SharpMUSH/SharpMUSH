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

public partial class SurrealDatabase(
	ILogger<SurrealDatabase> logger,
	ISurrealDbClient db,
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

	/// <summary>
	/// Wraps an <see cref="IAsyncEnumerable{T}"/> factory so that every call to
	/// <see cref="IAsyncEnumerable{T}.GetAsyncEnumerator"/> creates a brand-new
	/// <c>async IAsyncEnumerable</c> state machine instead of cloning the original.
	/// This prevents the <see cref="System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore{T}"/>
	/// race condition that occurs when the same cached state machine is concurrently enumerated
	/// (e.g. via <c>.GetAwaiter().GetResult()</c> in lock-expression trees).
	/// </summary>
	private sealed class FreshEnumerable<T>(Func<IAsyncEnumerable<T>> factory) : IAsyncEnumerable<T>
	{
		public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
			=> factory().GetAsyncEnumerator(cancellationToken);
	}

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
	/// Converts a partial-match regex to a full-match regex for SurrealDB.
	/// SurrealDB's regex matching does full-string matching,
	/// so we add .* anchors as needed to simulate partial matching.
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
	/// Executes a SurrealQL query and returns the response.
	/// </summary>
	private async ValueTask<SurrealDbResponse> ExecuteAsync(
		string query,
		CancellationToken ct = default)
	{
		logger.LogDebug("Executing SurrealQL: {Query}", query);
		var response = await db.RawQuery(query, ct);
		if (response.HasErrors)
		{
			var errors = string.Join("; ", response.Errors.Select(e => e.ToString()));
			logger.LogError("SurrealDB query error: {Errors} for query: {Query}", errors, query);
		}
		return response;
	}

	/// <summary>
	/// Executes a SurrealQL query with parameters and returns the response.
	/// </summary>
	private async ValueTask<SurrealDbResponse> ExecuteAsync(
		string query,
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken ct = default)
	{
		logger.LogDebug("Executing SurrealQL: {Query} with params: {Params}", query, parameters.Keys);
		var response = await db.RawQuery(query, parameters, ct);
		if (response.HasErrors)
		{
			var errors = string.Join("; ", response.Errors.Select(e => e.ToString()));
			logger.LogError("SurrealDB query error: {Errors} for query: {Query}", errors, query);
		}
		return response;
	}

	private async ValueTask<int> GetNextObjectKeyAsync(CancellationToken ct = default)
	{
		var response = await ExecuteAsync(
			"UPSERT counter:object_key SET value = (value ?? 0) + 1 RETURN AFTER", ct);
		var result = response.GetValue<List<JsonElement>>(0);
		return result[0].GetProperty("value").GetInt32();
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

	/// <summary>
	/// Safely gets an int value from a JsonElement, returning a default if the property is missing.
	/// </summary>
	private static int GetIntOrDefault(JsonElement element, string property, int defaultValue = 0)
	{
		if (element.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.Number)
			return prop.GetInt32();
		return defaultValue;
	}

	/// <summary>
	/// Safely gets a long value from a JsonElement, returning a default if the property is missing.
	/// </summary>
	private static long GetLongOrDefault(JsonElement element, string property, long defaultValue = 0)
	{
		if (element.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.Number)
			return prop.GetInt64();
		return defaultValue;
	}

	/// <summary>
	/// Safely gets a string value from a JsonElement, returning null if the property is missing.
	/// </summary>
	private static string? GetStringOrNull(JsonElement element, string property)
	{
		if (element.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String)
			return prop.GetString();
		return null;
	}

	/// <summary>
	/// Safely gets a string value from a JsonElement, returning a default if the property is missing.
	/// </summary>
	private static string GetStringOrDefault(JsonElement element, string property, string defaultValue = "")
	{
		if (element.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String)
			return prop.GetString() ?? defaultValue;
		return defaultValue;
	}

	/// <summary>
	/// Safely gets a bool value from a JsonElement.
	/// </summary>
	private static bool GetBoolOrDefault(JsonElement element, string property, bool defaultValue = false)
	{
		if (element.TryGetProperty(property, out var prop))
		{
			if (prop.ValueKind == JsonValueKind.True) return true;
			if (prop.ValueKind == JsonValueKind.False) return false;
		}
		return defaultValue;
	}

	/// <summary>
	/// Safely gets a string array from a JsonElement.
	/// </summary>
	private static string[] GetStringArrayOrEmpty(JsonElement element, string property)
	{
		if (element.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.Array)
			return prop.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
		return [];
	}

	private SharpObject MapElementToSharpObject(JsonElement element)
	{
		var key = GetIntOrDefault(element, "key");
		var name = GetStringOrDefault(element, "name");
		var type = GetStringOrDefault(element, "type");
		var creationTime = GetLongOrDefault(element, "creationTime");
		var modifiedTime = GetLongOrDefault(element, "modifiedTime");
		var warnings = (WarningType)GetIntOrDefault(element, "warnings");
		var locksJson = GetStringOrNull(element, "locks");
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
			Flags = new(() => new FreshEnumerable<SharpObjectFlag>(() => GetObjectFlagsForIdAsync(id, type.ToUpper(), CancellationToken.None))),
			Powers = new(() => new FreshEnumerable<SharpPower>(() => GetPowersForIdAsync(id, CancellationToken.None))),
			Attributes = new(() => new FreshEnumerable<SharpAttribute>(() => GetTopLevelAttributesAsync(id, CancellationToken.None))),
			LazyAttributes = new(() => new FreshEnumerable<LazySharpAttribute>(() => GetTopLevelLazyAttributesAsync(id, CancellationToken.None))),
			AllAttributes = new(() => new FreshEnumerable<SharpAttribute>(() => GetAllAttributesForIdAsync(id, CancellationToken.None))),
			LazyAllAttributes = new(() => new FreshEnumerable<LazySharpAttribute>(() => GetAllLazyAttributesForIdAsync(id, CancellationToken.None))),
			Owner = new(async ct => await GetObjectOwnerAsync(id, ct)),
			Parent = new(async ct => await GetParentForObjectAsync(id, ct)),
			Zone = new(async ct => await GetZoneAsync(id, ct)),
			Children = new(() => new FreshEnumerable<SharpObject>(() => GetChildrenAsync(id, CancellationToken.None)!))
		};
	}

	private async ValueTask<AnyOptionalSharpObject> BuildTypedObjectFromObjectElement(JsonElement objElement, CancellationToken ct)
	{
		var key = GetIntOrDefault(objElement, "key");
		var type = GetStringOrDefault(objElement, "type");
		var sharpObj = MapElementToSharpObject(objElement);

		var parameters = new Dictionary<string, object?> { ["key"] = key };
		var typedResult = await ExecuteAsync(
			"SELECT * FROM player, room, thing, exit WHERE key = $key",
			parameters, ct);

		var typedRecords = typedResult.GetValue<List<JsonElement>>(0);
		if (typedRecords.Count == 0) return new None();

		var typedElement = typedRecords[0];
		var typedId = GetTypedId(type, key);

		return type.ToUpper() switch
		{
			"PLAYER" => BuildPlayer(typedId, typedElement, sharpObj),
			"ROOM" => BuildRoom(typedId, sharpObj),
			"THING" => BuildThing(typedId, sharpObj),
			"EXIT" => BuildExit(typedId, typedElement, sharpObj),
			_ => throw new ArgumentException($"Invalid Object Type: '{type}'")
		};
	}

	private async ValueTask<AnyOptionalSharpObject> BuildTypedObjectFromKey(int key, CancellationToken ct)
	{
		var parameters = new Dictionary<string, object?> { ["key"] = key };
		var objResult = await ExecuteAsync(
			"SELECT * FROM object WHERE key = $key",
			parameters, ct);

		var objRecords = objResult.GetValue<List<JsonElement>>(0);
		if (objRecords.Count == 0) return new None();

		return await BuildTypedObjectFromObjectElement(objRecords[0], ct);
	}

	private string GetTypedId(string type, int key)
	{
		return type.ToUpper() switch
		{
			"PLAYER" => PlayerId(key),
			"ROOM" => RoomId(key),
			"THING" => ThingId(key),
			"EXIT" => ExitId(key),
			_ => throw new ArgumentException($"Unknown object type: {type}")
		};
	}

	private SharpPlayer BuildPlayer(string id, JsonElement typedElement, SharpObject sharpObj)
	{
		var aliases = GetStringArrayOrEmpty(typedElement, "aliases");
		return new SharpPlayer
		{
			Id = id,
			Object = sharpObj,
			Aliases = aliases,
			PasswordHash = GetStringOrDefault(typedElement, "passwordHash"),
			PasswordSalt = GetStringOrNull(typedElement, "passwordSalt"),
			Quota = GetIntOrDefault(typedElement, "quota"),
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

	private SharpExit BuildExit(string id, JsonElement typedElement, SharpObject sharpObj)
	{
		var aliases = GetStringArrayOrEmpty(typedElement, "aliases");
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
		var parameters = new Dictionary<string, object?> { ["key"] = key };
		var result = await ExecuteAsync(
			"SELECT ->at_location->object.* AS dest FROM type::thing('player', $key), type::thing('thing', $key), type::thing('exit', $key)",
			parameters, ct);

		var records = result.GetValue<List<JsonElement>>(0);
		if (records.Count == 0)
			throw new InvalidOperationException($"No location found for {typedId}");

		// Traverse the result to find the destination object
		var destArray = records[0].GetProperty("dest");
		if (destArray.ValueKind != JsonValueKind.Array || destArray.GetArrayLength() == 0)
			throw new InvalidOperationException($"No location found for {typedId}");

		var destObj = destArray[0];
		var located = await BuildTypedObjectFromObjectElement(destObj, ct);
		return located.Match<AnySharpContainer>(
			player => player,
			room => room,
			_ => throw new InvalidOperationException($"Invalid location for {typedId}: Exit objects cannot be locations"),
			thing => thing,
			_ => throw new InvalidOperationException($"No location found for {typedId}"));
	}

	private async ValueTask<AnySharpContainer> GetHomeAsync(string typedId, CancellationToken ct)
	{
		var key = ExtractKey(typedId);
		var parameters = new Dictionary<string, object?> { ["key"] = key };
		var result = await ExecuteAsync(
			"SELECT ->has_home->object.* AS dest FROM type::thing('player', $key), type::thing('thing', $key), type::thing('exit', $key)",
			parameters, ct);

		var records = result.GetValue<List<JsonElement>>(0);
		if (records.Count == 0)
			throw new InvalidOperationException($"No home found for {typedId}");

		var destArray = records[0].GetProperty("dest");
		if (destArray.ValueKind != JsonValueKind.Array || destArray.GetArrayLength() == 0)
			throw new InvalidOperationException($"No home found for {typedId}");

		var destObj = destArray[0];
		var homeObj = await BuildTypedObjectFromObjectElement(destObj, ct);
		return homeObj.Match<AnySharpContainer>(
			player => player,
			room => room,
			_ => throw new InvalidOperationException($"Invalid home for {typedId}: Exit objects cannot be homes"),
			thing => thing,
			_ => throw new InvalidOperationException($"No home found for {typedId}"));
	}

	private async ValueTask<AnyOptionalSharpContainer> GetDropToAsync(string roomId, CancellationToken ct)
	{
		var key = ExtractKey(roomId);
		var parameters = new Dictionary<string, object?> { ["key"] = key };
		var result = await ExecuteAsync(
			"SELECT ->has_home->object.* AS dest FROM type::thing('room', $key)",
			parameters, ct);

		var records = result.GetValue<List<JsonElement>>(0);
		if (records.Count == 0) return new None();

		var destArray = records[0].GetProperty("dest");
		if (destArray.ValueKind != JsonValueKind.Array || destArray.GetArrayLength() == 0)
			return new None();

		var destObj = destArray[0];
		var dropToObj = await BuildTypedObjectFromObjectElement(destObj, ct);
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
		var parameters = new Dictionary<string, object?> { ["key"] = key };
		var result = await ExecuteAsync(
			"SELECT ->has_owner->player.* AS owner FROM type::thing('object', $key)",
			parameters, ct);

		var records = result.GetValue<List<JsonElement>>(0);
		if (records.Count == 0)
			throw new InvalidOperationException($"No owner found for {objectId}");

		var ownerArray = records[0].GetProperty("owner");
		if (ownerArray.ValueKind != JsonValueKind.Array || ownerArray.GetArrayLength() == 0)
			throw new InvalidOperationException($"No owner found for {objectId}");

		var ownerTypedElement = ownerArray[0];
		var ownerKey = GetIntOrDefault(ownerTypedElement, "key");

		// Get the object node for the owner
		var ownerObjParams = new Dictionary<string, object?> { ["key"] = ownerKey };
		var ownerObjResult = await ExecuteAsync(
			"SELECT * FROM object WHERE key = $key",
			ownerObjParams, ct);

		var ownerObjRecords = ownerObjResult.GetValue<List<JsonElement>>(0);
		if (ownerObjRecords.Count == 0)
			throw new InvalidOperationException($"No object record found for owner of {objectId}");

		var sharpObj = MapElementToSharpObject(ownerObjRecords[0]);
		return BuildPlayer(PlayerId(ownerKey), ownerTypedElement, sharpObj);
	}

	private async ValueTask<AnyOptionalSharpObject> GetParentForObjectAsync(string objectId, CancellationToken ct)
	{
		var key = ExtractKey(objectId);
		var parameters = new Dictionary<string, object?> { ["key"] = key };
		var result = await ExecuteAsync(
			"SELECT ->has_parent->object.* AS parent FROM type::thing('object', $key)",
			parameters, ct);

		var records = result.GetValue<List<JsonElement>>(0);
		if (records.Count == 0) return new None();

		var parentArray = records[0].GetProperty("parent");
		if (parentArray.ValueKind != JsonValueKind.Array || parentArray.GetArrayLength() == 0)
			return new None();

		var parentObj = parentArray[0];
		return await BuildTypedObjectFromObjectElement(parentObj, ct);
	}

	private async ValueTask<AnyOptionalSharpObject> GetZoneAsync(string objectId, CancellationToken ct)
	{
		var key = ExtractKey(objectId);
		var parameters = new Dictionary<string, object?> { ["key"] = key };
		var result = await ExecuteAsync(
			"SELECT ->has_zone->object.* AS zone FROM type::thing('object', $key)",
			parameters, ct);

		var records = result.GetValue<List<JsonElement>>(0);
		if (records.Count == 0) return new None();

		var zoneArray = records[0].GetProperty("zone");
		if (zoneArray.ValueKind != JsonValueKind.Array || zoneArray.GetArrayLength() == 0)
			return new None();

		var zoneObj = zoneArray[0];
		return await BuildTypedObjectFromObjectElement(zoneObj, ct);
	}

	private async IAsyncEnumerable<SharpObjectFlag> GetObjectFlagsForIdAsync(string objectId, string type, [EnumeratorCancellation] CancellationToken ct = default)
	{
		var key = ExtractKey(objectId);
		var parameters = new Dictionary<string, object?> { ["key"] = key };
		var result = await ExecuteAsync(
			"SELECT ->has_flags->object_flag.* AS flags FROM type::thing('object', $key)",
			parameters, ct);

		var records = result.GetValue<List<JsonElement>>(0);
		if (records.Count > 0)
		{
			var flagsArray = records[0].GetProperty("flags");
			if (flagsArray.ValueKind == JsonValueKind.Array)
			{
				foreach (var flagElement in flagsArray.EnumerateArray())
				{
					yield return MapElementToFlag(flagElement);
				}
			}
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
		var parameters = new Dictionary<string, object?> { ["key"] = key };
		var result = await ExecuteAsync(
			"SELECT ->has_powers->power.* AS powers FROM type::thing('object', $key)",
			parameters, ct);

		var records = result.GetValue<List<JsonElement>>(0);
		if (records.Count > 0)
		{
			var powersArray = records[0].GetProperty("powers");
			if (powersArray.ValueKind == JsonValueKind.Array)
			{
				foreach (var powerElement in powersArray.EnumerateArray())
				{
					yield return MapElementToPower(powerElement);
				}
			}
		}
	}

	private static SharpObjectFlag MapElementToFlag(JsonElement element)
	{
		var name = GetStringOrDefault(element, "name");
		return new SharpObjectFlag
		{
			Id = $"ObjectFlag/{name}",
			Name = name,
			Symbol = GetStringOrDefault(element, "symbol"),
			System = GetBoolOrDefault(element, "system"),
			Disabled = GetBoolOrDefault(element, "disabled"),
			Aliases = GetStringArrayOrEmpty(element, "aliases"),
			SetPermissions = GetStringArrayOrEmpty(element, "setPermissions"),
			UnsetPermissions = GetStringArrayOrEmpty(element, "unsetPermissions"),
			TypeRestrictions = GetStringArrayOrEmpty(element, "typeRestrictions")
		};
	}

	private static SharpPower MapElementToPower(JsonElement element)
	{
		var name = GetStringOrDefault(element, "name");
		return new SharpPower
		{
			Id = $"Power/{name}",
			Name = name,
			Alias = GetStringOrDefault(element, "alias"),
			System = GetBoolOrDefault(element, "system"),
			Disabled = GetBoolOrDefault(element, "disabled"),
			SetPermissions = GetStringArrayOrEmpty(element, "setPermissions"),
			UnsetPermissions = GetStringArrayOrEmpty(element, "unsetPermissions"),
			TypeRestrictions = GetStringArrayOrEmpty(element, "typeRestrictions")
		};
	}

	private static SharpAttributeFlag MapElementToAttributeFlag(JsonElement element)
	{
		var name = GetStringOrDefault(element, "name");
		return new SharpAttributeFlag
		{
			Id = $"AttributeFlag/{name}",
			Key = name,
			Name = name,
			Symbol = GetStringOrDefault(element, "symbol"),
			System = GetBoolOrDefault(element, "system"),
			Inheritable = GetBoolOrDefault(element, "inheritable")
		};
	}

	private static SharpAttributeEntry MapElementToAttributeEntry(JsonElement element)
	{
		return new SharpAttributeEntry
		{
			Id = $"AttributeEntry/{GetStringOrDefault(element, "name")}",
			Name = GetStringOrDefault(element, "name"),
			DefaultFlags = GetStringArrayOrEmpty(element, "defaultFlags"),
			Limit = GetStringOrNull(element, "lim"),
			Enum = element.TryGetProperty("enumValues", out var enumProp) && enumProp.ValueKind == JsonValueKind.Array
				? enumProp.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
				: null
		};
	}

	private async IAsyncEnumerable<SharpAttributeFlag> GetAttributeFlagsForAttrAsync(string attrId, [EnumeratorCancellation] CancellationToken ct = default)
	{
		var attrKey = ExtractKeyString(attrId);
		var parameters = new Dictionary<string, object?> { ["key"] = attrKey };
		var result = await ExecuteAsync(
			"SELECT ->has_attribute_flag->attribute_flag.* AS flags FROM type::thing('attribute', $key)",
			parameters, ct);

		var records = result.GetValue<List<JsonElement>>(0);
		if (records.Count > 0)
		{
			var flagsArray = records[0].GetProperty("flags");
			if (flagsArray.ValueKind == JsonValueKind.Array)
			{
				foreach (var flagElement in flagsArray.EnumerateArray())
				{
					yield return MapElementToAttributeFlag(flagElement);
				}
			}
		}
	}

	private async ValueTask<SharpPlayer?> GetAttributeOwnerAsync(string attrId, CancellationToken ct)
	{
		var attrKey = ExtractKeyString(attrId);
		var parameters = new Dictionary<string, object?> { ["key"] = attrKey };
		var result = await ExecuteAsync(
			"SELECT ->has_attribute_owner->player.* AS owner FROM type::thing('attribute', $key)",
			parameters, ct);

		var records = result.GetValue<List<JsonElement>>(0);
		if (records.Count == 0) return null;

		var ownerArray = records[0].GetProperty("owner");
		if (ownerArray.ValueKind != JsonValueKind.Array || ownerArray.GetArrayLength() == 0)
			return null;

		var playerElement = ownerArray[0];
		var pKey = GetIntOrDefault(playerElement, "key");

		var objParams = new Dictionary<string, object?> { ["key"] = pKey };
		var objResult = await ExecuteAsync("SELECT * FROM object WHERE key = $key", objParams, ct);
		var objRecords = objResult.GetValue<List<JsonElement>>(0);
		if (objRecords.Count == 0) return null;

		var sharpObj = MapElementToSharpObject(objRecords[0]);
		return BuildPlayer(PlayerId(pKey), playerElement, sharpObj);
	}

	private async ValueTask<SharpAttributeEntry?> GetRelatedAttributeEntryAsync(string attrId, CancellationToken ct)
	{
		var attrKey = ExtractKeyString(attrId);
		var parameters = new Dictionary<string, object?> { ["key"] = attrKey };
		var result = await ExecuteAsync(
			"SELECT ->has_attribute_entry->attribute_entry.* AS entries FROM type::thing('attribute', $key)",
			parameters, ct);

		var records = result.GetValue<List<JsonElement>>(0);
		if (records.Count == 0) return null;

		var entriesArray = records[0].GetProperty("entries");
		if (entriesArray.ValueKind != JsonValueKind.Array || entriesArray.GetArrayLength() == 0)
			return null;

		return MapElementToAttributeEntry(entriesArray[0]);
	}

	private async ValueTask<SharpAttribute> MapToSharpAttribute(JsonElement element, CancellationToken ct)
	{
		var key = GetStringOrDefault(element, "key");
		var id = AttributeId(key);
		var flags = await GetAttributeFlagsForAttrAsync(id, ct).ToArrayAsync(ct);
		return new SharpAttribute(
			id,
			key,
			GetStringOrDefault(element, "name"),
			flags,
			null,
			GetStringOrNull(element, "longName"),
			new AsyncLazy<IAsyncEnumerable<SharpAttribute>>(innerCt => Task.FromResult<IAsyncEnumerable<SharpAttribute>>(new FreshEnumerable<SharpAttribute>(() => GetTopLevelAttributesAsync(id, innerCt)))),
			new AsyncLazy<SharpPlayer?>(async innerCt => await GetAttributeOwnerAsync(id, innerCt)),
			new AsyncLazy<SharpAttributeEntry?>(async innerCt => await GetRelatedAttributeEntryAsync(id, innerCt)))
		{
			Value = MModule.deserialize(GetStringOrDefault(element, "value"))
		};
	}

	private async ValueTask<LazySharpAttribute> MapToLazySharpAttribute(JsonElement element, CancellationToken ct)
	{
		var key = GetStringOrDefault(element, "key");
		var id = AttributeId(key);
		var flags = await GetAttributeFlagsForAttrAsync(id, ct).ToArrayAsync(ct);
		return new LazySharpAttribute(
			id,
			key,
			GetStringOrDefault(element, "name"),
			flags,
			null,
			GetStringOrNull(element, "longName"),
			new AsyncLazy<IAsyncEnumerable<LazySharpAttribute>>(innerCt => Task.FromResult<IAsyncEnumerable<LazySharpAttribute>>(new FreshEnumerable<LazySharpAttribute>(() => GetTopLevelLazyAttributesAsync(id, innerCt)))),
			new AsyncLazy<SharpPlayer?>(async innerCt => await GetAttributeOwnerAsync(id, innerCt)),
			new AsyncLazy<SharpAttributeEntry?>(async innerCt => await GetRelatedAttributeEntryAsync(id, innerCt)),
			Value: new AsyncLazy<MString>(innerCt =>
				Task.FromResult(MModule.deserialize(GetStringOrDefault(element, "value")))));
	}

	private async IAsyncEnumerable<SharpAttribute> GetTopLevelAttributesAsync(string parentId, [EnumeratorCancellation] CancellationToken ct = default)
	{
		SurrealDbResponse result;
		if (parentId.StartsWith("Attribute"))
		{
			var key = ExtractKeyString(parentId);
			var parameters = new Dictionary<string, object?> { ["key"] = key };
			result = await ExecuteAsync(
				"SELECT ->has_attribute->attribute.* AS children FROM type::thing('attribute', $key)",
				parameters, ct);
		}
		else
		{
			// parentId is an Object ID — find the typed node first
			var objKey = ExtractKey(parentId);
			var parameters = new Dictionary<string, object?> { ["key"] = objKey };
			result = await ExecuteAsync(
				"SELECT ->has_attribute->attribute.* AS children FROM player, room, thing, exit WHERE key = $key",
				parameters, ct);
		}

		var records = result.GetValue<List<JsonElement>>(0);
		if (records.Count == 0) yield break;

		var childrenArray = records[0].GetProperty("children");
		if (childrenArray.ValueKind != JsonValueKind.Array) yield break;

		foreach (var child in childrenArray.EnumerateArray())
		{
			yield return await MapToSharpAttribute(child, ct);
		}
	}

	private async IAsyncEnumerable<LazySharpAttribute> GetTopLevelLazyAttributesAsync(string parentId, [EnumeratorCancellation] CancellationToken ct = default)
	{
		SurrealDbResponse result;
		if (parentId.StartsWith("Attribute"))
		{
			var key = ExtractKeyString(parentId);
			var parameters = new Dictionary<string, object?> { ["key"] = key };
			result = await ExecuteAsync(
				"SELECT ->has_attribute->attribute.* AS children FROM type::thing('attribute', $key)",
				parameters, ct);
		}
		else
		{
			var objKey = ExtractKey(parentId);
			var parameters = new Dictionary<string, object?> { ["key"] = objKey };
			result = await ExecuteAsync(
				"SELECT ->has_attribute->attribute.* AS children FROM player, room, thing, exit WHERE key = $key",
				parameters, ct);
		}

		var records = result.GetValue<List<JsonElement>>(0);
		if (records.Count == 0) yield break;

		var childrenArray = records[0].GetProperty("children");
		if (childrenArray.ValueKind != JsonValueKind.Array) yield break;

		foreach (var child in childrenArray.EnumerateArray())
		{
			yield return await MapToLazySharpAttribute(child, ct);
		}
	}

	private async IAsyncEnumerable<SharpAttribute> GetAllAttributesForIdAsync(string parentId, [EnumeratorCancellation] CancellationToken ct = default)
	{
		// SurrealDB doesn't have variable-depth traversal like Cypher's *1..999,
		// so we recursively gather all attributes
		await foreach (var attr in GetTopLevelAttributesAsync(parentId, ct))
		{
			yield return attr;
			await foreach (var child in GetAllAttributesForIdAsync(attr.Id, ct))
			{
				yield return child;
			}
		}
	}

	private async IAsyncEnumerable<LazySharpAttribute> GetAllLazyAttributesForIdAsync(string parentId, [EnumeratorCancellation] CancellationToken ct = default)
	{
		await foreach (var attr in GetTopLevelLazyAttributesAsync(parentId, ct))
		{
			yield return attr;
			await foreach (var child in GetAllLazyAttributesForIdAsync(attr.Id, ct))
			{
				yield return child;
			}
		}
	}

	private IAsyncEnumerable<SharpObject>? GetChildrenAsync(string objectId, CancellationToken ct = default)
	{
		return GetChildrenAsyncInner(objectId, ct);
	}

	private async IAsyncEnumerable<SharpObject> GetChildrenAsyncInner(string objectId, [EnumeratorCancellation] CancellationToken ct = default)
	{
		var key = ExtractKey(objectId);
		var parameters = new Dictionary<string, object?> { ["key"] = key };
		var result = await ExecuteAsync(
			"SELECT * FROM object WHERE key IN (SELECT VALUE in.key FROM has_parent WHERE out = type::thing('object', $key))",
			parameters, ct);

		var records = result.GetValue<List<JsonElement>>(0);
		foreach (var record in records)
		{
			yield return MapElementToSharpObject(record);
		}
	}

	[GeneratedRegex(@"\*\*|[.*+?^${}()|[\]/]")]
	private static partial Regex WildcardToRegex();

	#endregion
}
