using DotNext.Threading;
using MarkupString;
using Microsoft.Extensions.Logging;
using OneOf.Types;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Database.Seed;
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
	#region Migration

	public async Task<IStagingDatabase> CreateStagingAsync(CancellationToken ct = default)
	{
		var stagingId = Guid.NewGuid().ToString("N")[..8];
		var stagingDbName = $"staging_{stagingId}";

		logger.LogInformation("Creating SurrealDB staging database: {StagingDb}", stagingDbName);

		// Create a new SurrealDB client pointing at the staging database within the same namespace
		var stagingClient = new SurrealDbClient(
			$"Endpoint=mem://;Namespace=sharpmush;Database={stagingDbName}");
		await stagingClient.Connect(ct);

		var staging = new SurrealStagingDatabase(
			logger, stagingClient, passwordService, relations,
			liveDatabase: this, liveClient: db,
			stagingDbName: stagingDbName, stagingId: stagingId);

		await staging.Migrate(ct);

		return staging;
	}

	public async ValueTask WipeDatabaseAsync(CancellationToken cancellationToken = default)
	{
		logger.LogWarning("WIPING DATABASE - This is destructive and irreversible!");

		await db.RawQuery("REMOVE DATABASE IF EXISTS sharpmush;");

		await Migrate(cancellationToken);

		logger.LogInformation("Database wiped and re-initialized successfully.");
	}

	public async ValueTask Migrate(CancellationToken cancellationToken = default)
	{
		await MigrateLock.WaitAsync(cancellationToken);
		try
		{
			logger.LogInformation("Migrating SurrealDB Database");

			// Migrations are recorded in the database: a migration id that is already present is never
			// run again, so a restart against a persistent store re-applies nothing. Everything outside
			// the gated block is idempotent (IF NOT EXISTS / UPSERT), so re-running Migrate() is always
			// safe — no in-process migration state exists, and callers are rare (boot, staging, wipe).
			var applyInitial = !await MigrationAppliedAsync(InitialSeedMigrationId, cancellationToken);

			var indexQueries = new[]
			{
				"DEFINE INDEX IF NOT EXISTS object_key ON object FIELDS key UNIQUE",
				"DEFINE INDEX IF NOT EXISTS object_type ON object FIELDS type",
				"DEFINE INDEX IF NOT EXISTS object_name ON object FIELDS name",
				"DEFINE INDEX IF NOT EXISTS player_key ON player FIELDS key UNIQUE",
				"DEFINE INDEX IF NOT EXISTS room_key ON room FIELDS key UNIQUE",
				"DEFINE INDEX IF NOT EXISTS thing_key ON thing FIELDS key UNIQUE",
				"DEFINE INDEX IF NOT EXISTS exit_key ON exit FIELDS key UNIQUE",
				"DEFINE INDEX IF NOT EXISTS attribute_key ON attribute FIELDS key",
				"DEFINE INDEX IF NOT EXISTS object_flag_name ON object_flag FIELDS name UNIQUE",
				"DEFINE INDEX IF NOT EXISTS power_name ON power FIELDS name UNIQUE",
				// "No letter" is the empty string, not an absent field: @power/letter's collision scan
				// must read the same shape from every power row.
				"UPDATE power SET symbol = '' WHERE symbol = NONE",
				"DEFINE INDEX IF NOT EXISTS attribute_flag_name ON attribute_flag FIELDS name UNIQUE",
				"DEFINE INDEX IF NOT EXISTS attribute_entry_name ON attribute_entry FIELDS name UNIQUE",
				"DEFINE INDEX IF NOT EXISTS channel_name ON channel FIELDS name UNIQUE",
				"DEFINE INDEX IF NOT EXISTS counter_name ON counter FIELDS name UNIQUE",
				"DEFINE INDEX IF NOT EXISTS mail_key ON mail FIELDS key UNIQUE",
				"DEFINE INDEX IF NOT EXISTS session_account ON session FIELDS accountId",
				"DEFINE INDEX IF NOT EXISTS session_ip ON session FIELDS originIp",
				// Category is part of page identity: backfill legacy null categories, then key on all three.
				"UPDATE wiki_page SET category = 'general' WHERE category = NONE",
				"DEFINE INDEX IF NOT EXISTS wiki_page_slug ON wiki_page FIELDS namespace, category, slug UNIQUE",
				"DEFINE INDEX IF NOT EXISTS wiki_page_updated ON wiki_page FIELDS updatedAt",
				"DEFINE INDEX IF NOT EXISTS wiki_page_ns ON wiki_page FIELDS namespace",
				"DEFINE INDEX IF NOT EXISTS wiki_page_category ON wiki_page FIELDS category",
				"DEFINE INDEX IF NOT EXISTS wiki_revision_page ON wiki_revision FIELDS pageId",

				// Backfill BEFORE the new unique index, or the DEFINE fails on rows with no locale.
				// Pages get the configured default; revisions get the empty source-stream marker, because
				// every pre-existing revision belongs to its page's own locale stream.
				$"UPDATE wiki_page SET sourceLocale = '{WikiOptions.DefaultLocaleFallback}' WHERE sourceLocale = NONE OR sourceLocale = ''",
				"UPDATE wiki_revision SET locale = '' WHERE locale = NONE",

				// The old index is UNIQUE on (pageId, revisionNumber) and would reject a translation's
				// revision 1 outright. DEFINE INDEX IF NOT EXISTS will not alter an existing index, so the
				// drop is mandatory rather than tidiness.
				"REMOVE INDEX IF EXISTS wiki_revision_page_rev ON wiki_revision",
				"DEFINE INDEX IF NOT EXISTS wiki_revision_page_locale_rev ON wiki_revision FIELDS pageId, locale, revisionNumber UNIQUE",

				"DEFINE INDEX IF NOT EXISTS wiki_translation_page_locale ON wiki_translation FIELDS pageId, locale UNIQUE",
				"DEFINE INDEX IF NOT EXISTS wiki_translation_locale ON wiki_translation FIELDS locale",
				// Softcode package manager system data (decisions 20.3, 20.13)
				"DEFINE INDEX IF NOT EXISTS sys_package_pkgid ON sys_package FIELDS packageId UNIQUE",
				"DEFINE INDEX IF NOT EXISTS sys_package_object_pkg_ref ON sys_package_object FIELDS packageId, refName UNIQUE",
				"DEFINE INDEX IF NOT EXISTS sys_package_object_objid ON sys_package_object FIELDS objid",
				"DEFINE INDEX IF NOT EXISTS sys_managed_attribute_key ON sys_managed_attribute FIELDS packageId, objid, attribute UNIQUE",
				"DEFINE INDEX IF NOT EXISTS sys_managed_attribute_objid ON sys_managed_attribute FIELDS objid",
				"DEFINE INDEX IF NOT EXISTS sys_package_dependency_pkg ON sys_package_dependency FIELDS packageId",
				"DEFINE INDEX IF NOT EXISTS sys_package_dependency_dep ON sys_package_dependency FIELDS dependsOnId",
				"DEFINE INDEX IF NOT EXISTS sys_remote_name ON sys_remote FIELDS name UNIQUE",
				"DEFINE INDEX IF NOT EXISTS sys_package_revision_key ON sys_package_revision FIELDS packageId, revision UNIQUE",
				"DEFINE INDEX IF NOT EXISTS role_slug ON role FIELDS slug UNIQUE",
				"DEFINE INDEX IF NOT EXISTS has_attribute_in ON has_attribute FIELDS in",
				"DEFINE INDEX IF NOT EXISTS has_attribute_out ON has_attribute FIELDS out",
				"DEFINE INDEX IF NOT EXISTS has_attribute_flag_in ON has_attribute_flag FIELDS in",
				"DEFINE INDEX IF NOT EXISTS has_attribute_entry_in ON has_attribute_entry FIELDS in",
				"DEFINE INDEX IF NOT EXISTS has_attribute_entry_out ON has_attribute_entry FIELDS out",
				"DEFINE INDEX IF NOT EXISTS has_attribute_owner_in ON has_attribute_owner FIELDS in",
				"DEFINE INDEX IF NOT EXISTS has_attribute_owner_out ON has_attribute_owner FIELDS out",
				"DEFINE INDEX IF NOT EXISTS has_flags_in ON has_flags FIELDS in",
				"DEFINE INDEX IF NOT EXISTS has_powers_in ON has_powers FIELDS in",
				"DEFINE INDEX IF NOT EXISTS has_owner_in ON has_owner FIELDS in UNIQUE",
				"DEFINE INDEX IF NOT EXISTS has_owner_out ON has_owner FIELDS out",
				"DEFINE INDEX IF NOT EXISTS has_home_in ON has_home FIELDS in UNIQUE",
				"DEFINE INDEX IF NOT EXISTS has_zone_in ON has_zone FIELDS in UNIQUE",
				"DEFINE INDEX IF NOT EXISTS has_zone_out ON has_zone FIELDS out",
				"DEFINE INDEX IF NOT EXISTS has_parent_in ON has_parent FIELDS in UNIQUE",
				"DEFINE INDEX IF NOT EXISTS has_parent_out ON has_parent FIELDS out",
				"DEFINE INDEX IF NOT EXISTS at_location_in ON at_location FIELDS in UNIQUE",
				"DEFINE INDEX IF NOT EXISTS at_location_out ON at_location FIELDS out",
				"DEFINE INDEX IF NOT EXISTS is_object_in ON is_object FIELDS in UNIQUE",
				"DEFINE INDEX IF NOT EXISTS member_of_channel_in ON member_of_channel FIELDS in",
				"DEFINE INDEX IF NOT EXISTS member_of_channel_out ON member_of_channel FIELDS out",
				"DEFINE INDEX IF NOT EXISTS owner_of_channel_in ON owner_of_channel FIELDS in",
				"DEFINE INDEX IF NOT EXISTS received_mail_in ON received_mail FIELDS in",
				"DEFINE INDEX IF NOT EXISTS received_mail_out ON received_mail FIELDS out",
				"DEFINE INDEX IF NOT EXISTS mail_sender_in ON mail_sender FIELDS in",
				"DEFINE INDEX IF NOT EXISTS mail_sender_out ON mail_sender FIELDS out",
				"DEFINE INDEX IF NOT EXISTS object_data_key_type ON object_data FIELDS objectKey, dataType UNIQUE",
				// RELATE has no built-in uniqueness (every execution appends a new edge record), so edge
				// cardinality is enforced at the schema level: the single-cardinality relations above
				// (one location / home / owner / zone / parent / object node per subject — the runtime
				// maintains them via delete-then-RELATE) are UNIQUE on `in`, and the multi-valued
				// flag/power relations are UNIQUE per (in, out) pair. A duplicate RELATE errors instead
				// of silently doubling room contents or flag letters. NOTE: databases created before
				// migration records existed are NOT upgradable in place — IF NOT EXISTS keeps their old
				// non-unique indexes, existing duplicates would fail these DEFINEs, and the seed would
				// re-run over live data. Deploy this schema on a clean database.
				"DEFINE INDEX IF NOT EXISTS has_flags_in_out ON has_flags FIELDS in, out UNIQUE",
				"DEFINE INDEX IF NOT EXISTS has_powers_in_out ON has_powers FIELDS in, out UNIQUE"
			};

			foreach (var q in indexQueries)
			{
				await ExecuteAsync(q, cancellationToken);
			}

			// Built-in flag / power / attribute-entry definitions and plugin-contributed flags are
			// idempotent UPSERTs keyed on name — always run, so additions in newer versions reach
			// existing databases without needing a new migration id.
			await CreateInitialFlags(cancellationToken);

			await CreateInitialAttributeFlags(cancellationToken);

			await CreateInitialPowers(cancellationToken);

			await CreateInitialAttributeEntries(cancellationToken);

			await SeedPluginFlags(cancellationToken);

			if (applyInitial)
			{
				await ApplyInitialSeedAsync(cancellationToken);
			}

			await RunPluginSurrealMigrations(cancellationToken);

			await RecomputeNextObjectKeyAsync(cancellationToken);

			if (applyInitial)
			{
				// Seed the default FORMAT`* attributes on the Ancestor Player (#4) so a plain player
				// inherits the PennMUSH-style say/pose/semipose/emit render templates.
				await AncestorSeed.SeedAncestorPlayerFormatsAsync(this, cancellationToken);

				// Recorded after the seed statements run, so an exception during the seed re-applies
				// it next boot. (SurrealQL-level errors are logged by ExecuteAsync, not thrown — only
				// .NET failures abort before the record is written.)
				await ExecuteAsync(
					$"CREATE migration:⟨{InitialSeedMigrationId}⟩ SET appliedAt = $now",
					new Dictionary<string, object?> { ["now"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
					cancellationToken);
			}

			await EnsureServerStateAsync(cancellationToken);

			logger.LogInformation("SurrealDB Migration Completed");
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "SurrealDB Migration Failed");
			throw;
		}
		finally
		{
			MigrateLock.Release();
		}
	}

	/// <summary>
	/// The one-time schema/data migrations this provider knows, by id. A migration id already
	/// recorded in the database's <c>migration</c> table is never applied again.
	/// </summary>
	private const string InitialSeedMigrationId = "0001_initial_seed";

	/// <summary>
	/// Recomputes the dbref allocator from what the database actually holds. Runs on every
	/// Migrate() (a restart must not re-hand-out keys that already exist — the next @create's
	/// UPSERT would silently overwrite that object) and after staging promotion, where the live
	/// instance's client is swapped to a database whose keys this instance never allocated.
	/// </summary>
	internal async Task RecomputeNextObjectKeyAsync(CancellationToken ct = default)
	{
		var maxKeyResponse = await ExecuteAsync(
			"SELECT VALUE key FROM object ORDER BY key DESC LIMIT 1", ct);
		var maxKeys = maxKeyResponse.GetValue<List<int>>(0);
		_nextObjectKey = Math.Max(9, maxKeys is { Count: > 0 } ? maxKeys[0] : 9);
	}

	private async Task<bool> MigrationAppliedAsync(string migrationId, CancellationToken ct)
	{
		var response = await ExecuteAsync(
			$"SELECT VALUE appliedAt FROM migration:⟨{migrationId}⟩", ct);
		var rows = response.GetValue<List<long>>(0);
		return rows is { Count: > 0 };
	}

	// First-run setup inference: create the server_state doc if missing; a game that already has
	// a claimed account (non-empty passwordHash) must not re-open the wizard. Runs every Migrate()
	// call, but only ever acts when the state doc doesn't exist yet — an existing record (whatever
	// its value) is never overwritten, so this never downgrades a completed setup.
	private async Task EnsureServerStateAsync(CancellationToken cancellationToken)
	{
		var existing = await ExecuteAsync("SELECT * FROM server_state:state",
			new Dictionary<string, object?>(), cancellationToken);
		if (existing.GetValue<List<ServerStateDbRecord>>(0) is { Count: > 0 })
			return;

		var claimed = await ExecuteAsync(
			"SELECT * FROM account WHERE passwordHash != NONE AND passwordHash != '' LIMIT 1",
			new Dictionary<string, object?>(), cancellationToken);
		var setupCompleted = claimed.GetValue<List<AccountDbRecord>>(0) is { Count: > 0 };

		await ExecuteAsync("CREATE server_state:state CONTENT { setupCompleted: $value }",
			new Dictionary<string, object?> { ["value"] = setupCompleted }, cancellationToken);
	}

	/// <summary>
	/// The 0001_initial_seed migration: the #0-#9 object seed and its graph edges. Runs at most
	/// once per database (see <see cref="MigrationAppliedAsync"/>).
	/// </summary>
	private async Task ApplyInitialSeedAsync(CancellationToken cancellationToken)
	{
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		await ExecuteAsync(
			"UPSERT object:0 SET name = 'Room Zero', type = 'ROOM', creationTime = $now, modifiedTime = $now, locks = '{}', warnings = 0, key = 0",
			new Dictionary<string, object?> { ["now"] = now },
			cancellationToken);
		await ExecuteAsync(
			"UPSERT room:0 SET key = 0, aliases = []",
			cancellationToken);

		await ExecuteAsync(
			"UPSERT object:1 SET name = 'God', type = 'PLAYER', creationTime = $now, modifiedTime = $now, locks = '{}', warnings = 0, key = 1",
			new Dictionary<string, object?> { ["now"] = now },
			cancellationToken);
		await ExecuteAsync(
			"UPSERT player:1 SET key = 1, passwordHash = '', passwordSalt = '', aliases = [], quota = 999999",
			cancellationToken);

		await ExecuteAsync(
			"UPSERT object:2 SET name = 'Master Room', type = 'ROOM', creationTime = $now, modifiedTime = $now, locks = '{}', warnings = 0, key = 2",
			new Dictionary<string, object?> { ["now"] = now },
			cancellationToken);
		await ExecuteAsync(
			"UPSERT room:2 SET key = 2, aliases = []",
			cancellationToken);

		// A room has no location/home edges — it is a pure attribute holder.
		await ExecuteAsync(
			"UPSERT object:3 SET name = 'Ancestor Room', type = 'ROOM', creationTime = $now, modifiedTime = $now, locks = '{}', warnings = 0, key = 3",
			new Dictionary<string, object?> { ["now"] = now },
			cancellationToken);
		await ExecuteAsync("UPSERT room:3 SET key = 3, aliases = []", cancellationToken);

		await ExecuteAsync(
			"UPSERT object:4 SET name = 'Ancestor Player', type = 'THING', creationTime = $now, modifiedTime = $now, locks = '{}', warnings = 0, key = 4",
			new Dictionary<string, object?> { ["now"] = now },
			cancellationToken);
		await ExecuteAsync("UPSERT thing:4 SET key = 4, aliases = []", cancellationToken);

		await ExecuteAsync(
			"UPSERT object:5 SET name = 'Ancestor Exit', type = 'THING', creationTime = $now, modifiedTime = $now, locks = '{}', warnings = 0, key = 5",
			new Dictionary<string, object?> { ["now"] = now },
			cancellationToken);
		await ExecuteAsync("UPSERT thing:5 SET key = 5, aliases = []", cancellationToken);

		await ExecuteAsync(
			"UPSERT object:6 SET name = 'Ancestor Thing', type = 'THING', creationTime = $now, modifiedTime = $now, locks = '{}', warnings = 0, key = 6",
			new Dictionary<string, object?> { ["now"] = now },
			cancellationToken);
		await ExecuteAsync("UPSERT thing:6 SET key = 6, aliases = []", cancellationToken);

		await ExecuteAsync(
			"UPSERT object:7 SET name = 'Package Manager', type = 'PLAYER', creationTime = $now, modifiedTime = $now, locks = '{}', warnings = 0, key = 7",
			new Dictionary<string, object?> { ["now"] = now },
			cancellationToken);
		await ExecuteAsync(
			"UPSERT player:7 SET key = 7, passwordHash = '', passwordSalt = '', aliases = [], quota = 999999",
			cancellationToken);

		await ExecuteAsync(
			"UPSERT object:8 SET name = 'HTTP Handler', type = 'THING', creationTime = $now, modifiedTime = $now, locks = '{}', warnings = 0, key = 8",
			new Dictionary<string, object?> { ["now"] = now },
			cancellationToken);
		await ExecuteAsync("UPSERT thing:8 SET key = 8, aliases = []", cancellationToken);

		await ExecuteAsync(
			"UPSERT object:9 SET name = 'Event Handler', type = 'THING', creationTime = $now, modifiedTime = $now, locks = '{}', warnings = 0, key = 9",
			new Dictionary<string, object?> { ["now"] = now },
			cancellationToken);
		await ExecuteAsync("UPSERT thing:9 SET key = 9, aliases = []", cancellationToken);

		// Seed graph edges. Bare RELATE is safe here: this method runs at most once per database
		// (gated by the recorded migration id), and the UNIQUE edge indexes are the backstop.
		var seedEdges = new (string From, string Edge, string To)[]
		{
			("room:0", "is_object", "object:0"),
			("player:1", "is_object", "object:1"),
			("room:2", "is_object", "object:2"),
			("room:3", "is_object", "object:3"),
			("thing:4", "is_object", "object:4"),
			("thing:5", "is_object", "object:5"),
			("thing:6", "is_object", "object:6"),
			("player:7", "is_object", "object:7"),
			("thing:8", "is_object", "object:8"),
			("thing:9", "is_object", "object:9"),

			("player:1", "at_location", "room:0"),
			("player:7", "at_location", "room:0"),
			("thing:4", "at_location", "room:2"),
			("thing:5", "at_location", "room:2"),
			("thing:6", "at_location", "room:2"),
			("thing:8", "at_location", "room:2"),
			("thing:9", "at_location", "room:2"),

			("player:1", "has_home", "room:0"),
			("player:7", "has_home", "room:0"),
			("thing:4", "has_home", "room:2"),
			("thing:5", "has_home", "room:2"),
			("thing:6", "has_home", "room:2"),
			("thing:8", "has_home", "room:2"),
			("thing:9", "has_home", "room:2"),

			("object:0", "has_owner", "player:1"),
			("object:1", "has_owner", "player:1"),
			("object:2", "has_owner", "player:1"),
			// God owns the ancestors (#3-#6) and the handler things (#8, #9); PM (#7) owns itself
			("object:3", "has_owner", "player:1"),
			("object:4", "has_owner", "player:1"),
			("object:5", "has_owner", "player:1"),
			("object:6", "has_owner", "player:1"),
			("object:8", "has_owner", "player:1"),
			("object:9", "has_owner", "player:1"),
			("object:7", "has_owner", "player:7"),
		};
		foreach (var (from, edge, to) in seedEdges)
		{
			await ExecuteAsync($"RELATE {from}->{edge}->{to}", cancellationToken);
		}

		// HTTP Handler (#8) and Event Handler (#9) are WIZARD so their handler softcode runs
		// with its own elevated permissions (each executes as itself).
		foreach (var wizard in (string[])["object:1", "object:7", "object:8", "object:9"])
		{
			await ExecuteAsync($"RELATE {wizard}->has_flags->object_flag:WIZARD", cancellationToken);
		}
	}

	/// <summary>
	/// The built-in flag table is <see cref="FlagSeed.Flags"/>, shared with every provider. The UPSERT sets
	/// every field unconditionally on each Migrate(), so a changed definition (MYOPIC splitting off MISTRUST,
	/// say) lands on an existing row the next time the game boots — no separate repair migration.
	/// </summary>
	private async Task CreateInitialFlags(CancellationToken ct)
	{
		foreach (var f in FlagSeed.Flags)
		{
			var parameters = new Dictionary<string, object?>
			{
				["name"] = f.Name,
				["symbol"] = f.Symbol,
				["aliases"] = f.Aliases ?? Array.Empty<string>(),
				["setPerms"] = f.SetPerms,
				["unsetPerms"] = f.UnsetPerms,
				["typeRestrictions"] = f.TypeRestrictions
			};

			await ExecuteAsync(
				$"UPSERT object_flag:{SanitizeRecordId(f.Name)} SET name = $name, symbol = $symbol, system = true, disabled = false, aliases = $aliases, setPermissions = $setPerms, unsetPermissions = $unsetPerms, typeRestrictions = $typeRestrictions",
				parameters, ct);
		}
	}

	/// <summary>
	/// Seed plugin-contributed flags (Phase 2a <see cref="IFlagSource"/>) via the same idempotent UPSERT
	/// the built-in flag seed uses, so the flags exist after migration on SurrealDB.
	/// </summary>
	private async Task SeedPluginFlags(CancellationToken ct)
	{
		foreach (var f in PluginFlags)
		{
			try
			{
				var parameters = new Dictionary<string, object?>
				{
					["name"] = f.Name,
					["symbol"] = f.Symbol,
					["system"] = f.System,
					["aliases"] = f.Aliases.ToArray(),
					["setPerms"] = f.SetPermissions.ToArray(),
					["unsetPerms"] = f.UnsetPermissions.ToArray(),
					["typeRestrictions"] = f.TypeRestrictions.ToArray()
				};

				await ExecuteAsync(
					$"UPSERT object_flag:{SanitizeRecordId(f.Name)} SET name = $name, symbol = $symbol, system = $system, disabled = false, aliases = $aliases, setPermissions = $setPerms, unsetPermissions = $unsetPerms, typeRestrictions = $typeRestrictions",
					parameters, ct);

				logger.LogInformation("Seeded plugin flag '{Flag}' (SurrealDB).", f.Name);
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Failed to seed plugin flag '{Flag}' (SurrealDB); continuing.", f.Name);
			}
		}
	}

	/// <summary>
	/// Run each plugin's SurrealQL migration statements (Phase 2a <see cref="IMigrationSource.SurrealStatements"/>)
	/// after the built-in seed batch. Required migrations abort startup; legacy sources keep log-and-continue behavior.
	/// </summary>
	private async Task RunPluginSurrealMigrations(CancellationToken ct)
	{
		foreach (var source in PluginMigrationSources)
		{
			foreach (var statement in source.SurrealStatements)
			{
				try
				{
					var response = await ExecuteAsync(statement, ct);
					if (response.HasErrors && source.RequireSuccessfulSurrealMigrations)
						throw new InvalidOperationException(
							$"Plugin SurrealQL migration failed ({source.GetType().FullName}): " +
							string.Join("; ", response.Errors.Select(FormatError)));
				}
				catch (Exception ex) when (!source.RequireSuccessfulSurrealMigrations && ex is not OperationCanceledException)
				{
					logger.LogError(ex, "Plugin SurrealQL migration statement failed (SurrealDB); continuing.");
				}
			}
		}
	}

	private async Task CreateInitialAttributeFlags(CancellationToken ct)
	{
		foreach (var af in AttributeFlagSeed.Flags)
		{
			var parameters = new Dictionary<string, object?>
			{
				["name"] = af.Name,
				["symbol"] = af.Symbol,
				["inheritable"] = af.Inheritable
			};

			await ExecuteAsync(
				$"UPSERT attribute_flag:{SanitizeRecordId(af.Name)} SET name = $name, symbol = $symbol, system = true, inheritable = $inheritable",
				parameters, ct);
		}
	}

	private async Task CreateInitialPowers(CancellationToken ct)
	{
		foreach (var p in PowerSeed.Powers)
		{
			var parameters = new Dictionary<string, object?>
			{
				["name"] = p.Name,
				["alias"] = p.Alias,
				["setPerms"] = p.SetPerms,
				["unsetPerms"] = p.UnsetPerms,
				["typeRestrictions"] = new[] { "ROOM", "PLAYER", "EXIT", "THING" }
			};

			// symbol = '': every entry in PennMUSH hdrs/flag_tab.h power_table has letter '\0'.
			await ExecuteAsync(
				$"UPSERT power:{SanitizeRecordId(p.Name)} SET name = $name, alias = $alias, symbol = '', system = true, disabled = false, setPermissions = $setPerms, unsetPermissions = $unsetPerms, typeRestrictions = $typeRestrictions",
				parameters, ct);
		}
	}

	private async Task CreateInitialAttributeEntries(CancellationToken ct)
	{
		foreach (var e in AttributeEntrySeed.Entries)
		{
			var parameters = new Dictionary<string, object?>
			{
				["name"] = e.Name,
				["defaultFlags"] = e.DefaultFlags
			};

			await ExecuteAsync(
				$"UPSERT attribute_entry:{SanitizeRecordId(e.Name)} SET name = $name, defaultFlags = $defaultFlags, lim = '', enumValues = []",
				parameters, ct);
		}
	}

	/// <summary>
	/// Sanitizes a name for use as a SurrealDB record ID segment.
	/// Wraps names containing special characters in backticks.
	/// </summary>
	private static string SanitizeRecordId(string name)
	{
		// SurrealDB record IDs with special characters need to be wrapped in backticks
		if (name.All(c => char.IsLetterOrDigit(c) || c == '_'))
			return name;
		return $"`{name}`";
	}

	#endregion
}
