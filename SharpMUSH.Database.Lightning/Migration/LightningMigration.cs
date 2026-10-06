using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Database.Seed;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using System.Text.Json;

namespace SharpMUSH.Database.Lightning;

public partial class LightningDatabase
{
	private const string InitialSeedMigrationId = "0001_initial_seed";
	private const string AncestorFormatsMigrationId = "0002_ancestor_formats";
	internal const string ExitSourceIndexMigrationId = "0003_exit_source_index";
	internal const string MailFolderCountMigrationId = "0004_mail_folder_count";
	internal const string PlayerAliasIndexMigrationId = "0005_player_alias_index";
	internal const string MailFolderNumberMigrationId = "0010_mail_folder_numbers";

	/// <summary>
	/// Idempotent world seed, run under <see cref="MigrateLock"/>:
	/// 1. upsert the shared flag/power/attribute-flag/attribute-entry definitions (always, cheap, and
	///    the only step a fresh install and a long-lived world both need every time);
	/// 2. seed objects #0-#9 once, gated on <see cref="InitialSeedMigrationId"/>;
	/// 3. apply pending core repairs, including the atomic exit source-index, mail folder-count and
	///    player-alias index rebuilds, the numbering of mail folders that predate folder numbers, and the
	///    move of wiki categories and tags into page text;
	/// 4. run every plugin's not-yet-applied <see cref="Library.Plugins.LightningMigrationStep"/>;
	/// 5. recompute <c>next_dbref</c> from the objects actually on disk;
	/// 6. ensure the singleton server-state row exists.
	/// </summary>
	public async ValueTask Migrate(CancellationToken cancellationToken = default)
	{
		await MigrateLock.WaitAsync(cancellationToken);
		try
		{
			await Store.WriteAsync(UpsertSeedDefinitions, cancellationToken);
			InvalidateDefinitions();
			await Store.WriteAsync(SeedRoles, cancellationToken);
			await Store.WriteAsync(MoveRoleBackedHolders, cancellationToken);

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

			await Store.WriteAsync(tx => RebuildExitSourceIndex(tx, cancellationToken), cancellationToken);
			await Store.WriteAsync(tx => RebuildMailFolderCounts(tx, cancellationToken), cancellationToken);
			await Store.WriteAsync(tx => RebuildPlayerAliases(tx, cancellationToken), cancellationToken);
			await RebuildReadIndexesAsync(cancellationToken);
			await MoveWikiCategoriesIntoTextAsync(cancellationToken);
			await MoveWikiProtectionIntoRequirementsAsync(cancellationToken);
			await AddMsspOptionAsync(cancellationToken);
			await Store.WriteAsync(tx => NumberMailFolders(tx, cancellationToken), cancellationToken);

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
			// A plugin step may have written definition rows of its own; whatever the copies held was read
			// before the seed or before that step.
			InvalidateDefinitions();
			MigrateLock.Release();
		}

		ReloadDefinitions();
	}

	/// <summary>Repairs both derived source indexes from stored exits and their authoritative Location edge.</summary>
	internal void RebuildExitSourceIndex(ITx tx, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var marker = Keys.Str("mig:" + ExitSourceIndexMigrationId);
		if (tx.TryGet(Tables.Meta, marker, out _)) return;
		Clear(Tables.Exit.Forward);
		Clear(Tables.Exit.Reverse);
		foreach (var (key, value) in tx.Range(Tables.Obj, []))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var record = Codec.Deserialize<ObjectRecord>(value);
			if (record.Type != DatabaseConstants.TypeExit) continue;
			var exit = Keys.ReadDbref(key);
			if (GetSingleEdge(tx, Tables.Location.Forward, exit) is not long source) continue;
			var location = ReadObject(tx, source);
			if (location is null || location.Value.Record.Type is not (DatabaseConstants.TypeRoom or DatabaseConstants.TypeThing or DatabaseConstants.TypePlayer)) continue;
			PutEdge(tx, Tables.Exit, source, exit);
		}
		cancellationToken.ThrowIfCancellationRequested();
		tx.Put(Tables.Meta, marker, Codec.Serialize(new MigrationRecord
		{
			Id = ExitSourceIndexMigrationId, AppliedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
		}));
		cancellationToken.ThrowIfCancellationRequested();

		void Clear(TableDef table)
		{
			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var batch = new List<(byte[] Key, byte[] Value)>(256);
				foreach (var entry in tx.Range(table, []).Take(256))
				{
					cancellationToken.ThrowIfCancellationRequested();
					batch.Add(entry);
				}
				if (batch.Count == 0) return;
				// Enumeration has disposed the cursor before this batch mutates its table.
				foreach (var (key, value) in batch)
				{
					cancellationToken.ThrowIfCancellationRequested();
					tx.Delete(table, key, value);
				}
			}
		}
	}

	/// <summary>Counts every recipient's box entries per folder into <see cref="Tables.MailCount"/>, once,
	/// for worlds whose mail predates the table. Counts the same entries <see cref="GetAllIncomingMailsAsync"/> yields:
	/// a box entry whose mail row is missing is not held mail.</summary>
	internal void RebuildMailFolderCounts(ITx tx, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var marker = Keys.Str("mig:" + MailFolderCountMigrationId);
		if (tx.TryGet(Tables.Meta, marker, out _)) return;
		var counts = new Dictionary<(long Recipient, string Folder), long>();
		foreach (var (key, _) in tx.Range(Tables.MailBox, []))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var recipient = Keys.ReadDbref(key.AsSpan(0, 8));
			var mailId = Keys.ReadDbref(key.AsSpan(key.Length - 8, 8));
			if (!tx.TryGet(Tables.Mail, MailKey(mailId), out var bytes)) continue;
			var folder = Codec.Deserialize<MailRecord>(bytes).Folder;
			counts[(recipient, folder)] = counts.GetValueOrDefault((recipient, folder)) + 1;
		}
		tx.DeletePrefix(Tables.MailCount, []);
		foreach (var ((recipient, folder), count) in counts)
		{
			tx.Put(Tables.MailCount, MailCountKey(recipient, folder), Keys.Dbref(count));
		}
		tx.Put(Tables.Meta, marker, Codec.Serialize(new MigrationRecord
		{
			Id = MailFolderCountMigrationId, AppliedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
		}));
	}

	/// <summary>
	/// Gives every mail folder a PennMUSH folder number, once, for worlds whose folders were kept by name alone:
	/// each player's folders that hold mail, and those its <see cref="ExpandedMailData"/> lists or makes current
	/// (whether or not the player holds any mail), are numbered by
	/// <see cref="ExpandedMailData.WithFolders"/> into its <see cref="ExpandedMailData.FolderNumbers"/>. Messages
	/// stay under the names they are stored under, so no mail row changes.
	/// </summary>
	internal void NumberMailFolders(ITx tx, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var marker = Keys.Str("mig:" + MailFolderNumberMigrationId);
		if (tx.TryGet(Tables.Meta, marker, out _)) return;

		var folders = new Dictionary<long, SortedSet<string>>();
		foreach (var (key, _) in tx.Range(Tables.MailBox, []))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var recipient = Keys.ReadDbref(key.AsSpan(0, 8));
			var mailId = Keys.ReadDbref(key.AsSpan(key.Length - 8, 8));
			if (!tx.TryGet(Tables.Mail, MailKey(mailId), out var bytes)) continue;
			var folder = Codec.Deserialize<MailRecord>(bytes).Folder;
			if (!folders.TryGetValue(recipient, out var held))
			{
				folders[recipient] = held = new SortedSet<string>(StringComparer.Ordinal);
			}

			held.Add(folder);
		}

		// A player can have folders and a current folder with no mail in them; their data numbers those.
		var dataType = nameof(ExpandedMailData);
		foreach (var (key, _) in tx.Range(Tables.ExpandedObj, []))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (key.Length <= 8) continue;
			var owner = Keys.ReadDbref(key.AsSpan(0, 8));
			if (!folders.ContainsKey(owner) && key.AsSpan().SequenceEqual(Keys.Composite(owner, dataType)))
			{
				folders[owner] = new SortedSet<string>(StringComparer.Ordinal);
			}
		}

		foreach (var (recipient, held) in folders)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var key = Keys.Composite(recipient, nameof(ExpandedMailData));
			var existing = tx.TryGet(Tables.ExpandedObj, key, out var stored) ? stored : null;
			var data = existing is null
				? new ExpandedMailData()
				: JsonSerializer.Deserialize<ExpandedMailData>(existing, ExpandedDataJsonOptions) ?? new ExpandedMailData();
			var numbered = data.WithFolders([.. held, .. data.Folders ?? [], .. data.ActiveFolder is { } active ? [active] : Array.Empty<string>()]);
			if (numbered.FolderNumbers is null || numbered == data) continue;

			var update = JsonSerializer.SerializeToUtf8Bytes(
				new Dictionary<string, object> { [nameof(ExpandedMailData.FolderNumbers)] = numbered.FolderNumbers },
				ExpandedDataJsonOptions);
			tx.Put(Tables.ExpandedObj, key, existing is null ? update : MergeExpandedData(existing, update));
		}

		tx.Put(Tables.Meta, marker, Codec.Serialize(new MigrationRecord
		{
			Id = MailFolderNumberMigrationId, AppliedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
		}));
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

		MergeRenamedFlag(tx, "ON_VACATION", "ON-VACATION");
		MoveHoldersByType(tx, "CLOUDY", "TERSE", ["PLAYER", "THING"]);

		foreach (var flag in _pluginFlags)
		{
			UpsertFlag(tx, flag.Name, flag.Symbol, flag.Aliases, flag.SetPermissions, flag.UnsetPermissions, flag.TypeRestrictions, flag.System);
		}

		foreach (var (name, symbol, inheritable) in AttributeFlagSeed.Flags)
		{
			tx.Put(Tables.AttrFlag, Keys.Upper(name),
				Codec.Serialize(new AttributeFlagRecord { Name = name, Symbol = symbol, Inheritable = inheritable, System = true }));
		}

		foreach (var (name, aliases, setPerms, unsetPerms) in PowerSeed.Powers)
		{
			var key = Keys.Upper(name);
			var currentPower = tx.TryGet(Tables.Power, key, out var existingPower)
				? Codec.Deserialize<PowerRecord>(existingPower)
				: null;

			// Same line as UpsertFlag: a power @power/add created is not the seed's to redefine.
			if (currentPower is { System: false })
			{
				continue;
			}

			var disabled = currentPower?.Disabled ?? false;
			tx.Put(Tables.Power, key, Codec.Serialize(new PowerRecord
			{
				Name = name,
				Aliases = aliases,
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
		if (!tx.TryGet(Tables.Power, oldKey, out var oldRecord))
		{
			return;
		}

		// PennMUSH renames its own struct; it has nothing to say about a power an administrator
		// created under the same name, which @power/add leaves System = false. Moving its grants and
		// deleting it would destroy exactly what the seed guard below refuses to overwrite.
		if (!Codec.Deserialize<PowerRecord>(oldRecord).System)
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

	/// <summary>
	/// TERSE used to be an alias of CLOUDY, so setting it stored a CLOUDY edge. Now that TERSE is its own
	/// flag and CLOUDY is an exit flag (#1404), a player or thing holding CLOUDY set TERSE: its edge
	/// moves to TERSE, and an exit keeps CLOUDY.
	/// </summary>
	private static void MoveHoldersByType(ITx tx, string fromName, string toName, string[] types)
	{
		var fromKey = Keys.Upper(fromName);
		var toKey = Keys.Upper(toName);
		if (!tx.TryGet(Tables.Flag, toKey, out _))
		{
			return;
		}

		// Materialise before mutating: the edge tables are being written inside this loop.
		var holders = tx.Dups(Tables.ObjFlag.Reverse, fromKey)
			.Select(value => Keys.ReadDbref(value))
			.Where(holder => tx.TryGet(Tables.Obj, Keys.Dbref(holder), out var bytes)
				&& types.Contains(Codec.Deserialize<ObjectRecord>(bytes).Type, StringComparer.OrdinalIgnoreCase))
			.ToArray();

		foreach (var holder in holders)
		{
			tx.Put(Tables.ObjFlag.Forward, Keys.Dbref(holder), toKey);
			tx.Put(Tables.ObjFlag.Reverse, toKey, Keys.Dbref(holder));
			tx.Delete(Tables.ObjFlag.Forward, Keys.Dbref(holder), fromKey);
			tx.Delete(Tables.ObjFlag.Reverse, fromKey, Keys.Dbref(holder));
		}
	}

	/// <summary>
	/// <see cref="MergeRenamedPower"/> for a flag: a seeded flag whose canonical name changed (ON_VACATION
	/// to PennMUSH's ON-VACATION, #1405). Holders are edges keyed by the flag's name, so they move onto the
	/// new key, the new record keeps the old one's <c>Disabled</c> state, and the old record is dropped.
	/// Idempotent: once the old record is gone every later boot finds nothing to do.
	/// </summary>
	private static void MergeRenamedFlag(ITx tx, string oldName, string newName)
	{
		var oldKey = Keys.Upper(oldName);
		if (!tx.TryGet(Tables.Flag, oldKey, out var oldRecord))
		{
			return;
		}

		// A flag an administrator created under the old name is not the seed's to move.
		var old = Codec.Deserialize<FlagRecord>(oldRecord);
		if (!old.System)
		{
			return;
		}

		var newKey = Keys.Upper(newName);
		if (old.Disabled && tx.TryGet(Tables.Flag, newKey, out var newRecord))
		{
			tx.Put(Tables.Flag, newKey, Codec.Serialize(Codec.Deserialize<FlagRecord>(newRecord) with { Disabled = true }));
		}

		// Materialise before mutating: the edge tables are being written inside this loop.
		var holders = tx.Dups(Tables.ObjFlag.Reverse, oldKey).Select(value => Keys.ReadDbref(value)).ToArray();

		foreach (var holder in holders)
		{
			tx.Put(Tables.ObjFlag.Forward, Keys.Dbref(holder), newKey);
			tx.Put(Tables.ObjFlag.Reverse, newKey, Keys.Dbref(holder));
			tx.Delete(Tables.ObjFlag.Forward, Keys.Dbref(holder), oldKey);
			tx.Delete(Tables.ObjFlag.Reverse, oldKey, Keys.Dbref(holder));
		}

		tx.Delete(Tables.Flag, oldKey);
	}

	/// <summary>
	/// Writes one flag definition, keeping the row's <c>Disabled</c> state. A definition the seed owns
	/// (<paramref name="system"/>) never overwrites a row an administrator created with
	/// <c>@flag/add</c>, which is always <c>System = false</c>: PennMUSH's own built-in add path stops
	/// the same way, with the "Don't double-add" guard in <c>add_flag_generic</c>
	/// (<c>src/flags.c:2252</c>) returning <c>FLAG_EXISTS</c> and leaving the existing flag alone. Rows
	/// the seed does own are still rewritten on every migration, which is how a corrected definition
	/// reaches a world that was seeded before the correction.
	/// </summary>
	private static void UpsertFlag(ITx tx, string name, string symbol, IReadOnlyList<string> aliases, IReadOnlyList<string> setPerms,
		IReadOnlyList<string> unsetPerms, IReadOnlyList<string> typeRestrictions, bool system)
	{
		var key = Keys.Upper(name);
		var present = tx.TryGet(Tables.Flag, key, out var existing);
		var current = present ? Codec.Deserialize<FlagRecord>(existing) : null;

		if (system && current is { System: false })
		{
			return;
		}

		var disabled = current?.Disabled ?? false;
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

	/// <summary>
	/// Writes what <see cref="BuiltInRoles.SeedChanges"/> asks for: the missing system roles, the starter
	/// roles into a world with none, and new in-game scopes on existing system roles. Runs on every start,
	/// here rather than in a hosted service, because the privilege checks read roles from the first
	/// command on. Then the two category lists: the seeded ones into a list with none, and any category a
	/// role or custom permission names but its list does not have, so nothing sits in a missing category.
	/// </summary>
	private static void SeedRoles(ITx tx)
	{
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var existing = tx.Range(Tables.Role, []).Select(e => MapRole(Codec.Deserialize<RoleRecord>(e.Value))).ToList();
		foreach (var role in BuiltInRoles.SeedChanges(existing, now))
		{
			tx.Put(Tables.Role, Keys.Str(role.Slug), Codec.Serialize(ToRoleRecord(role)));
		}

		SeedCategories(tx, CategoryKind.Role, now,
			tx.Range(Tables.Role, []).Select(e => Codec.Deserialize<RoleRecord>(e.Value).Category));
		SeedCategories(tx, CategoryKind.Permission, now,
			tx.Range(Tables.CustomPermission, []).Select(e => Codec.Deserialize<CustomPermissionRecord>(e.Value).Category));
	}

	/// <summary>
	/// Fills the category list <paramref name="kind"/>: its seeds when the list is empty, and any name in
	/// <paramref name="used"/> the list lacks.
	/// </summary>
	private static void SeedCategories(ITx tx, CategoryKind kind, long now, IEnumerable<string> used)
	{
		var categories = tx.Range(CategoryTable(kind), [])
			.Select(e => Codec.Deserialize<RoleCategoryRecord>(e.Value).Name)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		if (categories.Count == 0)
			foreach (var seed in Categories.Seeds(kind))
			{
				PutCategory(tx, kind, seed with { CreatedAt = now });
				categories.Add(seed.Name);
			}

		foreach (var name in used.Where(name => name.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Where(name => !categories.Contains(name)).ToList())
			PutCategory(tx, kind, new RoleCategory(name, "", now));
	}

	/// <summary>
	/// Moves WIZARD, ROYALTY and the built-in powers off the flag and power edges onto what stands for
	/// them now (see <see cref="RoleFlags"/> and <see cref="GamePowers"/>): an object role, or an Allow
	/// override on the power's scope. A world written before roles held privileges keeps every one.
	/// Idempotent: once the edges are gone a later start finds nothing to move.
	/// </summary>
	private static void MoveRoleBackedHolders(ITx tx)
	{
		foreach (var flag in RoleFlags.All)
		{
			var flagKey = Keys.Upper(flag.Name);
			foreach (var holder in tx.Dups(Tables.ObjFlag.Reverse, flagKey).Select(value => Keys.ReadDbref(value)).ToArray())
			{
				PutObjectRole(tx, holder, flag.Role);
				tx.Delete(Tables.ObjFlag.Forward, Keys.Dbref(holder), flagKey);
				tx.Delete(Tables.ObjFlag.Reverse, flagKey, Keys.Dbref(holder));
			}
		}

		foreach (var power in GamePowers.All)
		{
			var powerKey = Keys.Upper(power.Name);
			foreach (var holder in tx.Dups(Tables.ObjPower.Reverse, powerKey).Select(value => Keys.ReadDbref(value)).ToArray())
			{
				if (power.Role is { } role) PutObjectRole(tx, holder, role);
				else WriteObjectOverride(tx, holder, power.Scope, PermissionState.Allow);
				tx.Delete(Tables.ObjPower.Forward, Keys.Dbref(holder), powerKey);
				tx.Delete(Tables.ObjPower.Reverse, powerKey, Keys.Dbref(holder));
			}
		}
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
			IndexObjectType(tx, seedObject.Type, dbref);

			SetSingleEdge(tx, Tables.Location, dbref, seedObject.Location);
			SetSingleEdge(tx, Tables.Home, dbref, seedObject.Home);
			SetSingleEdge(tx, Tables.Owner, dbref, seedObject.Owner);

			foreach (var flagName in seedObject.Flags)
			{
				var flagKey = Keys.Upper(flagName);
				tx.Put(Tables.ObjFlag.Forward, Keys.Dbref(dbref), flagKey);
				tx.Put(Tables.ObjFlag.Reverse, flagKey, Keys.Dbref(dbref));
			}

			foreach (var role in seedObject.Roles)
			{
				PutObjectRole(tx, dbref, role);
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
