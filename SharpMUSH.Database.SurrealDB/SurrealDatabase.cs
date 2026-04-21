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
	private static int _nextObjectKey;

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
	/// Extracts the SurrealDB table name from a typed ID like "Player/42" → "player".
	/// </summary>
	private static string ExtractTable(string typedId) => typedId.Split('/')[0].ToLower();

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

	private static string FormatError(ISurrealDbErrorResult error) =>
		error is SurrealDbErrorResult concrete ? (concrete.Details ?? concrete.Status) : error.GetType().Name;

	/// <summary>
	/// Executes a SurrealQL query and returns the response.
	/// </summary>
	private async ValueTask<SurrealDbResponse> ExecuteAsync(
		string query,
		CancellationToken ct = default)
	{
		logger.LogDebug("Executing SurrealQL: {Query}", query);
		var response = await db.RawQuery(query, null, ct);
		if (response.HasErrors)
		{
			var errors = string.Join("; ", response.Errors.Select(FormatError));
			logger.LogError("SurrealDB query error: {Errors} for query: {Query}", errors, query);
		}
		return response;
	}

	/// <summary>
	/// Executes a SurrealQL query with parameters and returns the response.
	/// Since the SurrealDB embedded CBOR serializer cannot handle Dictionary&lt;string, object?&gt;
	/// with mixed value types, we inline parameter values directly into the query string.
	/// All string values are escaped via <see cref="EscapeString"/> to prevent SurrealQL injection.
	/// </summary>
	private async ValueTask<SurrealDbResponse> ExecuteAsync(
		string query,
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken ct = default)
	{
		// Replace $param references with their serialized values inline.
		// Special handling: when $param appears inside ⟨...⟩ (record ID context),
		// use the raw value without string quotes.
		var expandedQuery = query;
		foreach (var kvp in parameters.OrderByDescending(k => k.Key.Length))
		{
			var paramToken = $"${kvp.Key}";
			var serialized = SerializeValue(kvp.Value);
			var rawValue = SerializeValueRaw(kvp.Value);

			// Replace occurrences inside ⟨...⟩ with raw value (no quotes for strings)
			expandedQuery = System.Text.RegularExpressions.Regex.Replace(
				expandedQuery,
				$@"⟨([^⟩]*?){Regex.Escape(paramToken)}([^⟩]*?)⟩",
				m => $"⟨{m.Groups[1].Value}{rawValue}{m.Groups[2].Value}⟩");

			// Replace remaining occurrences with quoted value
			expandedQuery = expandedQuery.Replace(paramToken, serialized);
		}

		// Log the query template (not the expanded query) to avoid leaking sensitive parameter values
		logger.LogDebug("Executing SurrealQL: {Query}", query);
		var response = await db.RawQuery(expandedQuery, null, ct);
		if (response.HasErrors)
		{
			var errors = string.Join("; ", response.Errors.Select(FormatError));
			logger.LogError("SurrealDB query error: {Errors} for query: {Query}", errors, query);
		}
		return response;
	}

	/// <summary>
	/// Serializes a value to a SurrealQL literal string (with quotes for strings).
	/// </summary>
	private static string SerializeValue(object? value) => value switch
	{
		null => "NONE",
		string s => $"'{EscapeString(s)}'",
		int i => i.ToString(),
		long l => l.ToString(),
		double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
		float f => f.ToString(System.Globalization.CultureInfo.InvariantCulture),
		bool b => b ? "true" : "false",
		string[] arr => $"[{string.Join(", ", arr.Select(a => $"'{EscapeString(a)}'"))}]",
		int[] arr => $"[{string.Join(", ", arr)}]",
		IEnumerable<string> arr => $"[{string.Join(", ", arr.Select(a => $"'{EscapeString(a)}'"))}]",
		_ => $"'{EscapeString(value.ToString() ?? "")}'",
	};

	private static string EscapeString(string s) => s.Replace("\\", "\\\\").Replace("'", "\\'");

	/// <summary>
	/// Serializes a value without string quotes (for use inside record ID brackets ⟨...⟩).
	/// </summary>
	private static string SerializeValueRaw(object? value) => value switch
	{
		null => "",
		string s => s,
		int i => i.ToString(),
		long l => l.ToString(),
		_ => value.ToString() ?? "",
	};

	/// <summary>
	/// Escapes a string for use as a SurrealDB record ID inside ⟨...⟩ brackets.
	/// </summary>
	private static string EscapeRecordId(string s) => s;

	private ValueTask<int> GetNextObjectKeyAsync(CancellationToken ct = default)
	{
		// Use an in-memory atomic counter to avoid SurrealDB UPSERT transaction conflicts
		// under parallel test execution. The counter is initialized during migration.
		return ValueTask.FromResult(Interlocked.Increment(ref _nextObjectKey));
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

	private SharpObject MapRecordToSharpObject(ObjectRecord record)
	{
		var key = record.key;
		var name = record.name;
		var type = record.type;
		var creationTime = record.creationTime;
		var modifiedTime = record.modifiedTime;
		var warnings = (WarningType)record.warnings;
		var locksJson = record.locks;
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

	private async ValueTask<AnyOptionalSharpObject> BuildTypedObjectFromObjectRecord(ObjectRecord objRecord, CancellationToken ct)
	{
		var key = objRecord.key;
		var type = objRecord.type;
		var sharpObj = MapRecordToSharpObject(objRecord);
		var parameters = new Dictionary<string, object?> { ["key"] = key };
		var typedId = GetTypedId(type, key);

		switch (type.ToUpper())
		{
			case "PLAYER":
				var playerResult = await ExecuteAsync("SELECT * FROM player:$key", parameters, ct);
				var players = playerResult.GetValue<List<PlayerRecord>>(0)!;
				if (players.Count == 0) return new None();
				return BuildPlayer(typedId, players[0], sharpObj);
			case "ROOM":
				return BuildRoom(typedId, sharpObj);
			case "THING":
				return BuildThing(typedId, sharpObj);
			case "EXIT":
				var exitResult = await ExecuteAsync("SELECT * FROM exit:$key", parameters, ct);
				var exits = exitResult.GetValue<List<ExitRecord>>(0)!;
				if (exits.Count == 0) return new None();
				return BuildExit(typedId, exits[0], sharpObj);
			default:
				throw new ArgumentException($"Invalid Object Type: '{type}'");
		}
	}

	private async ValueTask<AnyOptionalSharpObject> BuildTypedObjectFromKey(int key, CancellationToken ct)
	{
		var parameters = new Dictionary<string, object?> { ["key"] = key };
		var objResult = await ExecuteAsync(
			"SELECT * FROM object:$key",
			parameters, ct);

		var objRecords = objResult.GetValue<List<ObjectRecord>>(0)!;
		if (objRecords.Count == 0) return new None();

		return await BuildTypedObjectFromObjectRecord(objRecords[0], ct);
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

	private string GetTypedIdFromObjectRecord(ObjectRecord record)
	{
		return GetTypedId(record.type, record.key);
	}

	private static string GetSurrealRecordId(string type, int key)
	{
		return $"{type.ToLower()}:{key}";
	}

	private SharpPlayer BuildPlayer(string id, PlayerRecord playerRecord, SharpObject sharpObj)
	{
		return new SharpPlayer
		{
			Id = id,
			Object = sharpObj,
			Aliases = playerRecord.aliases,
			PasswordHash = playerRecord.passwordHash,
			PasswordSalt = playerRecord.passwordSalt,
			Quota = playerRecord.quota,
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

	private SharpExit BuildExit(string id, ExitRecord exitRecord, SharpObject sharpObj)
	{
		return new SharpExit
		{
			Id = id,
			Object = sharpObj,
			Aliases = exitRecord.aliases,
			Location = new(async ct => await GetLocationForTypedAsync(id, ct)),
			Home = new(async ct => await GetHomeAsync(id, ct))
		};
	}

	private async ValueTask<AnySharpContainer> GetLocationForTypedAsync(string typedId, CancellationToken ct)
	{
		var key = ExtractKey(typedId);
		var table = ExtractTable(typedId);
		var parameters = new Dictionary<string, object?> { ["key"] = key };
		var result = await ExecuteAsync(
			$"SELECT VALUE out.key FROM at_location WHERE in = {table}:$key",
			parameters, ct);

		var destKeys = result.GetValue<List<int>>(0)!;
		if (destKeys.Count == 0)
			throw new InvalidOperationException($"No location found for {typedId}");

		var destKey = destKeys[0];
		var located = await BuildTypedObjectFromKey(destKey, ct);
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
		var table = ExtractTable(typedId);
		var parameters = new Dictionary<string, object?> { ["key"] = key };
		var result = await ExecuteAsync(
			$"SELECT VALUE out.key FROM has_home WHERE in = {table}:$key",
			parameters, ct);

		var destKeys = result.GetValue<List<int>>(0)!;
		if (destKeys.Count == 0)
			throw new InvalidOperationException($"No home found for {typedId}");

		var destKey = destKeys[0];
		var homeObj = await BuildTypedObjectFromKey(destKey, ct);
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
			"SELECT VALUE out.key FROM has_home WHERE in = room:$key",
			parameters, ct);

		var destKeys = result.GetValue<List<int>>(0)!;
		if (destKeys.Count == 0) return new None();

		var destKey = destKeys[0];
		var dropToObj = await BuildTypedObjectFromKey(destKey, ct);
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

		// Get the owner's player record via graph traversal
		var ownerResult = await ExecuteAsync(
			"SELECT * FROM object:$key->has_owner->player",
			parameters, ct);
		var ownerPlayers = ownerResult.GetValue<List<PlayerRecord>>(0)!;
		if (ownerPlayers.Count == 0)
			throw new InvalidOperationException($"No owner found for {objectId}");

		var ownerPlayerRecord = ownerPlayers[0];
		var ownerKey = ownerPlayerRecord.key;

		// Get the object record for the owner via direct record ID
		var ownerObjParams = new Dictionary<string, object?> { ["key"] = ownerKey };
		var ownerObjResult = await ExecuteAsync(
			"SELECT * FROM object:$key",
			ownerObjParams, ct);
		var ownerObjRecords = ownerObjResult.GetValue<List<ObjectRecord>>(0)!;
		if (ownerObjRecords.Count == 0)
			throw new InvalidOperationException($"No object record found for owner of {objectId}");

		var sharpObj = MapRecordToSharpObject(ownerObjRecords[0]);
		return BuildPlayer(PlayerId(ownerKey), ownerPlayerRecord, sharpObj);
	}

	private async ValueTask<AnyOptionalSharpObject> GetParentForObjectAsync(string objectId, CancellationToken ct)
	{
		var key = ExtractKey(objectId);
		var parameters = new Dictionary<string, object?> { ["key"] = key };
		var result = await ExecuteAsync(
			"SELECT * FROM object:$key->has_parent->object",
			parameters, ct);

		var records = result.GetValue<List<ObjectRecord>>(0)!;
		if (records.Count == 0) return new None();

		return await BuildTypedObjectFromObjectRecord(records[0], ct);
	}

	private async ValueTask<AnyOptionalSharpObject> GetZoneAsync(string objectId, CancellationToken ct)
	{
		var key = ExtractKey(objectId);
		var parameters = new Dictionary<string, object?> { ["key"] = key };
		var result = await ExecuteAsync(
			"SELECT * FROM object:$key->has_zone->object",
			parameters, ct);

		var records = result.GetValue<List<ObjectRecord>>(0)!;
		if (records.Count == 0) return new None();

		return await BuildTypedObjectFromObjectRecord(records[0], ct);
	}

	private async IAsyncEnumerable<SharpObjectFlag> GetObjectFlagsForIdAsync(string objectId, string type, [EnumeratorCancellation] CancellationToken ct = default)
	{
		var key = ExtractKey(objectId);
		var parameters = new Dictionary<string, object?> { ["key"] = key };
		var result = await ExecuteAsync(
			"SELECT * FROM object:$key->has_flags->object_flag",
			parameters, ct);

		var records = result.GetValue<List<FlagRecord>>(0)!;
		foreach (var record in records)
		{
			yield return MapRecordToFlag(record);
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
			"SELECT * FROM object:$key->has_powers->power",
			parameters, ct);

		var records = result.GetValue<List<PowerRecord>>(0)!;
		foreach (var record in records)
		{
			yield return MapRecordToPower(record);
		}
	}

	private static SharpObjectFlag MapRecordToFlag(FlagRecord record)
	{
		return new SharpObjectFlag
		{
			Id = $"ObjectFlag/{record.name}",
			Name = record.name,
			Symbol = record.symbol,
			System = record.system,
			Disabled = record.disabled,
			Aliases = record.aliases,
			SetPermissions = record.setPermissions,
			UnsetPermissions = record.unsetPermissions,
			TypeRestrictions = record.typeRestrictions
		};
	}

	private static SharpPower MapRecordToPower(PowerRecord record)
	{
		return new SharpPower
		{
			Id = $"Power/{record.name}",
			Name = record.name,
			Alias = record.alias,
			System = record.system,
			Disabled = record.disabled,
			SetPermissions = record.setPermissions,
			UnsetPermissions = record.unsetPermissions,
			TypeRestrictions = record.typeRestrictions
		};
	}

	private static SharpAttributeFlag MapRecordToAttributeFlag(AttributeFlagRecord record)
	{
		return new SharpAttributeFlag
		{
			Id = $"AttributeFlag/{record.name}",
			Key = record.name,
			Name = record.name,
			Symbol = record.symbol,
			System = record.system,
			Inheritable = record.inheritable
		};
	}

	private static SharpAttributeEntry MapRecordToAttributeEntry(AttributeEntryRecord record)
	{
		return new SharpAttributeEntry
		{
			Id = $"AttributeEntry/{record.name}",
			Name = record.name,
			DefaultFlags = record.defaultFlags,
			Limit = string.IsNullOrEmpty(record.lim) ? null : record.lim,
			Enum = record.enumValues.Length > 0 ? record.enumValues : null
		};
	}

	private async IAsyncEnumerable<SharpAttributeFlag> GetAttributeFlagsForAttrAsync(string attrId, [EnumeratorCancellation] CancellationToken ct = default)
	{
		var attrKey = ExtractKeyString(attrId);
		var parameters = new Dictionary<string, object?> { ["key"] = attrKey };
		var result = await ExecuteAsync(
			"SELECT * FROM attribute:⟨$key⟩->has_attribute_flag->attribute_flag",
			parameters, ct);

		var records = result.GetValue<List<AttributeFlagRecord>>(0)!;
		foreach (var record in records)
		{
			yield return MapRecordToAttributeFlag(record);
		}
	}

	private async ValueTask<SharpPlayer?> GetAttributeOwnerAsync(string attrId, CancellationToken ct)
	{
		var attrKey = ExtractKeyString(attrId);
		var parameters = new Dictionary<string, object?> { ["key"] = attrKey };
		var result = await ExecuteAsync(
			"SELECT * FROM attribute:⟨$key⟩->has_attribute_owner->player",
			parameters, ct);

		var records = result.GetValue<List<PlayerRecord>>(0)!;
		if (records.Count == 0) return null;

		var playerRecord = records[0];
		var pKey = playerRecord.key;

		var objParams = new Dictionary<string, object?> { ["key"] = pKey };
		var objResult = await ExecuteAsync("SELECT * FROM object:$key", objParams, ct);
		var objRecords = objResult.GetValue<List<ObjectRecord>>(0)!;
		if (objRecords.Count == 0) return null;

		var sharpObj = MapRecordToSharpObject(objRecords[0]);
		return BuildPlayer(PlayerId(pKey), playerRecord, sharpObj);
	}

	private async ValueTask<SharpAttributeEntry?> GetRelatedAttributeEntryAsync(string attrId, CancellationToken ct)
	{
		var attrKey = ExtractKeyString(attrId);
		var parameters = new Dictionary<string, object?> { ["key"] = attrKey };
		var result = await ExecuteAsync(
			"SELECT * FROM attribute:⟨$key⟩->has_attribute_entry->attribute_entry",
			parameters, ct);

		var records = result.GetValue<List<AttributeEntryRecord>>(0)!;
		if (records.Count == 0) return null;

		return MapRecordToAttributeEntry(records[0]);
	}

	private async ValueTask<SharpAttribute> MapToSharpAttribute(AttributeRecord record, CancellationToken ct)
	{
		var key = record.key;
		var id = AttributeId(key);
		var flags = await GetAttributeFlagsForAttrAsync(id, ct).ToArrayAsync(ct);
		return new SharpAttribute(
			id,
			key,
			record.name,
			flags,
			null,
			string.IsNullOrEmpty(record.longName) ? null : record.longName,
			new AsyncLazy<IAsyncEnumerable<SharpAttribute>>(innerCt => Task.FromResult<IAsyncEnumerable<SharpAttribute>>(new FreshEnumerable<SharpAttribute>(() => GetTopLevelAttributesAsync(id, innerCt)))),
			new AsyncLazy<SharpPlayer?>(async innerCt => await GetAttributeOwnerAsync(id, innerCt)),
			new AsyncLazy<SharpAttributeEntry?>(async innerCt => await GetRelatedAttributeEntryAsync(id, innerCt)))
		{
			Value = MModule.deserialize(record.value)
		};
	}

	private async ValueTask<LazySharpAttribute> MapToLazySharpAttribute(AttributeRecord record, CancellationToken ct)
	{
		var key = record.key;
		var id = AttributeId(key);
		var flags = await GetAttributeFlagsForAttrAsync(id, ct).ToArrayAsync(ct);
		return new LazySharpAttribute(
			id,
			key,
			record.name,
			flags,
			null,
			string.IsNullOrEmpty(record.longName) ? null : record.longName,
			new AsyncLazy<IAsyncEnumerable<LazySharpAttribute>>(innerCt => Task.FromResult<IAsyncEnumerable<LazySharpAttribute>>(new FreshEnumerable<LazySharpAttribute>(() => GetTopLevelLazyAttributesAsync(id, innerCt)))),
			new AsyncLazy<SharpPlayer?>(async innerCt => await GetAttributeOwnerAsync(id, innerCt)),
			new AsyncLazy<SharpAttributeEntry?>(async innerCt => await GetRelatedAttributeEntryAsync(id, innerCt)),
			Value: new AsyncLazy<MString>(innerCt =>
				Task.FromResult(MModule.deserialize(record.value))));
	}

	private async IAsyncEnumerable<SharpAttribute> GetTopLevelAttributesAsync(string parentId, [EnumeratorCancellation] CancellationToken ct = default)
	{
		SurrealDbResponse result;
		if (parentId.StartsWith("Attribute"))
		{
			var key = ExtractKeyString(parentId);
			var parameters = new Dictionary<string, object?> { ["key"] = key };
			result = await ExecuteAsync(
				"SELECT * FROM attribute:⟨$key⟩->has_attribute->attribute",
				parameters, ct);
		}
		else
		{
			var objKey = ExtractKey(parentId);
			var parameters = new Dictionary<string, object?> { ["key"] = objKey };
			result = await ExecuteAsync(
				"SELECT * FROM attribute WHERE id IN (SELECT VALUE out FROM has_attribute WHERE in IN [player:$key, room:$key, thing:$key, exit:$key])",
				parameters, ct);
		}

		var records = result.GetValue<List<AttributeRecord>>(0)!;
		foreach (var record in records)
		{
			yield return await MapToSharpAttribute(record, ct);
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
				"SELECT * FROM attribute:⟨$key⟩->has_attribute->attribute",
				parameters, ct);
		}
		else
		{
			var objKey = ExtractKey(parentId);
			var parameters = new Dictionary<string, object?> { ["key"] = objKey };
			result = await ExecuteAsync(
				"SELECT * FROM attribute WHERE id IN (SELECT VALUE out FROM has_attribute WHERE in IN [player:$key, room:$key, thing:$key, exit:$key])",
				parameters, ct);
		}

		var records = result.GetValue<List<AttributeRecord>>(0)!;
		foreach (var record in records)
		{
			yield return await MapToLazySharpAttribute(record, ct);
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
			"SELECT * FROM object:$key<-has_parent<-object",
			parameters, ct);

		var records = result.GetValue<List<ObjectRecord>>(0)!;
		foreach (var record in records)
		{
			yield return MapRecordToSharpObject(record);
		}
	}

	[GeneratedRegex(@"\*\*|[.*+?^${}()|[\]/]")]
	private static partial Regex WildcardToRegex();

	#endregion

	#region Internal Record Types for CBOR Deserialization

	internal record ObjectRecord
	{
		public int key { get; set; }
		public string name { get; set; } = "";
		public string type { get; set; } = "";
		public long creationTime { get; set; }
		public long modifiedTime { get; set; }
		public string locks { get; set; } = "{}";
		public int warnings { get; set; }
	}

	internal record PlayerRecord
	{
		public int key { get; set; }
		public string passwordHash { get; set; } = "";
		public string passwordSalt { get; set; } = "";
		public string[] aliases { get; set; } = [];
		public int quota { get; set; }
	}

	internal record RoomRecord
	{
		public int key { get; set; }
		public string[] aliases { get; set; } = [];
	}

	internal record ThingRecord
	{
		public int key { get; set; }
		public string[] aliases { get; set; } = [];
	}

	internal record ExitRecord
	{
		public int key { get; set; }
		public string[] aliases { get; set; } = [];
	}

	internal record AttributeRecord
	{
		public string key { get; set; } = "";
		public string name { get; set; } = "";
		public string value { get; set; } = "";
		public string longName { get; set; } = "";
	}

	internal record FlagRecord
	{
		public string name { get; set; } = "";
		public string symbol { get; set; } = "";
		public bool system { get; set; }
		public bool disabled { get; set; }
		public string[] aliases { get; set; } = [];
		public string[] setPermissions { get; set; } = [];
		public string[] unsetPermissions { get; set; } = [];
		public string[] typeRestrictions { get; set; } = [];
	}

	internal record PowerRecord
	{
		public string name { get; set; } = "";
		public string alias { get; set; } = "";
		public bool system { get; set; }
		public bool disabled { get; set; }
		public string[] setPermissions { get; set; } = [];
		public string[] unsetPermissions { get; set; } = [];
		public string[] typeRestrictions { get; set; } = [];
	}

	internal record AttributeFlagRecord
	{
		public string name { get; set; } = "";
		public string symbol { get; set; } = "";
		public bool system { get; set; }
		public bool inheritable { get; set; }
	}

	internal record AttributeEntryRecord
	{
		public string name { get; set; } = "";
		public string[] defaultFlags { get; set; } = [];
		public string lim { get; set; } = "";
		public string[] enumValues { get; set; } = [];
	}

	internal record CountRecord
	{
		public long cnt { get; set; }
	}

	internal record ValueRecord
	{
		public int value { get; set; }
	}

	internal record ChannelDbRecord
	{
		public string name { get; set; } = "";
		public string markedUpName { get; set; } = "";
		public string description { get; set; } = "";
		public string[] privs { get; set; } = [];
		public string joinLock { get; set; } = "";
		public string speakLock { get; set; } = "";
		public string seeLock { get; set; } = "";
		public string hideLock { get; set; } = "";
		public string modLock { get; set; } = "";
		public string mogrifier { get; set; } = "";
		public int buffer { get; set; }
	}

	internal record ChannelMemberEdgeRecord
	{
		public int memberKey { get; set; }
		public bool combine { get; set; }
		public bool gagged { get; set; }
		public bool hide { get; set; }
		public bool mute { get; set; }
		public string title { get; set; } = "";
	}

	internal record MailDbRecord
	{
		public string key { get; set; } = "";
		public long dateSent { get; set; }
		public bool fresh { get; set; }
		public bool read { get; set; }
		public bool tagged { get; set; }
		public bool urgent { get; set; }
		public bool forwarded { get; set; }
		public bool cleared { get; set; }
		public string folder { get; set; } = "";
		public string content { get; set; } = "";
		public string subject { get; set; } = "";
	}

	internal record ExpandedDataDbRecord
	{
		public string data { get; set; } = "";
	}

	#endregion
}
