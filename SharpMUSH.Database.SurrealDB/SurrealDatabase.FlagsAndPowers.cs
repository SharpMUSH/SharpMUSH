using DotNext.Threading;
using MarkupString;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SurrealDb.Net;
using SurrealDb.Net.Models;
using SurrealDb.Net.Models.Response;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SharpMUSH.Database.SurrealDB;

public partial class SurrealDatabase
{
	#region Flags and Powers

	// Embedded optimistic transactions can commit duplicate edges despite the unique index.
	// One provider owns the store; serialize its assignment writes across both relationship tables.
	private readonly SemaphoreSlim _definitionAssignmentLock = new(1, 1);

	/// <summary>Finds a flag by its case-insensitive name or alias.</summary>
	public async ValueTask<SharpObjectFlag?> GetObjectFlagAsync(string name, CancellationToken cancellationToken = default)
	{
		var record = await FindFlagRecordAsync(name, cancellationToken);
		return record is null ? null : MapRecordToFlag(record);
	}

	private async ValueTask<FlagRecord?> FindFlagRecordAsync(string name, CancellationToken cancellationToken = default)
	{
		var parameters = new Dictionary<string, object?> { ["name"] = name.ToUpperInvariant() };
		var response = await ExecuteAsync(
			"SELECT *, <string> id AS recordId FROM object_flag WHERE string::uppercase(name) = $name",
			parameters, cancellationToken);

		var results = response.GetValue<List<FlagRecord>>(0)!;
		if (results.Count > 0)
		{
			return results[0];
		}

		// Aliases are scanned rather than queried, as Lightning scans them: the flag table is a few
		// dozen rows, and matching inside an array case-insensitively is not worth a query that has to
		// be right across SurrealDB versions.
		var all = await ExecuteAsync("SELECT *, <string> id AS recordId FROM object_flag", cancellationToken);
		var byAlias = all.GetValue<List<FlagRecord>>(0)!
			.FirstOrDefault(record => record.aliases?.Any(
				alias => string.Equals(alias, name, StringComparison.OrdinalIgnoreCase)) == true);
		return byAlias;
	}

	public async IAsyncEnumerable<SharpObjectFlag> GetObjectFlagsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var response = await ExecuteAsync("SELECT * FROM object_flag", cancellationToken);
		var results = response.GetValue<List<FlagRecord>>(0)!;
		foreach (var element in results)
			yield return MapRecordToFlag(element);
	}

	public async ValueTask<SharpObjectFlag?> CreateObjectFlagAsync(string name, string[]? aliases, string symbol,
		bool system, string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions,
		CancellationToken cancellationToken = default)
	{
		var parameters = new Dictionary<string, object?>
		{
			["name"] = name.ToUpperInvariant(),
			["symbol"] = symbol,
			["system"] = system,
			["aliases"] = aliases ?? Array.Empty<string>(),
			["setPerms"] = setPermissions,
			["unsetPerms"] = unsetPermissions,
			["typeRestrictions"] = typeRestrictions
		};

		var response = await ExecuteAsync(
			"IF array::len((SELECT id FROM object_flag WHERE string::uppercase(name) = $name LIMIT 1)) = 0 { RETURN (" +
			"CREATE object_flag:⟨$name⟩ SET name = $name, symbol = $symbol, system = $system, disabled = false, aliases = $aliases, setPermissions = $setPerms, unsetPermissions = $unsetPerms, typeRestrictions = $typeRestrictions); } ELSE { RETURN []; }",
			parameters, cancellationToken);

		if (response.HasErrors) return null;
		var created = response.GetValue<List<FlagRecord>>(0);
		return created is { Count: > 0 } ? MapRecordToFlag(created[0]) : null;
	}

	public async ValueTask<bool> DeleteObjectFlagAsync(string name, CancellationToken cancellationToken = default)
	{
		var flag = await FindFlagRecordAsync(name, cancellationToken);
		if (flag == null || flag.system) return false;

		var parameters = new Dictionary<string, object?> { ["recordId"] = new StringRecordId(flag.recordId) };
		var response = await ExecuteAsync(
			"BEGIN TRANSACTION;" +
			"LET $deleted = (DELETE $recordId WHERE (system ?? false) = false RETURN BEFORE);" +
			"DELETE has_flags WHERE out IN $deleted.id;" +
			"RETURN array::len($deleted) > 0;" +
			"COMMIT TRANSACTION;",
			parameters, cancellationToken);
		return !response.HasErrors && response.GetValue<bool>(0);
	}

	public async ValueTask<bool> SetObjectFlagAsync(AnySharpObject dbref, SharpObjectFlag flag, CancellationToken cancellationToken = default)
	{
		await _definitionAssignmentLock.WaitAsync(cancellationToken);
		try
		{
			var definition = await FindFlagRecordAsync(flag.Name, cancellationToken);
			if (definition is null) return false;

			var parameters = new Dictionary<string, object?>
			{
				["key"] = dbref.Object().Key,
				["recordId"] = new StringRecordId(definition.recordId)
			};

			var existing = await ExecuteAsync(
				"SELECT count() AS cnt FROM has_flags WHERE in = object:$key AND out = $recordId GROUP ALL",
				parameters, cancellationToken);
			if (existing.HasErrors) return false;
			if (existing.GetValue<List<CountRecord>>(0) is { Count: > 0 } counts && counts[0].cnt > 0)
				return false;

			var response = await ExecuteAsync(
				"RELATE (SELECT VALUE id FROM object:$key)->has_flags->(SELECT VALUE id FROM $recordId) RETURN VALUE <string> id",
				parameters, cancellationToken);
			return !response.HasErrors && response.GetValue<List<string>>(0) is { Count: > 0 };
		}
		finally
		{
			_definitionAssignmentLock.Release();
		}
	}

	public async ValueTask<bool> UnsetObjectFlagAsync(AnySharpObject dbref, SharpObjectFlag flag, CancellationToken cancellationToken = default)
	{
		await _definitionAssignmentLock.WaitAsync(cancellationToken);
		try
		{
			var definition = await FindFlagRecordAsync(flag.Name, cancellationToken);
			if (definition is null) return false;

			var parameters = new Dictionary<string, object?>
			{
				["key"] = dbref.Object().Key,
				["recordId"] = new StringRecordId(definition.recordId)
			};

			var response = await ExecuteAsync(
				"RETURN array::len((DELETE has_flags WHERE in = object:$key AND out = $recordId RETURN BEFORE)) > 0;",
				parameters, cancellationToken);
			return !response.HasErrors && response.GetValue<bool>(0);
		}
		finally
		{
			_definitionAssignmentLock.Release();
		}
	}

	public async ValueTask<bool> UpdateObjectFlagAsync(string name, string[]? aliases, string symbol,
		string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions,
		CancellationToken cancellationToken = default)
	{
		var flag = await FindFlagRecordAsync(name, cancellationToken);
		if (flag == null || flag.system) return false;

		var parameters = new Dictionary<string, object?>
		{
			["recordId"] = new StringRecordId(flag.recordId),
			["aliases"] = aliases ?? Array.Empty<string>(),
			["symbol"] = symbol,
			["setPerms"] = setPermissions,
			["unsetPerms"] = unsetPermissions,
			["typeRestrictions"] = typeRestrictions
		};

		var response = await ExecuteAsync(
			"UPDATE $recordId SET aliases = $aliases, symbol = $symbol, setPermissions = $setPerms, unsetPermissions = $unsetPerms, typeRestrictions = $typeRestrictions WHERE (system ?? false) = false RETURN AFTER",
			parameters, cancellationToken);
		return !response.HasErrors && response.GetValue<List<FlagRecord>>(0) is { Count: > 0 };
	}

	public async ValueTask<bool> SetObjectFlagDisabledAsync(string name, bool disabled, CancellationToken cancellationToken = default)
	{
		var flag = await FindFlagRecordAsync(name, cancellationToken);
		if (flag == null || flag.system) return false;

		var parameters = new Dictionary<string, object?>
		{
			["recordId"] = new StringRecordId(flag.recordId),
			["disabled"] = disabled
		};
		var response = await ExecuteAsync(
			"UPDATE $recordId SET disabled = $disabled WHERE (system ?? false) = false RETURN AFTER",
			parameters, cancellationToken);
		return !response.HasErrors && response.GetValue<List<FlagRecord>>(0) is { Count: > 0 };
	}

	public async ValueTask<SharpPower?> GetPowerAsync(string name, CancellationToken cancellationToken = default)
	{
		var record = await FindPowerRecordAsync(name, cancellationToken);
		return record is null ? null : MapRecordToPower(record);
	}

	private async ValueTask<PowerRecord?> FindPowerRecordAsync(string name, CancellationToken cancellationToken = default)
	{
		var parameters = new Dictionary<string, object?> { ["name"] = name.ToUpperInvariant() };
		var response = await ExecuteAsync(
			"SELECT *, <string> id AS recordId FROM power WHERE string::uppercase(name) = $name",
			parameters, cancellationToken);

		var results = response.GetValue<List<PowerRecord>>(0)!;
		return results.FirstOrDefault();
	}

	public async IAsyncEnumerable<SharpPower> GetObjectPowersAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var response = await ExecuteAsync("SELECT * FROM power", cancellationToken);
		var results = response.GetValue<List<PowerRecord>>(0)!;
		foreach (var element in results)
			yield return MapRecordToPower(element);
	}

	public async ValueTask<SharpPower?> CreatePowerAsync(string name, string alias, string symbol, bool system,
		string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions,
		CancellationToken cancellationToken = default)
	{
		var parameters = new Dictionary<string, object?>
		{
			["name"] = name.ToUpperInvariant(),
			["alias"] = alias,
			["symbol"] = symbol,
			["system"] = system,
			["setPerms"] = setPermissions,
			["unsetPerms"] = unsetPermissions,
			["typeRestrictions"] = typeRestrictions
		};

		var response = await ExecuteAsync(
			"IF array::len((SELECT id FROM power WHERE string::uppercase(name) = $name LIMIT 1)) = 0 { RETURN (" +
			"CREATE power:⟨$name⟩ SET name = $name, alias = $alias, symbol = $symbol, system = $system, disabled = false, setPermissions = $setPerms, unsetPermissions = $unsetPerms, typeRestrictions = $typeRestrictions); } ELSE { RETURN []; }",
			parameters, cancellationToken);

		if (response.HasErrors) return null;
		var created = response.GetValue<List<PowerRecord>>(0);
		return created is { Count: > 0 } ? MapRecordToPower(created[0]) : null;
	}

	public async ValueTask<bool> DeletePowerAsync(string name, CancellationToken cancellationToken = default)
	{
		var power = await FindPowerRecordAsync(name, cancellationToken);
		if (power == null || power.system) return false;

		var parameters = new Dictionary<string, object?> { ["recordId"] = new StringRecordId(power.recordId) };
		var response = await ExecuteAsync(
			"BEGIN TRANSACTION;" +
			"LET $deleted = (DELETE $recordId WHERE (system ?? false) = false RETURN BEFORE);" +
			"DELETE has_powers WHERE out IN $deleted.id;" +
			"RETURN array::len($deleted) > 0;" +
			"COMMIT TRANSACTION;",
			parameters, cancellationToken);
		return !response.HasErrors && response.GetValue<bool>(0);
	}

	public async ValueTask<bool> SetObjectPowerAsync(AnySharpObject dbref, SharpPower power, CancellationToken cancellationToken = default)
	{
		await _definitionAssignmentLock.WaitAsync(cancellationToken);
		try
		{
			var definition = await FindPowerRecordAsync(power.Name, cancellationToken);
			if (definition is null) return false;

			var parameters = new Dictionary<string, object?>
			{
				["key"] = dbref.Object().Key,
				["recordId"] = new StringRecordId(definition.recordId)
			};

			var existing = await ExecuteAsync(
				"SELECT count() AS cnt FROM has_powers WHERE in = object:$key AND out = $recordId GROUP ALL",
				parameters, cancellationToken);
			if (existing.HasErrors) return false;
			if (existing.GetValue<List<CountRecord>>(0) is { Count: > 0 } counts && counts[0].cnt > 0)
				return false;

			var response = await ExecuteAsync(
				"RELATE (SELECT VALUE id FROM object:$key)->has_powers->(SELECT VALUE id FROM $recordId) RETURN VALUE <string> id",
				parameters, cancellationToken);
			return !response.HasErrors && response.GetValue<List<string>>(0) is { Count: > 0 };
		}
		finally
		{
			_definitionAssignmentLock.Release();
		}
	}

	public async ValueTask<bool> UnsetObjectPowerAsync(AnySharpObject dbref, SharpPower power, CancellationToken cancellationToken = default)
	{
		await _definitionAssignmentLock.WaitAsync(cancellationToken);
		try
		{
			var definition = await FindPowerRecordAsync(power.Name, cancellationToken);
			if (definition is null) return false;

			var parameters = new Dictionary<string, object?>
			{
				["key"] = dbref.Object().Key,
				["recordId"] = new StringRecordId(definition.recordId)
			};

			var response = await ExecuteAsync(
				"RETURN array::len((DELETE has_powers WHERE in = object:$key AND out = $recordId RETURN BEFORE)) > 0;",
				parameters, cancellationToken);
			return !response.HasErrors && response.GetValue<bool>(0);
		}
		finally
		{
			_definitionAssignmentLock.Release();
		}
	}

	public async ValueTask<bool> UpdatePowerAsync(string name, string alias, string symbol,
		string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions,
		CancellationToken cancellationToken = default)
	{
		var power = await FindPowerRecordAsync(name, cancellationToken);
		if (power == null || power.system) return false;

		var parameters = new Dictionary<string, object?>
		{
			["recordId"] = new StringRecordId(power.recordId),
			["alias"] = alias,
			["symbol"] = symbol,
			["setPerms"] = setPermissions,
			["unsetPerms"] = unsetPermissions,
			["typeRestrictions"] = typeRestrictions
		};

		var response = await ExecuteAsync(
			"UPDATE $recordId SET alias = $alias, symbol = $symbol, setPermissions = $setPerms, unsetPermissions = $unsetPerms, typeRestrictions = $typeRestrictions WHERE (system ?? false) = false RETURN AFTER",
			parameters, cancellationToken);
		return !response.HasErrors && response.GetValue<List<PowerRecord>>(0) is { Count: > 0 };
	}

	public async ValueTask<bool> SetPowerDisabledAsync(string name, bool disabled, CancellationToken cancellationToken = default)
	{
		var power = await FindPowerRecordAsync(name, cancellationToken);
		if (power == null || power.system) return false;

		var parameters = new Dictionary<string, object?>
		{
			["recordId"] = new StringRecordId(power.recordId),
			["disabled"] = disabled
		};
		var response = await ExecuteAsync(
			"UPDATE $recordId SET disabled = $disabled WHERE (system ?? false) = false RETURN AFTER",
			parameters, cancellationToken);
		return !response.HasErrors && response.GetValue<List<PowerRecord>>(0) is { Count: > 0 };
	}

	#endregion
}
