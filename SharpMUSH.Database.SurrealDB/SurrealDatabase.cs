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
using SharpMUSH.Library.Plugins;
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
	IPasswordService passwordService,
	IObjectRelationLoader relations,
	IReadOnlyList<IMigrationSource>? migrationSources = null,
	IReadOnlyList<PluginFlag>? pluginFlags = null
) : ISharpDatabase
{
	/// <summary>
	/// SurrealQL: the object fields plus its flag and power records, so an object arrives with its
	/// relations in the round trip that loads it and no <c>HasFlag</c> / <c>HasPower</c> re-reads
	/// storage through a loaded instance. Deserialised into <see cref="ObjectRecord.flags"/> and
	/// <see cref="ObjectRecord.powers"/>.
	/// </summary>
	private const string ObjectWithRelations = "*, ->has_flags->object_flag.* AS flags, ->has_powers->power.* AS powers";

	private Lazy<IAsyncEnumerable<SharpObjectFlag>> FlagsOf(string id, string type, List<FlagRecord>? records)
	{
		var upperType = type.ToUpper();
		if (records is null)
		{
			throw new InvalidOperationException("Object loaded without its flags: every query that builds an object must select ObjectWithRelations.");
		}

		var flags = records.Select(MapRecordToFlag).Append(ObjectTypeFlag.For(upperType)).ToArray();
		return new(() => flags.ToAsyncEnumerable());
	}

	private Lazy<IAsyncEnumerable<SharpPower>> PowersOf(string id, List<PowerRecord>? records)
	{
		if (records is null)
		{
			throw new InvalidOperationException("Object loaded without its powers: every query that builds an object must select ObjectWithRelations.");
		}

		var powers = records.Select(MapRecordToPower).ToArray();
		return new(() => powers.ToAsyncEnumerable());
	}

	private static readonly SemaphoreSlim MigrateLock = new(1, 1);

	// Per-instance: each SurrealDatabase owns one database (live, staging, or a test's private
	// mem store), so the dbref allocator belongs to the instance. Whether a migration has run is
	// recorded IN the database (see MigrationAppliedAsync) — there is no in-process migration state.
	private int _nextObjectKey;

	// Phase 2a plugin contributions threaded through from the pre-build PluginCatalog. Empty for staging
	// databases (created from a live DB) and any host that does not load plugins.
	private IReadOnlyList<IMigrationSource> PluginMigrationSources => migrationSources ?? [];
	private IReadOnlyList<PluginFlag> PluginFlags => pluginFlags ?? [];

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
	/// Extracts the SurrealDB table name from a typed ID like "Player/42" → "player".
	/// </summary>
	private static string ExtractTable(string typedId)
	{
		var parts = typedId.Split('/');
		if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0]))
			throw new ArgumentException($"Invalid ID format: '{typedId}'. Expected 'Label/key'.", nameof(typedId));

		return parts[0].ToLowerInvariant();
	}

	private const string AttributeChildrenByParentQuery =
		"SELECT *, ->has_attribute_flag->attribute_flag.* AS flags FROM type::thing('attribute', $key)->has_attribute->attribute";

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
	/// True when SurrealDB refused a commit because another transaction touched the same records. Its own
	/// message says the transaction can be retried, and on retry a create resolves to "already exists".
	/// <para>
	/// Both phrases come from one message — "Failed to commit transaction due to a read or write
	/// conflict. This transaction can be retried." — and every occurrence across a full CI run carried
	/// both. The second clause is matched as <c>transaction can be retried</c> rather than the bare
	/// <c>can be retried</c> so an unrelated error that happens to say a thing is retryable cannot buy
	/// itself eight retries and a demotion to warning.
	/// </para>
	/// </summary>
	internal static bool IsRetryableConflict(string message)
		=> message.Contains("read or write conflict", StringComparison.OrdinalIgnoreCase)
			|| message.Contains("transaction can be retried", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Reports a failed response at a level that matches what it means.
	/// <para>
	/// A retryable commit conflict is an ordinary outcome of SurrealDB's optimistic concurrency —
	/// callers like <c>CreateChannelAsync</c> retry and succeed — so it is a warning, not an error.
	/// Logging it at Error made healthy runs look broken and actively misdirected diagnosis: one CI
	/// run carried 270 of these for channel creates that every one of them went on to complete.
	/// </para>
	/// </summary>
	private void LogResponseErrors(string errors, string query)
	{
		if (IsRetryableConflict(errors))
		{
			logger.LogWarning("SurrealDB retryable conflict (caller may retry): {Errors} for query: {Query}",
				errors, query);
			return;
		}

		logger.LogError("SurrealDB query error: {Errors} for query: {Query}", errors, query);
	}

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
			LogResponseErrors(string.Join("; ", response.Errors.Select(FormatError)), query);
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

			expandedQuery = System.Text.RegularExpressions.Regex.Replace(
				expandedQuery,
				$@"⟨([^⟩]*?){Regex.Escape(paramToken)}([^⟩]*?)⟩",
				m => $"⟨{m.Groups[1].Value}{rawValue}{m.Groups[2].Value}⟩");

			expandedQuery = expandedQuery.Replace(paramToken, serialized);
		}

		// Log the query template (not the expanded query) to avoid leaking sensitive parameter values
		logger.LogDebug("Executing SurrealQL: {Query}", query);
		var response = await db.RawQuery(expandedQuery, null, ct);
		if (response.HasErrors)
		{
			LogResponseErrors(string.Join("; ", response.Errors.Select(FormatError)), query);
		}
		return response;
	}

	/// <summary>
	/// Serializes a value to a SurrealQL literal string (with quotes for strings).
	/// </summary>
	private static string SerializeValue(object? value) => value switch
	{
		null => "NONE",
		SurrealDb.Net.Models.StringRecordId id => id.Value,
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
		SurrealDb.Net.Models.StringRecordId id => id.Value,
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
			Flags = FlagsOf(id, type, record.flags),
			Powers = PowersOf(id, record.powers),
			Attributes = new(() => new FreshAsyncEnumerable<SharpAttribute>(enumCt => GetTopLevelAttributesAsync(id, enumCt))),
			LazyAttributes = new(() => new FreshAsyncEnumerable<LazySharpAttribute>(enumCt => GetTopLevelLazyAttributesAsync(id, enumCt))),
			AllAttributes = new(() => new FreshAsyncEnumerable<SharpAttribute>(enumCt => GetAllAttributesForIdAsync(id, enumCt))),
			LazyAllAttributes = new(() => new FreshAsyncEnumerable<LazySharpAttribute>(enumCt => GetAllLazyAttributesForIdAsync(id, enumCt))),
			Owner = new(ct => relations.OwnerOf(id, record.key, ct)),
			Parent = new(ct => relations.ParentOf(id, record.key, ct)),
			Zone = new(ct => relations.ZoneOf(id, record.key, ct)),
			Children = new(() => new FreshAsyncEnumerable<SharpObject>(enumCt => GetChildrenAsync(id, enumCt)!))
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
			$"SELECT {ObjectWithRelations} FROM object:$key",
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
			Location = new(ct => relations.LocationOf(id, sharpObj.Id!, ct)),
			Home = new(ct => relations.HomeOf(id, sharpObj.Id!, sharpObj.Key, ct))
		};
	}

	private SharpRoom BuildRoom(string id, SharpObject sharpObj)
	{
		return new SharpRoom
		{
			Id = id,
			Object = sharpObj,
			Location = new(ct => relations.DropToOf(id, sharpObj.Id!, sharpObj.Key, ct))
		};
	}

	private SharpThing BuildThing(string id, SharpObject sharpObj)
	{
		return new SharpThing
		{
			Id = id,
			Object = sharpObj,
			Location = new(ct => relations.LocationOf(id, sharpObj.Id!, ct)),
			Home = new(ct => relations.HomeOf(id, sharpObj.Id!, sharpObj.Key, ct))
		};
	}

	private SharpExit BuildExit(string id, ExitRecord exitRecord, SharpObject sharpObj)
	{
		return new SharpExit
		{
			Id = id,
			Object = sharpObj,
			Aliases = exitRecord.aliases,
			Location = new(ct => relations.LocationOf(id, sharpObj.Id!, ct)),
			Home = new(ct => relations.ExitDestinationOf(id, sharpObj.Id!, sharpObj.Key, ct))
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

	public async ValueTask<AnySharpContainer> GetHomeAsync(string typedId, CancellationToken ct = default)
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

	/// <summary>
	/// An exit's destination. Absent on a freshly @open'd or an @unlink'd exit, hence optional.
	/// </summary>
	public async ValueTask<AnyOptionalSharpContainer> GetExitDestinationAsync(string typedId, CancellationToken ct = default)
	{
		var key = ExtractKey(typedId);
		var table = ExtractTable(typedId);
		var parameters = new Dictionary<string, object?> { ["key"] = key };
		var result = await ExecuteAsync(
			$"SELECT VALUE out.key FROM has_home WHERE in = {table}:$key",
			parameters, ct);

		var destKeys = result.GetValue<List<int>>(0)!;
		if (destKeys.Count == 0)
		{
			return new None();
		}

		var destination = await BuildTypedObjectFromKey(destKeys[0], ct);
		return destination.Match<AnyOptionalSharpContainer>(
			player => player,
			room => room,
			_ => new None(),
			thing => thing,
			_ => new None());
	}

	public async ValueTask<AnyOptionalSharpContainer> GetDropToAsync(string roomId, CancellationToken ct = default)
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

	public async ValueTask<SharpPlayer> GetObjectOwnerAsync(string objectId, CancellationToken ct = default)
	{
		var key = ExtractKey(objectId);
		var parameters = new Dictionary<string, object?> { ["key"] = key };

		var ownerResult = await ExecuteAsync(
			"SELECT * FROM object:$key->has_owner->player",
			parameters, ct);
		var ownerPlayers = ownerResult.GetValue<List<PlayerRecord>>(0)!;
		if (ownerPlayers.Count == 0)
			throw new InvalidOperationException($"No owner found for {objectId}");

		var ownerPlayerRecord = ownerPlayers[0];
		var ownerKey = ownerPlayerRecord.key;

		var ownerObjParams = new Dictionary<string, object?> { ["key"] = ownerKey };
		var ownerObjResult = await ExecuteAsync(
			$"SELECT {ObjectWithRelations} FROM object:$key",
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
			$"SELECT {ObjectWithRelations} FROM object:$key->has_parent->object",
			parameters, ct);

		var records = result.GetValue<List<ObjectRecord>>(0)!;
		if (records.Count == 0) return new None();

		return await BuildTypedObjectFromObjectRecord(records[0], ct);
	}

	public async ValueTask<AnyOptionalSharpObject> GetZoneAsync(string objectId, CancellationToken ct = default)
	{
		var key = ExtractKey(objectId);
		var parameters = new Dictionary<string, object?> { ["key"] = key };
		var result = await ExecuteAsync(
			$"SELECT {ObjectWithRelations} FROM object:$key->has_zone->object",
			parameters, ct);

		var records = result.GetValue<List<ObjectRecord>>(0)!;
		if (records.Count == 0) return new None();

		return await BuildTypedObjectFromObjectRecord(records[0], ct);
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
			Symbol = record.symbol,
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
		var objResult = await ExecuteAsync($"SELECT {ObjectWithRelations} FROM object:$key", objParams, ct);
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

	// NOT async: the producing query already projects flags (->has_attribute_flag->attribute_flag.*
	// AS flags), so mapping a record needs no round trip of its own. Was `async ValueTask<...>`
	// with an internal `await GetAttributeFlagsForAttrAsync(...)` - every call site still awaits
	// these (ValueTask.FromResult is a valid await target), so nothing downstream had to change.
	private ValueTask<SharpAttribute> MapToSharpAttribute(AttributeRecord record, CancellationToken ct)
	{
		var key = record.key;
		var id = AttributeId(key);
		var flags = record.flags.Select(MapRecordToAttributeFlag).ToArray();
		return ValueTask.FromResult(new SharpAttribute(
			id,
			key,
			record.name,
			flags,
			null,
			record.longName,
			new AsyncLazy<IAsyncEnumerable<SharpAttribute>>(innerCt => Task.FromResult<IAsyncEnumerable<SharpAttribute>>(new FreshAsyncEnumerable<SharpAttribute>(enumCt => GetTopLevelAttributesAsync(id, enumCt)))),
			new AsyncLazy<SharpPlayer?>(async innerCt => await GetAttributeOwnerAsync(id, innerCt)),
			new AsyncLazy<SharpAttributeEntry?>(async innerCt => await GetRelatedAttributeEntryAsync(id, innerCt)))
		{
			Value = MModule.deserialize(record.value)
		});
	}

	/// <inheritdoc cref="MapToSharpAttribute"/>
	private ValueTask<LazySharpAttribute> MapToLazySharpAttribute(AttributeRecord record, CancellationToken ct)
	{
		var key = record.key;
		var id = AttributeId(key);
		var flags = record.flags.Select(MapRecordToAttributeFlag).ToArray();
		return ValueTask.FromResult(new LazySharpAttribute(
			id,
			key,
			record.name,
			flags,
			null,
			record.longName,
			new AsyncLazy<IAsyncEnumerable<LazySharpAttribute>>(innerCt => Task.FromResult<IAsyncEnumerable<LazySharpAttribute>>(new FreshAsyncEnumerable<LazySharpAttribute>(enumCt => GetTopLevelLazyAttributesAsync(id, enumCt)))),
			new AsyncLazy<SharpPlayer?>(async innerCt => await GetAttributeOwnerAsync(id, innerCt)),
			new AsyncLazy<SharpAttributeEntry?>(async innerCt => await GetRelatedAttributeEntryAsync(id, innerCt)),
			Value: new AsyncLazy<MString>(innerCt =>
				Task.FromResult(MModule.deserialize(record.value)))));
	}

	private async IAsyncEnumerable<SharpAttribute> GetTopLevelAttributesAsync(string parentId, [EnumeratorCancellation] CancellationToken ct = default)
	{
		SurrealDbResponse result;
		if (parentId.StartsWith("Attribute"))
		{
			var key = ExtractKeyString(parentId);
			var parameters = new Dictionary<string, object?> { ["key"] = key };
			result = await ExecuteAsync(
				AttributeChildrenByParentQuery,
				parameters, ct);
		}
		else
		{
			var objKey = ExtractKey(parentId);
			var parameters = new Dictionary<string, object?> { ["key"] = objKey };
			result = await ExecuteAsync(
				"SELECT *, ->has_attribute_flag->attribute_flag.* AS flags FROM array::flatten([player:$key, room:$key, thing:$key, exit:$key]->has_attribute->attribute)",
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
				AttributeChildrenByParentQuery,
				parameters, ct);
		}
		else
		{
			var objKey = ExtractKey(parentId);
			var parameters = new Dictionary<string, object?> { ["key"] = objKey };
			result = await ExecuteAsync(
				"SELECT *, ->has_attribute_flag->attribute_flag.* AS flags FROM array::flatten([player:$key, room:$key, thing:$key, exit:$key]->has_attribute->attribute)",
				parameters, ct);
		}

		var records = result.GetValue<List<AttributeRecord>>(0)!;
		foreach (var record in records)
		{
			yield return await MapToLazySharpAttribute(record, ct);
		}
	}

	/// <summary>
	/// Builds the query and bind parameters for <see cref="GetAllAttributesForIdAsync"/> /
	/// <see cref="GetAllLazyAttributesForIdAsync"/>: every descendant attribute of
	/// <paramref name="parentId"/>, in one round trip, via SurrealDB's recursive graph-path
	/// syntax (<c>.{..+collect}</c>, walking <c>-&gt;has_attribute-&gt;attribute</c>) rather than
	/// one <c>has_attribute</c> hop per application-level recursion. <c>+collect</c> walks
	/// depth-first and returns every node visited, not just leaves - matching the old code's
	/// flatten-the-whole-subtree behaviour - and the engine caps recursion at a fixed depth
	/// (SurrealDB 2.6: 256) and errors rather than looping forever, so a corrupted graph with a
	/// cycle fails the query instead of hanging the connection or stack-overflowing, unlike the
	/// C# recursion this replaces.
	/// <para>
	/// This only works because <c>has_attribute</c> edges are created via <c>RELATE</c>
	/// (<see cref="SetAttributeAsync"/>), not a plain <c>UPSERT ... SET in = ..., out = ...</c> -
	/// SurrealDB's graph-traversal operator only finds records actually created through
	/// <c>RELATE</c> (or an <c>INSERT RELATION</c>), never a plain table row that merely happens
	/// to have matching <c>in</c>/<c>out</c> fields, even on an untyped/schemaless table, confirmed
	/// directly against both the embedded engine and a real SurrealDB 2.6.5 server. No migration
	/// converts attributes written before this change - SharpMUSH is pre-1.0, so an existing
	/// database with attributes from before this fix is expected to be wiped and reseeded, not
	/// upgraded in place.
	/// </para>
	/// <para>
	/// The innermost <c>SELECT @.{..+collect}(...) AS ids</c> computes the descendant id array once;
	/// the middle <c>SELECT (SELECT *, ->has_attribute_flag->attribute_flag.* AS flags FROM
	/// $parent.ids) AS descendants</c> re-queries full records - each with its own flags projected
	/// in the same round trip, not a separate one per attribute - for exactly those ids
	/// (<c>$parent</c> reaches the enclosing row's <c>ids</c>, not a re-walk of the tree); the outer
	/// <c>SELECT VALUE descendants FROM (...)</c> unwraps the single-row wrapper so the statement's
	/// own result is the array of records (still nested one level - SurrealDB returns one row per
	/// queried FROM target - which <see cref="GetAllAttributesForIdAsync"/>/
	/// <see cref="GetAllLazyAttributesForIdAsync"/> unwrap). A <paramref name="parentId"/> that
	/// doesn't exist in the database, or an attribute with no descendants, both come back as an
	/// empty result rather than an error.
	/// </para>
	/// </summary>
	private static (string Query, Dictionary<string, object?> Parameters) BuildDescendantAttributesQuery(
		string parentId)
	{
		// Two nested SELECTs, not one FETCH: FETCH only dereferences the collected ids to their raw
		// stored fields, with no way to also project each one's flags in the same round trip. The
		// inner SELECT computes the id array once (@.{..+collect}); the middle SELECT re-queries
		// full records (with flags) for exactly those ids via $parent, referencing the enclosing
		// row rather than re-walking the tree.
		static string QueryFor(string fromTarget) =>
			"SELECT VALUE descendants FROM (SELECT (SELECT *, ->has_attribute_flag->attribute_flag.* AS flags FROM $parent.ids) AS descendants FROM (SELECT @.{..+collect}(->has_attribute->attribute) AS ids FROM "
			+ fromTarget
			+ "))";

		if (parentId.StartsWith("Attribute"))
		{
			var key = ExtractKeyString(parentId);
			return (
				QueryFor("type::thing('attribute', $key)"),
				new Dictionary<string, object?> { ["key"] = key });
		}

		// A bare object id doesn't carry its own type: some callers (GetAttributesAsync/
		// GetAttributesByRegexAsync) resolve it via GetTypedId first, but MapRecordToSharpObject's
		// AllAttributes property (this method's third caller) passes the generic ObjectId(key)
		// ("Object/N") - and has_attribute edges are never stored against an "object" table at
		// all, only against the typed player/room/thing/exit one. Naming all four as an array
		// FROM target matches GetTopLevelAttributesAsync's own object branch (an OR across all
		// four); whichever one doesn't exist as a real record is silently skipped rather than
		// erroring, so this costs nothing over naming the one real type.
		var objKey = ExtractKey(parentId);
		return (
			QueryFor("[player:$key, room:$key, thing:$key, exit:$key]"),
			new Dictionary<string, object?> { ["key"] = objKey });
	}

	private async IAsyncEnumerable<SharpAttribute> GetAllAttributesForIdAsync(string parentId, [EnumeratorCancellation] CancellationToken ct = default)
	{
		var (query, parameters) = BuildDescendantAttributesQuery(parentId);
		var result = await ExecuteAsync(query, parameters, ct);
		var rows = result.GetValue<List<List<AttributeRecord>>>(0);
		var records = rows is { Count: > 0 } ? rows[0] : [];
		foreach (var record in records)
		{
			yield return await MapToSharpAttribute(record, ct);
		}
	}

	private async IAsyncEnumerable<LazySharpAttribute> GetAllLazyAttributesForIdAsync(string parentId, [EnumeratorCancellation] CancellationToken ct = default)
	{
		var (query, parameters) = BuildDescendantAttributesQuery(parentId);
		var result = await ExecuteAsync(query, parameters, ct);
		var rows = result.GetValue<List<List<AttributeRecord>>>(0);
		var records = rows is { Count: > 0 } ? rows[0] : [];
		foreach (var record in records)
		{
			yield return await MapToLazySharpAttribute(record, ct);
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
			$"SELECT {ObjectWithRelations} FROM object:$key<-has_parent<-object",
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

		// Present only when the query projected ObjectWithRelations; null means load on first use.
		public List<FlagRecord>? flags { get; set; }
		public List<PowerRecord>? powers { get; set; }
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

		// Populated when the producing query projects it (every query that feeds
		// MapToSharpAttribute/MapToLazySharpAttribute does, via
		// `->has_attribute_flag->attribute_flag.* AS flags`) so mapping an attribute costs zero
		// extra round trips instead of one GetAttributeFlagsForAttrAsync call per record.
		public AttributeFlagRecord[] flags { get; set; } = [];
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
		public string symbol { get; set; } = "";
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
