using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Database.Seed;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

public partial class LightningDatabase
{
	private const string InitialSeedMigrationId = "0001_initial_seed";
	private const string AncestorFormatsMigrationId = "0002_ancestor_formats";

	/// <summary>
	/// Idempotent world seed, run under <see cref="MigrateLock"/>:
	/// 1. upsert the shared flag/power/attribute-flag/attribute-entry definitions (always, cheap, and
	///    the only step a fresh install and a long-lived world both need every time);
	/// 2. seed objects #0-#9 once, gated on <see cref="InitialSeedMigrationId"/>;
	/// 3. run every plugin's not-yet-applied <see cref="Library.Plugins.LightningMigrationStep"/>;
	/// 4. recompute <c>next_dbref</c> from the objects actually on disk;
	/// 5. ensure the singleton server-state row exists.
	/// </summary>
	public async ValueTask Migrate(CancellationToken cancellationToken = default)
	{
		await MigrateLock.WaitAsync(cancellationToken);
		try
		{
			await Store.WriteAsync(UpsertSeedDefinitions, cancellationToken);

			var initialSeedApplied = Store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("mig:" + InitialSeedMigrationId), out _));
			if (!initialSeedApplied)
			{
				await Store.WriteAsync(ApplyInitialObjectSeed, cancellationToken);
				await RecordMigrationAsync(InitialSeedMigrationId, cancellationToken);
			}

			// Runs after the object seed because it writes attributes onto #4 owned by #1, and through
			// SetAttributeAsync rather than raw puts so all providers seed byte-identical values.
			var ancestorFormatsApplied = Store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("mig:" + AncestorFormatsMigrationId), out _));
			if (!ancestorFormatsApplied)
			{
				await AncestorSeed.SeedAncestorPlayerFormatsAsync(this, cancellationToken);
				await RecordMigrationAsync(AncestorFormatsMigrationId, cancellationToken);
			}

			foreach (var source in _migrationSources)
			{
				foreach (var step in source.LightningSteps)
				{
					var applied = Store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("mig:" + step.Id), out _));
					if (applied)
					{
						continue;
					}

					await step.Apply(this);
					await RecordMigrationAsync(step.Id, cancellationToken);
				}
			}

			await Store.WriteAsync(RecomputeNextDbref, cancellationToken);
			await Store.WriteAsync(EnsureServerState, cancellationToken);
		}
		finally
		{
			MigrateLock.Release();
		}
	}

	private async ValueTask RecordMigrationAsync(string id, CancellationToken cancellationToken)
		=> await Store.WriteAsync(tx => tx.Put(Tables.Meta, Keys.Str("mig:" + id),
			Codec.Serialize(new MigrationRecord { Id = id, AppliedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() })),
			cancellationToken);

	/// <summary>Upserts the built-in flag/power/attribute-flag/attribute-entry tables plus any plugin flags. Always runs, keyed by name, so a plugin installed later still lands its flags.</summary>
	private void UpsertSeedDefinitions(ITx tx)
	{
		foreach (var (name, symbol, aliases, setPerms, unsetPerms, typeRestrictions) in FlagSeed.Flags)
		{
			UpsertFlag(tx, name, symbol, aliases ?? [], setPerms, unsetPerms, typeRestrictions, system: true);
		}

		foreach (var flag in _pluginFlags)
		{
			UpsertFlag(tx, flag.Name, flag.Symbol, flag.Aliases, flag.SetPermissions, flag.UnsetPermissions, flag.TypeRestrictions, flag.System);
		}

		foreach (var (name, symbol, inheritable) in AttributeFlagSeed.Flags)
		{
			tx.Put(Tables.AttrFlag, Keys.Upper(name),
				Codec.Serialize(new AttributeFlagRecord { Name = name, Symbol = symbol, Inheritable = inheritable, System = true }));
		}

		foreach (var (name, alias, setPerms, unsetPerms) in PowerSeed.Powers)
		{
			var key = Keys.Upper(name);
			var disabled = tx.TryGet(Tables.Power, key, out var existingPower) && Codec.Deserialize<PowerRecord>(existingPower).Disabled;
			tx.Put(Tables.Power, key, Codec.Serialize(new PowerRecord
			{
				Name = name,
				Alias = alias,
				Symbol = "",
				SetPermissions = setPerms,
				UnsetPermissions = unsetPerms,
				TypeRestrictions = [],
				System = true,
				Disabled = disabled
			}));
		}

		MergeRenamedPower(tx, "Pueblo_Send", "Send_OOB");

		foreach (var (name, defaultFlags) in AttributeEntrySeed.Entries)
		{
			tx.Put(Tables.AttrEntry, Keys.Upper(name), Codec.Serialize(new AttributeEntryRecord { Name = name, DefaultFlags = defaultFlags }));
		}
	}

	/// <summary>
	/// PennMUSH renames Pueblo_Send to Send_OOB at load (<c>src/flags.c:850-855</c>) by rewriting the
	/// FLAG struct's name in place, so the struct keeps its identity and every object already holding
	/// the power follows the rename for free. Grants here are edges keyed by the power's name, so the
	/// equivalent is to move the edges onto the new name and drop the superseded record.
	/// Idempotent: once the old record is gone every later boot finds nothing to do.
	/// </summary>
	private static void MergeRenamedPower(ITx tx, string oldName, string newName)
	{
		var oldKey = Keys.Upper(oldName);
		if (!tx.TryGet(Tables.Power, oldKey, out _))
		{
			return;
		}

		var newKey = Keys.Upper(newName);

		// Materialise before mutating: the edge tables are being written inside this loop.
		var holders = tx.Dups(Tables.ObjPower.Reverse, oldKey).Select(value => Keys.ReadDbref(value)).ToArray();

		foreach (var holder in holders)
		{
			tx.Put(Tables.ObjPower.Forward, Keys.Dbref(holder), newKey);
			tx.Put(Tables.ObjPower.Reverse, newKey, Keys.Dbref(holder));
			tx.Delete(Tables.ObjPower.Forward, Keys.Dbref(holder), oldKey);
			tx.Delete(Tables.ObjPower.Reverse, oldKey, Keys.Dbref(holder));
		}

		tx.Delete(Tables.Power, oldKey);
	}

	private static void UpsertFlag(ITx tx, string name, string symbol, IReadOnlyList<string> aliases, IReadOnlyList<string> setPerms,
		IReadOnlyList<string> unsetPerms, IReadOnlyList<string> typeRestrictions, bool system)
	{
		var key = Keys.Upper(name);
		var disabled = tx.TryGet(Tables.Flag, key, out var existing) && Codec.Deserialize<FlagRecord>(existing).Disabled;
		tx.Put(Tables.Flag, key, Codec.Serialize(new FlagRecord
		{
			Name = name,
			Symbol = symbol,
			Aliases = [.. aliases],
			SetPermissions = [.. setPerms],
			UnsetPermissions = [.. unsetPerms],
			TypeRestrictions = [.. typeRestrictions],
			System = system,
			Disabled = disabled
		}));
	}

	/// <summary>Seeds objects #0-#9 (names/types/edges/flags from <see cref="InitialObjectSeed"/>) and sets <c>next_dbref</c> to 10. Runs once, gated by <see cref="InitialSeedMigrationId"/>.</summary>
	private static void ApplyInitialObjectSeed(ITx tx)
	{
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		foreach (var seedObject in InitialObjectSeed.Objects)
		{
			var dbref = seedObject.Dbref;
			tx.Put(Tables.Obj, Keys.Dbref(dbref), Codec.Serialize(new ObjectRecord
			{
				Name = seedObject.Name,
				Type = seedObject.Type,
				Aliases = [],
				CreationTime = now,
				ModifiedTime = now,
				PasswordHash = null,
				PasswordSalt = null,
				Quota = seedObject.Quota,
				Warnings = null,
				Locks = new Dictionary<string, LockRecord>()
			}));
			tx.Put(Tables.ObjName, Keys.Lower(seedObject.Name), Keys.Dbref(dbref));

			SetSingleEdge(tx, Tables.Location, dbref, seedObject.Location);
			SetSingleEdge(tx, Tables.Home, dbref, seedObject.Home);
			SetSingleEdge(tx, Tables.Owner, dbref, seedObject.Owner);

			foreach (var flagName in seedObject.Flags)
			{
				var flagKey = Keys.Upper(flagName);
				tx.Put(Tables.ObjFlag.Forward, Keys.Dbref(dbref), flagKey);
				tx.Put(Tables.ObjFlag.Reverse, flagKey, Keys.Dbref(dbref));
			}
		}

		tx.Put(Tables.Meta, Keys.Str("next_dbref"), Keys.Dbref(10));
	}

	/// <summary>Raises <c>next_dbref</c> to (highest key in <c>obj</c>) + 1 when that is larger than what is stored — the floor a wiped-and-reimported world needs, never a ceiling this lowers.</summary>
	private static void RecomputeNextDbref(ITx tx) => RecomputeCounter(tx, "next_dbref", Tables.Obj);

	private static void EnsureServerState(ITx tx)
	{
		if (!tx.TryGet(Tables.State, Keys.Str("state"), out _))
		{
			tx.Put(Tables.State, Keys.Str("state"), Codec.Serialize(new ServerStateRecord()));
		}
	}
}
