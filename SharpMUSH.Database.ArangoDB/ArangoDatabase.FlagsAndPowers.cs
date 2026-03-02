using Core.Arango;
using Core.Arango.Migration;
using Core.Arango.Protocol;
using DotNext.Threading;
using MarkupString;
using Mediator;
using Microsoft.Extensions.Logging;
using OneOf.Types;
using SharpMUSH.Database.Models;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace SharpMUSH.Database.ArangoDB;

public partial class ArangoDatabase
{
	#region Flags and Powers

	public async ValueTask<SharpObjectFlag?> GetObjectFlagAsync(string name, CancellationToken ct = default)
		=> await arangoDb.Query.ExecuteStreamAsync<SharpObjectFlagQueryResult>(
				handle,
				$"FOR v in @@C1 FILTER v.Name == @flag RETURN v",
				bindVars: new Dictionary<string, object>
				{
					{ "@C1", DatabaseConstants.ObjectFlags },
					{ "flag", name }
				},
				cache: true, cancellationToken: ct)
			.Select(SharpObjectFlagQueryToSharpFlag)
			.FirstOrDefaultAsync(cancellationToken: ct);

	public IAsyncEnumerable<SharpObjectFlag> GetObjectFlagsAsync(CancellationToken ct = default)
		=> arangoDb.Query.ExecuteStreamAsync<SharpObjectFlagQueryResult>(
				handle,
				$"FOR v in {DatabaseConstants.ObjectFlags:@} RETURN v",
				cache: true, cancellationToken: ct)
			.Select(SharpObjectFlagQueryToSharpFlag);

	public IAsyncEnumerable<SharpPower> GetObjectPowersAsync(CancellationToken ct = default)
		=> arangoDb.Query.ExecuteStreamAsync<SharpPowerQueryResult>(
				handle,
				$"FOR v in {DatabaseConstants.ObjectPowers:@} RETURN v",
				cache: true, cancellationToken: ct)
			.Select(SharpPowerQueryToSharpPower);

	private static SharpPower SharpPowerQueryToSharpPower(SharpPowerQueryResult arg) =>
		new()
		{
			Id = arg.Id,
			Alias = arg.Alias,
			Name = arg.Name,
			System = arg.System,
			Disabled = arg.Disabled,
			SetPermissions = arg.SetPermissions,
			UnsetPermissions = arg.UnsetPermissions,
			TypeRestrictions = arg.TypeRestrictions
		};

	private async ValueTask<string?> GetObjectFlagEdge(AnySharpObject target, SharpObjectFlag flag,
		CancellationToken ct = default)
	{
		var result = await arangoDb.Query.ExecuteAsync<string>(handle,
			$"FOR v,e IN 1..1 OUTBOUND {target.Object().Id} GRAPH {DatabaseConstants.GraphFlags} FILTER v._id == {flag.Id} RETURN e._key",
			cancellationToken: ct);
		return result.FirstOrDefault();
	}

	private async ValueTask<string?> GetObjectPowerEdge(AnySharpObject target, SharpPower flag,
		CancellationToken ct = default)
	{
		var result = await arangoDb.Query.ExecuteAsync<string>(handle,
			$"FOR v,e IN 1..1 OUTBOUND {target.Object().Id} GRAPH {DatabaseConstants.GraphPowers} FILTER v._id == {flag.Id} RETURN e._key",
			cancellationToken: ct);
		return result.FirstOrDefault();
	}

	public async ValueTask<bool> SetObjectFlagAsync(AnySharpObject target, SharpObjectFlag flag,
		CancellationToken ct = default)
	{
		var edge = await GetObjectFlagEdge(target, flag, ct);
		if (edge is not null) return false;

		await arangoDb.Graph.Edge.CreateAsync(handle, DatabaseConstants.GraphFlags, DatabaseConstants.HasFlags,
			new SharpEdgeCreateRequest(target.Object().Id!, flag.Id!), cancellationToken: ct);

		return true;
	}

	public async ValueTask<bool> SetObjectPowerAsync(AnySharpObject dbref, SharpPower power,
		CancellationToken ct = default)
	{
		var edge = await GetObjectPowerEdge(dbref, power, ct);
		if (edge is not null) return false;

		await arangoDb.Graph.Edge.CreateAsync(handle, DatabaseConstants.GraphPowers, DatabaseConstants.HasPowers,
			new SharpEdgeCreateRequest(dbref.Object().Id!, power.Id!), cancellationToken: ct);

		return true;
	}

	public async ValueTask<bool> UnsetObjectPowerAsync(AnySharpObject dbref, SharpPower power,
		CancellationToken ct = default)
	{
		var edge = await GetObjectPowerEdge(dbref, power, ct);
		if (edge is null) return false;

		await arangoDb.Graph.Edge.RemoveAsync<string>(handle, DatabaseConstants.GraphPowers, DatabaseConstants.HasPowers,
			edge, cancellationToken: ct);

		return true;
	}
	public async ValueTask<bool> UnsetObjectFlagAsync(AnySharpObject target, SharpObjectFlag flag,
		CancellationToken ct = default)
	{
		var edge = await GetObjectFlagEdge(target, flag, ct);
		if (edge is null) return false;

		await arangoDb.Graph.Edge.RemoveAsync<string>(handle, DatabaseConstants.GraphFlags, DatabaseConstants.HasFlags,
			edge, cancellationToken: ct);

		return true;
	}

	private IAsyncEnumerable<SharpPower> GetPowersAsync(string id, CancellationToken ct = default) =>
		arangoDb.Query.ExecuteStreamAsync<SharpPowerQueryResult>(handle,
				$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.GraphPowers} RETURN v", cancellationToken: ct)
			.Select(SharpPowerQueryToSharpPower);

	public IAsyncEnumerable<SharpObjectFlag> GetObjectFlagsAsync(string id, string type, CancellationToken ct = default)
		=> arangoDb.Query.ExecuteStreamAsync<SharpObjectFlagQueryResult>(handle,
				$"FOR v IN 1..1 OUTBOUND {id} GRAPH {DatabaseConstants.GraphFlags} RETURN v", cancellationToken: ct)
			.Select(SharpObjectFlagQueryToSharpFlag)
			.Append(new SharpObjectFlag()
			{
				Name = type,
				SetPermissions = [],
				TypeRestrictions = [],
				Symbol = type[0].ToString(),
				System = true,
				UnsetPermissions = [],
				Id = null,
				Aliases = []
			});
	private SharpObjectFlag SharpObjectFlagQueryToSharpFlag(SharpObjectFlagQueryResult x) =>
		new()
		{
			Id = x.Id,
			Name = x.Name,
			Symbol = x.Symbol,
			System = x.System,
			Disabled = x.Disabled,
			SetPermissions = x.SetPermissions,
			UnsetPermissions = x.UnsetPermissions,
			Aliases = x.Aliases,
			TypeRestrictions = x.TypeRestrictions
		};
	public IAsyncEnumerable<SharpPower> GetObjectPowersAsync(string id, CancellationToken ct = default)
		=> arangoDb.Query.ExecuteStreamAsync<SharpPower>(handle,
			$"FOR v IN 1 OUTBOUND {id} GRAPH {DatabaseConstants.GraphPowers} RETURN v", cache: true,
			cancellationToken: ct);
	public async ValueTask<SharpObjectFlag?> CreateObjectFlagAsync(string name, string[]? aliases, string symbol,
		bool system, string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions,
		CancellationToken ct = default)
	{
		// Create the flag document in the database
		var request = new SharpObjectFlagCreationRequest(
			name,
			aliases,
			symbol,
			system,
			false, // disabled - user-created flags start enabled
			setPermissions,
			unsetPermissions,
			typeRestrictions
		);

		var result = await arangoDb.Document.CreateAsync(
			handle,
			DatabaseConstants.ObjectFlags,
			request,
			cancellationToken: ct
		);

		if (result != null)
		{
			// Return the created flag
			return new SharpObjectFlag
			{
				Id = result.Id,
				Name = name,
				Aliases = aliases,
				Symbol = symbol,
				System = system,
				SetPermissions = setPermissions,
				UnsetPermissions = unsetPermissions,
				TypeRestrictions = typeRestrictions
			};
		}

		return null;
	}

	public async ValueTask<bool> DeleteObjectFlagAsync(string name, CancellationToken ct = default)
	{
		// Get the flag to delete
		var flag = await GetObjectFlagAsync(name, ct);
		if (flag == null)
		{
			return false;
		}

		// Prevent deletion of system flags
		if (flag.System)
		{
			return false;
		}

		// Delete the flag document using collection and key
		await arangoDb.Document.DeleteAsync<object>(
			handle,
			DatabaseConstants.ObjectFlags,
			flag.Id!.Split('/')[1], // Extract key from ID (format: collection/key)
			cancellationToken: ct
		);

		return true;
	}

	public async ValueTask<SharpPower?> CreatePowerAsync(string name, string alias, bool system,
		string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions,
		CancellationToken ct = default)
	{
		// Create the power document in the database
		var request = new SharpPowerCreateRequest(
			name,
			alias,
			system,
			false, // disabled - user-created powers start enabled
			setPermissions,
			unsetPermissions,
			typeRestrictions
		);

		var result = await arangoDb.Document.CreateAsync(
			handle,
			DatabaseConstants.ObjectPowers,
			request,
			cancellationToken: ct
		);

		if (result != null)
		{
			// Return the created power
			return new SharpPower
			{
				Id = result.Id,
				Name = name,
				Alias = alias,
				System = system,
				SetPermissions = setPermissions,
				UnsetPermissions = unsetPermissions,
				TypeRestrictions = typeRestrictions
			};
		}

		return null;
	}

	public async ValueTask<bool> DeletePowerAsync(string name, CancellationToken ct = default)
	{
		// Get the power to delete
		var power = await GetPowerAsync(name, ct);
		if (power == null)
		{
			return false;
		}

		// Prevent deletion of system powers
		if (power.System)
		{
			return false;
		}

		// Delete the power document using collection and key
		await arangoDb.Document.DeleteAsync<object>(
			handle,
			DatabaseConstants.ObjectPowers,
			power.Id!.Split('/')[1], // Extract key from ID (format: collection/key)
			cancellationToken: ct
		);

		return true;
	}

	public async ValueTask<SharpPower?> GetPowerAsync(string name, CancellationToken ct = default)
		=> await arangoDb.Query.ExecuteStreamAsync<SharpPowerQueryResult>(
				handle,
				$"FOR v in @@C1 FILTER v.Name == @power RETURN v",
				bindVars: new Dictionary<string, object>
				{
					{ "@C1", DatabaseConstants.ObjectPowers },
					{ "power", name }
				},
				cache: true, cancellationToken: ct)
			.Select(SharpPowerQueryToSharpPower)
			.FirstOrDefaultAsync(cancellationToken: ct);

	public async ValueTask<bool> UpdateObjectFlagAsync(string name, string[]? aliases, string symbol,
		string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions,
		CancellationToken ct = default)
	{
		// Get the flag to update
		var flag = await GetObjectFlagAsync(name, ct);
		if (flag == null)
		{
			return false;
		}

		// Prevent modification of system flags
		if (flag.System)
		{
			return false;
		}

		// Update the flag document - need to extract the Key from the ID
		var key = flag.Id!.Split('/')[1];
		await arangoDb.Document.UpdateAsync(
			handle,
			DatabaseConstants.ObjectFlags,
			new
			{
				Key = key,
				Aliases = aliases ?? Array.Empty<string>(),
				Symbol = symbol,
				SetPermissions = setPermissions,
				UnsetPermissions = unsetPermissions,
				TypeRestrictions = typeRestrictions
			},
			mergeObjects: true,
			cancellationToken: ct
		);

		return true;
	}

	public async ValueTask<bool> UpdatePowerAsync(string name, string alias,
		string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions,
		CancellationToken ct = default)
	{
		// Get the power to update
		var power = await GetPowerAsync(name, ct);
		if (power == null)
		{
			return false;
		}

		// Prevent modification of system powers
		if (power.System)
		{
			return false;
		}

		// Update the power document - need to extract the Key from the ID
		var key = power.Id!.Split('/')[1];
		await arangoDb.Document.UpdateAsync(
			handle,
			DatabaseConstants.ObjectPowers,
			new
			{
				Key = key,
				Alias = alias,
				SetPermissions = setPermissions,
				UnsetPermissions = unsetPermissions,
				TypeRestrictions = typeRestrictions
			},
			mergeObjects: true,
			cancellationToken: ct
		);

		return true;
	}

	public async ValueTask<bool> SetObjectFlagDisabledAsync(string name, bool disabled,
		CancellationToken ct = default)
	{
		// Get the flag to update
		var flag = await GetObjectFlagAsync(name, ct);
		if (flag == null)
		{
			return false;
		}

		// Prevent disabling system flags
		if (flag.System)
		{
			return false;
		}

		// Update the flag document - need to extract the Key from the ID
		var key = flag.Id!.Split('/')[1];
		await arangoDb.Document.UpdateAsync(
			handle,
			DatabaseConstants.ObjectFlags,
			new
			{
				Key = key,
				Disabled = disabled
			},
			mergeObjects: true,
			cancellationToken: ct
		);

		return true;
	}

	public async ValueTask<bool> SetPowerDisabledAsync(string name, bool disabled,
		CancellationToken ct = default)
	{
		// Get the power to update
		var power = await GetPowerAsync(name, ct);
		if (power == null)
		{
			return false;
		}

		// Prevent disabling system powers
		if (power.System)
		{
			return false;
		}

		// Update the power document - need to extract the Key from the ID
		var key = power.Id!.Split('/')[1];
		await arangoDb.Document.UpdateAsync(
			handle,
			DatabaseConstants.ObjectPowers,
			new
			{
				Key = key,
				Disabled = disabled
			},
			mergeObjects: true,
			cancellationToken: ct
		);

		return true;
	}

	#endregion
}
