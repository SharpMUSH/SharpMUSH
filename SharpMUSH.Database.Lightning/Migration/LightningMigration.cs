using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Database.Lightning.Migration;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Database.Seed;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Database.Lightning;

public partial class LightningDatabase
{
	internal const string InitialMigrationId = "0001_initial";
	internal const string AccountCredentialMigrationId = "0002_account_credentials";
	internal const string OnboardingMigrationId = "0004_onboarding_progress_v1";
	internal const string SessionDigestMigrationId = "0003_session_digest_authority";

	/// <summary>The <c>meta</c> key holding the world format, a 4-byte big-endian integer.</summary>
	internal static ReadOnlySpan<byte> WorldFormatKey => "world_format"u8;

	/// <summary>
	/// The core migrations in the order they run, each with the world format it brings a world to. A change
	/// to what the provider stores is a migration added at the end with the next format, so
	/// <see cref="WorldFormat"/> moves with it and an older server refuses the world it writes.
	/// </summary>
	internal static readonly CoreMigration[] CoreMigrations =
	[
		new(1, InitialMigrationId, (db, _, ct) => db.ApplyInitialMigrationAsync(ct)),
		new(2, AccountCredentialMigrationId, (db, _, ct) => db.Store.WriteAsync(MigrateAccountCredentials, ct)),
		new(3, SessionDigestMigrationId, (db, _, ct) => db.Store.WriteAsync(MigrateSessionDigests, ct)),
		new(4, OnboardingMigrationId, (db, _, ct) => db.Store.WriteAsync(MigrateOnboarding, ct)),
		new(5, WikiCategoryCheckpointCodec.MigrationId, (db, options, ct) => db.MigrateWikiCategoryValuesAsync(options, ct)),
		new(6, WikiCountMigrationId, (db, _, ct) => db.Store.WriteAsync(MigrateWikiCounts, ct)),
	];

	/// <summary>The world format this server writes and the newest it opens: the last core migration's.</summary>
	internal static int WorldFormat => CoreMigrations[^1].Format;

	/// <summary>One core migration: the format it brings a world to, its <c>mig:</c> id, and its body, which skips itself once applied.</summary>
	internal sealed record CoreMigration(int Format, string Id,
		Func<LightningDatabase, WikiCategoryMigrationOptions, CancellationToken, ValueTask> Apply);

	/// <summary>
	/// Idempotent world setup, run under <see cref="MigrateLock"/>:
	/// 0. refuse a world whose stamped format is newer than <see cref="WorldFormat"/>, before anything is written;
	/// 1. upsert the shared flag/power/attribute-flag/attribute-entry definitions and the system roles and
	///    categories (always, cheap, and keyed by name, so a plugin installed later still lands its flags);
	/// 2. apply the initial migration once, gated on <see cref="InitialMigrationId"/>: objects #0-#9, their
	///    showcase image metadata, the ancestor player formats, the <c>system</c> wiki namespace's requirement
	///    and the <c>character</c> category's pin to the wiki home;
	/// 3. finish the rest of <see cref="CoreMigrations"/>, fill the stored configuration's defaults for settings
	///    added since it was written, then run every plugin's not-yet-applied
	///    <see cref="Library.Plugins.LightningMigrationStep"/>;
	/// 4. recompute <c>next_dbref</c> from the objects actually on disk;
	/// 5. ensure the singleton server-state row exists;
	/// 6. stamp the world with <see cref="WorldFormat"/>.
	/// </summary>
	public async ValueTask Migrate(CancellationToken cancellationToken = default)
		=> await _credentialGate.RunAsync(() => MigrateCoreAsync(cancellationToken), cancellationToken);

	private async Task MigrateCoreAsync(CancellationToken cancellationToken)
	{
		await MigrateLock.WaitAsync(cancellationToken);
		try
		{
			RefuseNewerWorld(Store.Read(ReadWorldFormat));

			var categoryOptions = WikiCategoryMigrationOptions.FromEnvironment();
			// Execute exactly the immutable ordered snapshot that passed reserved-ID admission.
			var pluginSteps = _migrationSources.SelectMany(source => source.LightningSteps).ToArray();
			if (pluginSteps.Any(step => string.Equals(step.Id, WikiCategoryCheckpointCodec.MigrationId, StringComparison.Ordinal)))
				throw new InvalidOperationException("A plugin migration uses the reserved wiki category migration ID.");
			await Store.WriteAsync(UpsertSeedDefinitions, cancellationToken);
			InvalidateDefinitions();
			await Store.WriteAsync(SeedRoles, cancellationToken);

			foreach (var migration in CoreMigrations)
			{
				await migration.Apply(this, categoryOptions, cancellationToken);
			}

			await Store.WriteAsync(FillStoredOptionDefaults, cancellationToken);

			foreach (var step in pluginSteps)
			{
				var applied = Store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("mig:" + step.Id), out _));
				if (applied)
				{
					continue;
				}

				await step.Apply(this);
				await RecordMigrationAsync(step.Id, cancellationToken);
			}

			await Store.WriteAsync(RecomputeNextDbref, cancellationToken);
			await Store.WriteAsync(EnsureServerState, cancellationToken);
			await Store.WriteAsync(StampWorldFormat, cancellationToken);
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

	/// <summary>Objects #0-#9 and the ancestor formats, once.</summary>
	private async ValueTask ApplyInitialMigrationAsync(CancellationToken cancellationToken)
	{
		if (Store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("mig:" + InitialMigrationId), out _))) return;

		await Store.WriteAsync(ApplyInitialSeed, cancellationToken);
		// After the object seed because it writes attributes onto #4 owned by #1, and through
		// SetAttributeAsync rather than raw puts so the stored values match what a write in game makes.
		await AncestorSeed.SeedAncestorFormatsAsync(this, cancellationToken);
		await RecordMigrationAsync(InitialMigrationId, cancellationToken);
	}

	/// <summary>The world's stamped format, or <see langword="null"/> for a new world or one stamped by no server yet.</summary>
	internal static int? ReadWorldFormat(ITx tx)
		=> tx.TryGet(Tables.Meta, WorldFormatKey, out var value) && value.Length == sizeof(int)
			? BinaryPrimitives.ReadInt32BigEndian(value)
			: null;

	/// <summary>
	/// Stops before anything is written when the world was stamped by a newer server: this one would read
	/// records it does not know with code that predates them, and rewrite them without what it dropped.
	/// </summary>
	private static void RefuseNewerWorld(int? stamped)
	{
		if (stamped is not { } format || format <= WorldFormat) return;

		throw new InvalidOperationException(
			$"This world is format {format}, written by a newer SharpMUSH; this server opens format {WorldFormat} and older. " +
			"Run the newer server, or restore a backup taken before the upgrade.");
	}

	private static void StampWorldFormat(ITx tx)
	{
		var value = new byte[sizeof(int)];
		BinaryPrimitives.WriteInt32BigEndian(value, WorldFormat);
		tx.Put(Tables.Meta, WorldFormatKey, value);
	}

	/// <summary>
	/// Gives the stored configuration a default for every section and setting added since it was written.
	/// Runs on every start, not once, because each release can add settings.
	/// </summary>
	private void FillStoredOptionDefaults(ITx tx)
	{
		var key = Keys.Str(nameof(SharpMUSHOptions));
		if (!tx.TryGet(Tables.ExpandedSrv, key, out var bytes)
			|| JsonNode.Parse(bytes) is not JsonObject stored) return;

		var defaults = JsonSerializer.SerializeToNode(SharpMUSHOptions.Default(), ExpandedDataJsonOptions)!.AsObject();
		var filled = StoredOptionsDefaults.Fill(stored, defaults);
		if (filled.Count == 0) return;

		tx.Put(Tables.ExpandedSrv, key, JsonSerializer.SerializeToUtf8Bytes(stored, ExpandedDataJsonOptions));
		_logger.LogInformation("Stored configuration given the shipped default for: {Settings}", string.Join(", ", filled));
	}

	private static void MigrateOnboarding(ITx tx)
	{
		var marker = Keys.Str("mig:" + OnboardingMigrationId);
		if (tx.TryGet(Tables.Meta, marker, out _)) return;
		var identity = Keys.Str("onboarding:game-id");
		if (!tx.TryGet(Tables.Meta, identity, out _))
			tx.Put(Tables.Meta, identity, Keys.Str(Guid.NewGuid().ToString("N")));
		tx.Put(Tables.Meta, marker, Codec.Serialize(new MigrationRecord { Id = OnboardingMigrationId, AppliedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }));
	}

	private static void MigrateSessionDigests(ITx tx)
	{
		var marker = Keys.Str("mig:" + SessionDigestMigrationId);
		if (tx.TryGet(Tables.Meta, marker, out _)) return;
		foreach (var row in tx.Range(Tables.Session, []))
			tx.Put(Tables.SessionDigest, System.Security.Cryptography.SHA256.HashData(row.Key), row.Value);
		tx.Put(Tables.Meta, marker, Codec.Serialize(new MigrationRecord
		{
			Id = SessionDigestMigrationId, AppliedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
		}));
	}

	private static void MigrateAccountCredentials(ITx tx)
	{
		var marker = Keys.Str("mig:" + AccountCredentialMigrationId);
		if (tx.TryGet(Tables.Meta, marker, out _)) return;
		foreach (var row in tx.Range(Tables.Account, []).ToArray())
		{
			var record = Codec.Deserialize<AccountRecord>(row.Value);
			tx.Put(Tables.Account, row.Key, Codec.Serialize(record with
			{
				PasswordHash = string.IsNullOrEmpty(record.PasswordHash) ? null : record.PasswordHash,
				AllowCharacterPasswordAccountLogin = true
			}));
		}
		tx.Put(Tables.Meta, marker, Codec.Serialize(new MigrationRecord
		{
			Id = AccountCredentialMigrationId, AppliedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
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

		foreach (var (name, defaultFlags) in AttributeEntrySeed.Entries)
		{
			// An index and a watch are staff's (@attribute/index, @attribute/watch), not the seed's: dropping an index here
			// would leave its rows unmaintained, and a watch would stop at the next restart.
			var stored = ReadAttributeEntryRecord(tx, name);
			tx.Put(Tables.AttrEntry, Keys.Upper(name), Codec.Serialize(new AttributeEntryRecord
			{
				Name = name,
				DefaultFlags = defaultFlags,
				Index = stored?.Index,
				Watch = stored?.Watch
			}));
		}
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

	/// <summary>Seeds objects #0-#9 (names/types/edges/flags from <see cref="InitialObjectSeed"/>), their
	/// portal image metadata, sets <c>next_dbref</c> to 10 and makes the <c>system</c> wiki namespace
	/// <c>wiki.admin</c>-only, then pins the <c>character</c> category to the wiki home. Runs once, gated
	/// by <see cref="InitialMigrationId"/>.</summary>
	private static void ApplyInitialSeed(ITx tx)
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

		foreach (var image in InitialObjectImageSeed.Objects)
		{
			WriteInitialAttribute(tx, image.Dbref, "IMAGE", image.Image);
			WriteInitialAttribute(tx, image.Dbref, "IMAGE`BANNER", image.Banner);
			WriteInitialAttribute(tx, image.Dbref, "IMAGE`ALT", image.Alt);
			WriteInitialAttribute(tx, image.Dbref, "IMAGE`FOCAL", image.Focal);
		}

		// A room shows its picture (the Ancestor Room's DESCFORMAT) only when it may show pictures itself.
		var rooms = InitialObjectSeed.Objects.Where(seed => seed.Type == "ROOM").Select(seed => seed.Dbref).ToHashSet();
		foreach (var room in InitialObjectImageSeed.Objects.Where(image => rooms.Contains(image.Dbref)))
		{
			WriteObjectOverride(tx, room.Dbref, PortalPermission.GamePower("Send_Image"), PermissionState.Allow);
		}

		tx.Put(Tables.Meta, Keys.Str("next_dbref"), Keys.Dbref(10));

		var admin = new[] { PortalPermission.WikiAdmin };
		tx.Put(Tables.WikiRequirement, WikiRequirementKey(WikiRuleScope.Namespace, "system"), Codec.Serialize(new WikiRequirementRecord
		{
			Scope = "namespace",
			Key = "system",
			Required = new() { ["create"] = admin, ["edit"] = admin, ["delete"] = admin },
			UpdatedBy = "#1",
			UpdatedAt = now,
		}));

		foreach (var category in WikiHelpers.SeededPinnedCategories)
		{
			tx.Put(Tables.WikiPin, WikiPinKey(category), WikiPinValue);
		}
	}

	private static void WriteInitialAttribute(ITx tx, long dbref, string longName, string value)
		=> WriteAttributePath(tx, dbref, ReadObjectHeader(tx, dbref)?.Type ?? string.Empty, new PreparedAttributeWrite(
			longName.Split('`'),
			Keys.Str(MarkupTextSerializer.Serialize(MString.Plain(value))),
			1,
			[],
			MString.Plain(value)), WatchedAttributes.None.Names);

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
