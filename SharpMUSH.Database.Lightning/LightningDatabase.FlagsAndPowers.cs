using System.Runtime.CompilerServices;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IFlagAndPowerStore"/>: flag and power definitions, and their assignment to objects.
/// Definitions live in <see cref="Tables.Flag"/>/<see cref="Tables.Power"/>, keyed by
/// <c>Keys.Upper(name)</c> and seeded by <c>Migrate()</c> (see
/// <c>Migration/LightningMigration.cs</c>). Assignment is the <see cref="Tables.ObjFlag"/>/
/// <see cref="Tables.ObjPower"/> edge pair — forward key <c>Keys.Dbref(dbref)</c> -&gt; value
/// <c>Keys.Upper(name)</c>, reverse key <c>Keys.Upper(name)</c> -&gt; value <c>Keys.Dbref(dbref)</c> —
/// both opened <c>fixedDuplicates: false</c> since a name is variable-length. <see cref="ReadObjectFlags"/>
/// and <see cref="ReadObjectPowers"/> are the shared per-object reads <c>Hydrate</c> (in
/// <c>LightningDatabase.Objects.cs</c>) and the object-search flag predicate both use.
/// <para>
/// A role-backed name is not stored as a flag or power at all: WIZARD and ROYALTY (<see cref="RoleFlags"/>)
/// are the object's <c>wizard</c> and <c>royalty</c> roles, the Guest and Builder powers its <c>guest</c>
/// and <c>builder</c> roles, and every other built-in power (<see cref="GamePowers"/>) an Allow override on
/// its scope. Setting or clearing one here writes that instead, so every writer (an import, a seed, a
/// restore) lands it where the privilege checks read it.
/// </para>
/// </summary>
public partial class LightningDatabase
{
	#region Flags and Powers

	public ValueTask<SharpObjectFlag?> GetObjectFlagAsync(string name, CancellationToken cancellationToken = default)
		=> new(FindFlagRecord(FlagDefinitions(), name) is { } record ? MapFlag(record) : null);

	public IAsyncEnumerable<SharpObjectFlag> GetObjectFlagsAsync(CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<SharpObjectFlag>(GetAllFlagsCoreAsync);

	private async IAsyncEnumerable<SharpObjectFlag> GetAllFlagsCoreAsync([EnumeratorCancellation] CancellationToken ct)
	{
		foreach (var record in FlagDefinitions().Ordered)
		{
			ct.ThrowIfCancellationRequested();
			yield return MapFlag(record);
		}
	}

	public async ValueTask<SharpObjectFlag?> CreateObjectFlagAsync(string name, string[]? aliases, string symbol,
		bool system, string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions,
		CancellationToken cancellationToken = default)
	{
		var record = new FlagRecord
		{
			Name = name.ToUpperInvariant(),
			Symbol = symbol,
			Aliases = aliases ?? [],
			SetPermissions = setPermissions,
			UnsetPermissions = unsetPermissions,
			TypeRestrictions = typeRestrictions,
			System = system,
			Disabled = false
		};

		var created = await WriteDefinitionAsync(_flagDefinitions, tx =>
		{
			var key = Keys.Upper(name);
			if (tx.TryGet(Tables.Flag, key, out _)) return false;
			tx.Put(Tables.Flag, key, Codec.Serialize(record));
			return true;
		}, cancellationToken);
		return created ? MapFlag(record) : null;
	}

	public async ValueTask<bool> DeleteObjectFlagAsync(string name, CancellationToken cancellationToken = default)
		=> await WriteDefinitionAsync(_flagDefinitions, tx =>
		{
			var key = Keys.Upper(name);
			if (!tx.TryGet(Tables.Flag, key, out var bytes)) return false;
			if (Codec.Deserialize<FlagRecord>(bytes).System) return false;

			foreach (var dbrefValue in tx.Dups(Tables.ObjFlag.Reverse, key).ToList())
			{
				tx.Delete(Tables.ObjFlag.Forward, dbrefValue, key);
				tx.Delete(Tables.ObjFlag.Reverse, key, dbrefValue);
			}

			tx.Delete(Tables.Flag, key);
			return true;
		}, cancellationToken);

	public async ValueTask<bool> SetObjectFlagAsync(AnySharpObject dbref, SharpObjectFlag flag, CancellationToken cancellationToken = default)
	{
		var dbrefKey = (long)dbref.Object().Key;
		if (RoleFlags.Find(flag.Name) is { } roleFlag)
			return await Store.WriteAsync(tx => AddObjectRole(tx, dbrefKey, roleFlag.Role), cancellationToken);
		return await Store.WriteAsync(tx =>
		{
			if (HasMembershipEdge(Tables.ObjFlag.Forward, tx, dbrefKey, flag.Name)) return false;

			PutMembershipEdge(Tables.ObjFlag, tx, dbrefKey, flag.Name);
			return true;
		}, cancellationToken);
	}

	public async ValueTask<bool> UnsetObjectFlagAsync(AnySharpObject dbref, SharpObjectFlag flag, CancellationToken cancellationToken = default)
	{
		var dbrefKey = (long)dbref.Object().Key;
		if (RoleFlags.Find(flag.Name) is { } roleFlag)
			return await Store.WriteAsync(tx => RemoveObjectRole(tx, dbrefKey, roleFlag.Role), cancellationToken);
		return await Store.WriteAsync(tx =>
		{
			if (!HasMembershipEdge(Tables.ObjFlag.Forward, tx, dbrefKey, flag.Name)) return false;

			DeleteMembershipEdge(Tables.ObjFlag, tx, dbrefKey, flag.Name);
			return true;
		}, cancellationToken);
	}

	public async ValueTask<bool> UpdateObjectFlagAsync(string name, string[]? aliases, string symbol,
		string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions,
		CancellationToken cancellationToken = default)
		=> await WriteDefinitionAsync(_flagDefinitions, tx =>
		{
			var key = Keys.Upper(name);
			if (!tx.TryGet(Tables.Flag, key, out var bytes)) return false;
			var existing = Codec.Deserialize<FlagRecord>(bytes);
			if (existing.System) return false;

			tx.Put(Tables.Flag, key, Codec.Serialize(existing with
			{
				Aliases = aliases ?? [],
				Symbol = symbol,
				SetPermissions = setPermissions,
				UnsetPermissions = unsetPermissions,
				TypeRestrictions = typeRestrictions
			}));
			return true;
		}, cancellationToken);

	public async ValueTask<bool> SetObjectFlagDisabledAsync(string name, bool disabled, CancellationToken cancellationToken = default)
		=> await WriteDefinitionAsync(_flagDefinitions, tx =>
		{
			var key = Keys.Upper(name);
			if (!tx.TryGet(Tables.Flag, key, out var bytes)) return false;
			var existing = Codec.Deserialize<FlagRecord>(bytes);
			if (existing.System) return false;

			tx.Put(Tables.Flag, key, Codec.Serialize(existing with { Disabled = disabled }));
			return true;
		}, cancellationToken);

	public ValueTask<SharpPower?> GetPowerAsync(string name, CancellationToken cancellationToken = default)
		=> new(PowerDefinitions().ByName(name) is { } record ? MapPower(record) : null);

	public IAsyncEnumerable<SharpPower> GetObjectPowersAsync(CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<SharpPower>(GetAllPowersCoreAsync);

	private async IAsyncEnumerable<SharpPower> GetAllPowersCoreAsync([EnumeratorCancellation] CancellationToken ct)
	{
		foreach (var record in PowerDefinitions().Ordered)
		{
			ct.ThrowIfCancellationRequested();
			yield return MapPower(record);
		}
	}

	public async ValueTask<SharpPower?> CreatePowerAsync(string name, string[] aliases, string symbol, bool system,
		string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions,
		CancellationToken cancellationToken = default)
	{
		var record = new PowerRecord
		{
			Name = name.ToUpperInvariant(),
			Aliases = aliases,
			Symbol = symbol,
			SetPermissions = setPermissions,
			UnsetPermissions = unsetPermissions,
			TypeRestrictions = typeRestrictions,
			System = system,
			Disabled = false
		};

		var created = await WriteDefinitionAsync(_powerDefinitions, tx =>
		{
			var key = Keys.Upper(name);
			if (tx.TryGet(Tables.Power, key, out _)) return false;
			tx.Put(Tables.Power, key, Codec.Serialize(record));
			return true;
		}, cancellationToken);
		return created ? MapPower(record) : null;
	}

	public async ValueTask<bool> DeletePowerAsync(string name, CancellationToken cancellationToken = default)
		=> await WriteDefinitionAsync(_powerDefinitions, tx =>
		{
			var key = Keys.Upper(name);
			if (!tx.TryGet(Tables.Power, key, out var bytes)) return false;
			if (Codec.Deserialize<PowerRecord>(bytes).System) return false;

			foreach (var dbrefValue in tx.Dups(Tables.ObjPower.Reverse, key).ToList())
			{
				tx.Delete(Tables.ObjPower.Forward, dbrefValue, key);
				tx.Delete(Tables.ObjPower.Reverse, key, dbrefValue);
			}

			tx.Delete(Tables.Power, key);
			return true;
		}, cancellationToken);

	public async ValueTask<bool> SetObjectPowerAsync(AnySharpObject dbref, SharpPower power, CancellationToken cancellationToken = default)
	{
		var dbrefKey = (long)dbref.Object().Key;
		if (GamePowers.Find(power.Name) is { } gamePower)
			return await Store.WriteAsync(tx => gamePower.Role is { } role
				? AddObjectRole(tx, dbrefKey, role)
				: AllowObjectScope(tx, dbrefKey, gamePower.Scope), cancellationToken);
		return await Store.WriteAsync(tx =>
		{
			if (HasMembershipEdge(Tables.ObjPower.Forward, tx, dbrefKey, power.Name)) return false;

			PutMembershipEdge(Tables.ObjPower, tx, dbrefKey, power.Name);
			return true;
		}, cancellationToken);
	}

	public async ValueTask<bool> UnsetObjectPowerAsync(AnySharpObject dbref, SharpPower power, CancellationToken cancellationToken = default)
	{
		var dbrefKey = (long)dbref.Object().Key;
		if (GamePowers.Find(power.Name) is { } gamePower)
			return await Store.WriteAsync(tx => gamePower.Role is { } role
				? RemoveObjectRole(tx, dbrefKey, role)
				: ClearObjectScope(tx, dbrefKey, gamePower.Scope), cancellationToken);
		return await Store.WriteAsync(tx =>
		{
			if (!HasMembershipEdge(Tables.ObjPower.Forward, tx, dbrefKey, power.Name)) return false;

			DeleteMembershipEdge(Tables.ObjPower, tx, dbrefKey, power.Name);
			return true;
		}, cancellationToken);
	}

	public async ValueTask<bool> UpdatePowerAsync(string name, string[] aliases, string symbol,
		string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions,
		CancellationToken cancellationToken = default)
		=> await WriteDefinitionAsync(_powerDefinitions, tx =>
		{
			var key = Keys.Upper(name);
			if (!tx.TryGet(Tables.Power, key, out var bytes)) return false;
			var existing = Codec.Deserialize<PowerRecord>(bytes);
			if (existing.System) return false;

			tx.Put(Tables.Power, key, Codec.Serialize(existing with
			{
				Alias = string.Empty,
				Aliases = aliases,
				Symbol = symbol,
				SetPermissions = setPermissions,
				UnsetPermissions = unsetPermissions,
				TypeRestrictions = typeRestrictions
			}));
			return true;
		}, cancellationToken);

	public async ValueTask<bool> SetPowerDisabledAsync(string name, bool disabled, CancellationToken cancellationToken = default)
		=> await WriteDefinitionAsync(_powerDefinitions, tx =>
		{
			var key = Keys.Upper(name);
			if (!tx.TryGet(Tables.Power, key, out var bytes)) return false;
			var existing = Codec.Deserialize<PowerRecord>(bytes);
			if (existing.System) return false;

			tx.Put(Tables.Power, key, Codec.Serialize(existing with { Disabled = disabled }));
			return true;
		}, cancellationToken);

	/// <summary>Every object flag assigned to <paramref name="dbref"/>, resolved through <see cref="Tables.Flag"/>
	/// — a stale <see cref="Tables.ObjFlag"/> edge whose definition was deleted is silently dropped rather than
	/// surfaced as null. Does not synthesize the type-named flag <c>FlagsOf</c> appends; callers that need that
	/// (Hydrate) add it themselves.</summary>
	internal IEnumerable<SharpObjectFlag> ReadObjectFlags(ITx tx, long dbref)
		=> ReadObjectFlagRecords(tx, dbref).Select(MapFlag);

	/// <summary><see cref="ReadObjectFlags"/> without mapping each definition to a Library model.</summary>
	internal IEnumerable<FlagRecord> ReadObjectFlagRecords(ITx tx, long dbref)
	{
		var definitions = FlagDefinitions(tx);
		return tx.Dups(Tables.ObjFlag.Forward, Keys.Dbref(dbref))
			.Select(v => definitions.ByStoredName(Keys.ReadStr(v).ToUpperInvariant()))
			.OfType<FlagRecord>();
	}

	/// <summary>Every power assigned to <paramref name="dbref"/>, resolved through <see cref="Tables.Power"/>, same
	/// stale-edge handling as <see cref="ReadObjectFlags"/>.</summary>
	internal IEnumerable<SharpPower> ReadObjectPowers(ITx tx, long dbref)
	{
		var definitions = PowerDefinitions(tx);
		return tx.Dups(Tables.ObjPower.Forward, Keys.Dbref(dbref))
			.Select(v => definitions.ByStoredName(Keys.ReadStr(v).ToUpperInvariant()))
			.OfType<PowerRecord>()
			.Select(MapPower);
	}

	/// <summary>Exact case-insensitive name match first (the common case); falls back to a case-insensitive alias
	/// match, the first definition in key order listing it.</summary>
	private static FlagRecord? FindFlagRecord(DefinitionMap<FlagRecord> definitions, string name)
		=> string.IsNullOrEmpty(name) ? null : definitions.ByName(name) ?? definitions.ByAlias(name);

	/// <summary>Assigns <paramref name="role"/> to the object; false when it already holds it.</summary>
	private static bool AddObjectRole(ITx tx, long dbref, string role)
	{
		if (ReadObjectRoles(tx, dbref).Contains(role, StringComparer.OrdinalIgnoreCase)) return false;
		PutObjectRole(tx, dbref, role);
		return true;
	}

	/// <summary>Takes <paramref name="role"/> off the object; false when it does not hold it.</summary>
	private static bool RemoveObjectRole(ITx tx, long dbref, string role)
	{
		if (!ReadObjectRoles(tx, dbref).Contains(role, StringComparer.OrdinalIgnoreCase)) return false;
		DeleteObjectRole(tx, dbref, role);
		return true;
	}

	/// <summary>Sets an Allow override on <paramref name="scope"/>; false when one is already set.</summary>
	private static bool AllowObjectScope(ITx tx, long dbref, string scope)
	{
		if (ReadObjectOverrides(tx, dbref).TryGetValue(scope, out var state) && state == (int)PermissionState.Allow) return false;
		WriteObjectOverride(tx, dbref, scope, PermissionState.Allow);
		return true;
	}

	/// <summary>Clears an Allow override on <paramref name="scope"/> (a Deny stays); false when none is set.</summary>
	private static bool ClearObjectScope(ITx tx, long dbref, string scope)
	{
		if (!ReadObjectOverrides(tx, dbref).TryGetValue(scope, out var state) || state != (int)PermissionState.Allow) return false;
		WriteObjectOverride(tx, dbref, scope, PermissionState.Inherit);
		return true;
	}

	private static bool HasMembershipEdge(TableDef forward, ITx tx, long dbref, string name)
	{
		// The stored value is Keys.Upper(name) verbatim, so the bytes compare without decoding each one.
		var upper = Keys.Upper(name);
		return tx.Dups(forward, Keys.Dbref(dbref)).Any(value => value.AsSpan().SequenceEqual(upper));
	}

	private static void PutMembershipEdge((TableDef Forward, TableDef Reverse) edge, ITx tx, long dbref, string name)
	{
		tx.Put(edge.Forward, Keys.Dbref(dbref), Keys.Upper(name));
		tx.Put(edge.Reverse, Keys.Upper(name), Keys.Dbref(dbref));
	}

	private static void DeleteMembershipEdge((TableDef Forward, TableDef Reverse) edge, ITx tx, long dbref, string name)
	{
		tx.Delete(edge.Forward, Keys.Dbref(dbref), Keys.Upper(name));
		tx.Delete(edge.Reverse, Keys.Upper(name), Keys.Dbref(dbref));
	}

	private static SharpObjectFlag MapFlag(FlagRecord record) => new()
	{
		Id = null,
		Name = record.Name,
		Aliases = record.Aliases,
		Symbol = record.Symbol,
		SetPermissions = record.SetPermissions,
		UnsetPermissions = record.UnsetPermissions,
		System = record.System,
		Disabled = record.Disabled,
		TypeRestrictions = record.TypeRestrictions
	};

	private static SharpPower MapPower(PowerRecord record) => new()
	{
		Id = null,
		Name = record.Name,
		System = record.System,
		Disabled = record.Disabled,
		Aliases = record.AllAliases,
		Symbol = record.Symbol,
		SetPermissions = record.SetPermissions,
		UnsetPermissions = record.UnsetPermissions,
		TypeRestrictions = record.TypeRestrictions
	};

	#endregion
}
