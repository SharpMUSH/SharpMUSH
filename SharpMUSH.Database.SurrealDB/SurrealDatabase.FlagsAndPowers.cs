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
	#region Flags and Powers

	public async ValueTask<SharpObjectFlag?> GetObjectFlagAsync(string name, CancellationToken cancellationToken = default)
	{
		var parameters = new Dictionary<string, object?> { ["name"] = name };
		var response = await ExecuteAsync(
			"SELECT * FROM object_flag WHERE name = $name",
			parameters, cancellationToken);

		var results = response.GetValue<List<FlagRecord>>(0)!;
		return results.Count > 0 ? MapRecordToFlag(results[0]) : null;
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
			["name"] = name,
			["symbol"] = symbol,
			["system"] = system,
			["aliases"] = aliases ?? Array.Empty<string>(),
			["setPerms"] = setPermissions,
			["unsetPerms"] = unsetPermissions,
			["typeRestrictions"] = typeRestrictions
		};

		await ExecuteAsync(
			"CREATE object_flag SET name = $name, symbol = $symbol, system = $system, disabled = false, aliases = $aliases, setPermissions = $setPerms, unsetPermissions = $unsetPerms, typeRestrictions = $typeRestrictions",
			parameters, cancellationToken);

		return new SharpObjectFlag
		{
			Id = ObjectFlagId(name),
			Name = name,
			Aliases = aliases,
			Symbol = symbol,
			System = system,
			SetPermissions = setPermissions,
			UnsetPermissions = unsetPermissions,
			TypeRestrictions = typeRestrictions
		};
	}

	public async ValueTask<bool> DeleteObjectFlagAsync(string name, CancellationToken cancellationToken = default)
	{
		var flag = await GetObjectFlagAsync(name, cancellationToken);
		if (flag == null || flag.System) return false;

		var parameters = new Dictionary<string, object?> { ["name"] = name };
		// Delete the flag and any edges referencing it
		await ExecuteAsync(
			"DELETE has_flags WHERE out.name = $name;" +
			"DELETE object_flag WHERE name = $name",
			parameters, cancellationToken);
		return true;
	}

	public async ValueTask<bool> SetObjectFlagAsync(AnySharpObject dbref, SharpObjectFlag flag, CancellationToken cancellationToken = default)
	{
		var objKey = dbref.Object().Key;
		var parameters = new Dictionary<string, object?>
		{
			["key"] = objKey,
			["fname"] = flag.Name
		};

		// Check if already set
		var existing = await ExecuteAsync(
			"SELECT count() AS cnt FROM has_flags WHERE in = type::thing('object', $key) AND out.name = $fname GROUP ALL",
			parameters, cancellationToken);

		var existingResults = existing.GetValue<List<CountRecord>>(0)!;
		if (existingResults.Count > 0 && existingResults[0].cnt > 0)
			return false;

		// Find the flag record and relate
		await ExecuteAsync(
			"LET $flag = (SELECT id FROM object_flag WHERE name = $fname);" +
			"RELATE type::thing('object', $key)->has_flags->$flag[0].id",
			parameters, cancellationToken);
		return true;
	}

	public async ValueTask<bool> UnsetObjectFlagAsync(AnySharpObject dbref, SharpObjectFlag flag, CancellationToken cancellationToken = default)
	{
		var objKey = dbref.Object().Key;
		var parameters = new Dictionary<string, object?>
		{
			["key"] = objKey,
			["fname"] = flag.Name
		};

		var response = await ExecuteAsync(
			"DELETE has_flags WHERE in = type::thing('object', $key) AND out.name = $fname RETURN BEFORE",
			parameters, cancellationToken);

		var results = response.GetValue<List<CountRecord>>(0)!;
		return results.Count > 0;
	}

	public async ValueTask<bool> UpdateObjectFlagAsync(string name, string[]? aliases, string symbol,
		string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions,
		CancellationToken cancellationToken = default)
	{
		var flag = await GetObjectFlagAsync(name, cancellationToken);
		if (flag == null || flag.System) return false;

		var parameters = new Dictionary<string, object?>
		{
			["name"] = name,
			["aliases"] = aliases ?? Array.Empty<string>(),
			["symbol"] = symbol,
			["setPerms"] = setPermissions,
			["unsetPerms"] = unsetPermissions,
			["typeRestrictions"] = typeRestrictions
		};

		await ExecuteAsync(
			"UPDATE object_flag SET aliases = $aliases, symbol = $symbol, setPermissions = $setPerms, unsetPermissions = $unsetPerms, typeRestrictions = $typeRestrictions WHERE name = $name",
			parameters, cancellationToken);
		return true;
	}

	public async ValueTask<bool> SetObjectFlagDisabledAsync(string name, bool disabled, CancellationToken cancellationToken = default)
	{
		var flag = await GetObjectFlagAsync(name, cancellationToken);
		if (flag == null || flag.System) return false;

		var parameters = new Dictionary<string, object?>
		{
			["name"] = name,
			["disabled"] = disabled
		};
		await ExecuteAsync(
			"UPDATE object_flag SET disabled = $disabled WHERE name = $name",
			parameters, cancellationToken);
		return true;
	}

	public async ValueTask<SharpPower?> GetPowerAsync(string name, CancellationToken cancellationToken = default)
	{
		var parameters = new Dictionary<string, object?> { ["name"] = name };
		var response = await ExecuteAsync(
			"SELECT * FROM power WHERE name = $name",
			parameters, cancellationToken);

		var results = response.GetValue<List<PowerRecord>>(0)!;
		return results.Count > 0 ? MapRecordToPower(results[0]) : null;
	}

	public async IAsyncEnumerable<SharpPower> GetObjectPowersAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var response = await ExecuteAsync("SELECT * FROM power", cancellationToken);
		var results = response.GetValue<List<PowerRecord>>(0)!;
		foreach (var element in results)
			yield return MapRecordToPower(element);
	}

	public async ValueTask<SharpPower?> CreatePowerAsync(string name, string alias, bool system,
		string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions,
		CancellationToken cancellationToken = default)
	{
		var parameters = new Dictionary<string, object?>
		{
			["name"] = name,
			["alias"] = alias,
			["system"] = system,
			["setPerms"] = setPermissions,
			["unsetPerms"] = unsetPermissions,
			["typeRestrictions"] = typeRestrictions
		};

		await ExecuteAsync(
			"CREATE power SET name = $name, alias = $alias, system = $system, disabled = false, setPermissions = $setPerms, unsetPermissions = $unsetPerms, typeRestrictions = $typeRestrictions",
			parameters, cancellationToken);

		return new SharpPower
		{
			Id = PowerId(name),
			Name = name,
			Alias = alias,
			System = system,
			SetPermissions = setPermissions,
			UnsetPermissions = unsetPermissions,
			TypeRestrictions = typeRestrictions
		};
	}

	public async ValueTask<bool> DeletePowerAsync(string name, CancellationToken cancellationToken = default)
	{
		var power = await GetPowerAsync(name, cancellationToken);
		if (power == null || power.System) return false;

		var parameters = new Dictionary<string, object?> { ["name"] = name };
		await ExecuteAsync(
			"DELETE has_powers WHERE out.name = $name;" +
			"DELETE power WHERE name = $name",
			parameters, cancellationToken);
		return true;
	}

	public async ValueTask<bool> SetObjectPowerAsync(AnySharpObject dbref, SharpPower power, CancellationToken cancellationToken = default)
	{
		var objKey = dbref.Object().Key;
		var parameters = new Dictionary<string, object?>
		{
			["key"] = objKey,
			["pname"] = power.Name
		};

		// Check if already set
		var existing = await ExecuteAsync(
			"SELECT count() AS cnt FROM has_powers WHERE in = type::thing('object', $key) AND out.name = $pname GROUP ALL",
			parameters, cancellationToken);

		var existingResults = existing.GetValue<List<CountRecord>>(0)!;
		if (existingResults.Count > 0 && existingResults[0].cnt > 0)
			return false;

		await ExecuteAsync(
			"LET $pwr = (SELECT id FROM power WHERE name = $pname);" +
			"RELATE type::thing('object', $key)->has_powers->$pwr[0].id",
			parameters, cancellationToken);
		return true;
	}

	public async ValueTask<bool> UnsetObjectPowerAsync(AnySharpObject dbref, SharpPower power, CancellationToken cancellationToken = default)
	{
		var objKey = dbref.Object().Key;
		var parameters = new Dictionary<string, object?>
		{
			["key"] = objKey,
			["pname"] = power.Name
		};

		var response = await ExecuteAsync(
			"DELETE has_powers WHERE in = type::thing('object', $key) AND out.name = $pname RETURN BEFORE",
			parameters, cancellationToken);

		var results = response.GetValue<List<CountRecord>>(0)!;
		return results.Count > 0;
	}

	public async ValueTask<bool> UpdatePowerAsync(string name, string alias,
		string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions,
		CancellationToken cancellationToken = default)
	{
		var power = await GetPowerAsync(name, cancellationToken);
		if (power == null || power.System) return false;

		var parameters = new Dictionary<string, object?>
		{
			["name"] = name,
			["alias"] = alias,
			["setPerms"] = setPermissions,
			["unsetPerms"] = unsetPermissions,
			["typeRestrictions"] = typeRestrictions
		};

		await ExecuteAsync(
			"UPDATE power SET alias = $alias, setPermissions = $setPerms, unsetPermissions = $unsetPerms, typeRestrictions = $typeRestrictions WHERE name = $name",
			parameters, cancellationToken);
		return true;
	}

	public async ValueTask<bool> SetPowerDisabledAsync(string name, bool disabled, CancellationToken cancellationToken = default)
	{
		var power = await GetPowerAsync(name, cancellationToken);
		if (power == null || power.System) return false;

		var parameters = new Dictionary<string, object?>
		{
			["name"] = name,
			["disabled"] = disabled
		};
		await ExecuteAsync(
			"UPDATE power SET disabled = $disabled WHERE name = $name",
			parameters, cancellationToken);
		return true;
	}

	#endregion
}
