using System.Runtime.CompilerServices;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
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
/// </summary>
public partial class LightningDatabase
{
	#region Flags and Powers

	public ValueTask<SharpObjectFlag?> GetObjectFlagAsync(string name, CancellationToken cancellationToken = default)
		=> new(Store.Read(tx => FindFlagRecord(tx, name) is { } record ? MapFlag(record) : null));

	public IAsyncEnumerable<SharpObjectFlag> GetObjectFlagsAsync(CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<SharpObjectFlag>(GetAllFlagsCoreAsync);

	private async IAsyncEnumerable<SharpObjectFlag> GetAllFlagsCoreAsync([EnumeratorCancellation] CancellationToken ct)
	{
		var flags = Store.Read(tx => tx.Range(Tables.Flag, [])
			.Select(entry => MapFlag(Codec.Deserialize<FlagRecord>(entry.Value)))
			.ToList());

		foreach (var flag in flags)
		{
			ct.ThrowIfCancellationRequested();
			yield return flag;
		}
	}

	public async ValueTask<SharpObjectFlag?> CreateObjectFlagAsync(string name, string[]? aliases, string symbol,
		bool system, string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions,
		CancellationToken cancellationToken = default)
	{
		var record = new FlagRecord
		{
			Name = name,
			Symbol = symbol,
			Aliases = aliases ?? [],
			SetPermissions = setPermissions,
			UnsetPermissions = unsetPermissions,
			TypeRestrictions = typeRestrictions,
			System = system,
			Disabled = false
		};

		await Store.WriteAsync(tx => tx.Put(Tables.Flag, Keys.Upper(name), Codec.Serialize(record)), cancellationToken);
		return MapFlag(record);
	}

	public async ValueTask<bool> DeleteObjectFlagAsync(string name, CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx =>
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
		=> await Store.WriteAsync(tx =>
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
		=> await Store.WriteAsync(tx =>
		{
			var key = Keys.Upper(name);
			if (!tx.TryGet(Tables.Flag, key, out var bytes)) return false;
			var existing = Codec.Deserialize<FlagRecord>(bytes);
			if (existing.System) return false;

			tx.Put(Tables.Flag, key, Codec.Serialize(existing with { Disabled = disabled }));
			return true;
		}, cancellationToken);

	public ValueTask<SharpPower?> GetPowerAsync(string name, CancellationToken cancellationToken = default)
		=> new(Store.Read(tx => tx.TryGet(Tables.Power, Keys.Upper(name), out var bytes)
			? MapPower(Codec.Deserialize<PowerRecord>(bytes))
			: null));

	public IAsyncEnumerable<SharpPower> GetObjectPowersAsync(CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<SharpPower>(GetAllPowersCoreAsync);

	private async IAsyncEnumerable<SharpPower> GetAllPowersCoreAsync([EnumeratorCancellation] CancellationToken ct)
	{
		var powers = Store.Read(tx => tx.Range(Tables.Power, [])
			.Select(entry => MapPower(Codec.Deserialize<PowerRecord>(entry.Value)))
			.ToList());

		foreach (var power in powers)
		{
			ct.ThrowIfCancellationRequested();
			yield return power;
		}
	}

	public async ValueTask<SharpPower?> CreatePowerAsync(string name, string alias, string symbol, bool system,
		string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions,
		CancellationToken cancellationToken = default)
	{
		var record = new PowerRecord
		{
			Name = name,
			Alias = alias,
			Symbol = symbol,
			SetPermissions = setPermissions,
			UnsetPermissions = unsetPermissions,
			TypeRestrictions = typeRestrictions,
			System = system,
			Disabled = false
		};

		await Store.WriteAsync(tx => tx.Put(Tables.Power, Keys.Upper(name), Codec.Serialize(record)), cancellationToken);
		return MapPower(record);
	}

	public async ValueTask<bool> DeletePowerAsync(string name, CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx =>
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
		return await Store.WriteAsync(tx =>
		{
			if (!HasMembershipEdge(Tables.ObjPower.Forward, tx, dbrefKey, power.Name)) return false;

			DeleteMembershipEdge(Tables.ObjPower, tx, dbrefKey, power.Name);
			return true;
		}, cancellationToken);
	}

	public async ValueTask<bool> UpdatePowerAsync(string name, string alias, string symbol,
		string[] setPermissions, string[] unsetPermissions, string[] typeRestrictions,
		CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx =>
		{
			var key = Keys.Upper(name);
			if (!tx.TryGet(Tables.Power, key, out var bytes)) return false;
			var existing = Codec.Deserialize<PowerRecord>(bytes);
			if (existing.System) return false;

			tx.Put(Tables.Power, key, Codec.Serialize(existing with
			{
				Alias = alias,
				Symbol = symbol,
				SetPermissions = setPermissions,
				UnsetPermissions = unsetPermissions,
				TypeRestrictions = typeRestrictions
			}));
			return true;
		}, cancellationToken);

	public async ValueTask<bool> SetPowerDisabledAsync(string name, bool disabled, CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx =>
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
	internal static IEnumerable<SharpObjectFlag> ReadObjectFlags(ITx tx, long dbref)
		=> tx.Dups(Tables.ObjFlag.Forward, Keys.Dbref(dbref))
			.Select(v => Keys.ReadStr(v))
			.Select(name => tx.TryGet(Tables.Flag, Keys.Upper(name), out var bytes) ? Codec.Deserialize<FlagRecord>(bytes) : null)
			.Where(flag => flag is not null)
			.Select(flag => MapFlag(flag!));

	/// <summary>Every power assigned to <paramref name="dbref"/>, resolved through <see cref="Tables.Power"/>, same
	/// stale-edge handling as <see cref="ReadObjectFlags"/>.</summary>
	internal static IEnumerable<SharpPower> ReadObjectPowers(ITx tx, long dbref)
		=> tx.Dups(Tables.ObjPower.Forward, Keys.Dbref(dbref))
			.Select(v => Keys.ReadStr(v))
			.Select(name => tx.TryGet(Tables.Power, Keys.Upper(name), out var bytes) ? Codec.Deserialize<PowerRecord>(bytes) : null)
			.Where(power => power is not null)
			.Select(power => MapPower(power!));

	/// <summary>Exact case-insensitive name match first (the common case, one point lookup); falls back to a scan of
	/// the whole table (~60 rows) for a case-insensitive alias match, mirroring SurrealDatabase's alias handling.</summary>
	private static FlagRecord? FindFlagRecord(ITx tx, string name)
	{
		if (tx.TryGet(Tables.Flag, Keys.Upper(name), out var bytes))
		{
			return Codec.Deserialize<FlagRecord>(bytes);
		}

		return tx.Range(Tables.Flag, [])
			.Select(entry => Codec.Deserialize<FlagRecord>(entry.Value))
			.FirstOrDefault(record => record.Aliases.Any(alias => string.Equals(alias, name, StringComparison.OrdinalIgnoreCase)));
	}

	private static bool HasMembershipEdge(TableDef forward, ITx tx, long dbref, string name)
	{
		var upper = name.ToUpperInvariant();
		return tx.Dups(forward, Keys.Dbref(dbref)).Any(value => Keys.ReadStr(value) == upper);
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
		Alias = record.Alias,
		Symbol = record.Symbol,
		SetPermissions = record.SetPermissions,
		UnsetPermissions = record.UnsetPermissions,
		TypeRestrictions = record.TypeRestrictions
	};

	#endregion
}
