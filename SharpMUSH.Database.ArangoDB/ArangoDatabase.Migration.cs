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
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace SharpMUSH.Database.ArangoDB;

public partial class ArangoDatabase
{
	#region Migration

	public async Task<IStagingDatabase> CreateStagingAsync(CancellationToken ct = default)
	{
		var stagingId = Guid.NewGuid().ToString("N")[..8];
		var stagingHandle = new ArangoHandle($"{handle}_staging_{stagingId}");

		logger.LogInformation("Creating staging database: {StagingDb}", (string)stagingHandle);

		var staging = new ArangoStagingDatabase(
			logger, arangoDb, stagingHandle, mediator, passwordService,
			liveDatabase: this, originalHandle: handle, stagingId: stagingId);

		await staging.Migrate(ct);

		return staging;
	}

	public async ValueTask WipeDatabaseAsync(CancellationToken ct = default)
	{
		try
		{
			logger.LogWarning("WIPING DATABASE - This is destructive and irreversible!");

			if (await arangoDb.Database.ExistAsync(handle))
			{
				await arangoDb.Database.DropAsync(handle);
				logger.LogInformation("Database dropped successfully.");
			}

			await Migrate(ct);

			logger.LogInformation("Database wiped and re-initialized successfully.");
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to wipe database.");
			throw;
		}
	}

	public async ValueTask Migrate(CancellationToken ct = default)
	{
		try
		{
			logger.LogInformation("Migrating Database");

			// Still an ArangoMigrator: the migrations take IArangoMigrator and use its structure-diffing
			// helpers. Only the "which migrations still need running" decision is ours (see
			// ApplyMigrationsAsync) — UpgradeAsync is not called.
			var migrator = new ArangoMigrator(arangoDb)
			{
				HistoryCollection = MigrationHistoryCollection
			};

			if (!await migrator.Context.Database.ExistAsync(handle))
			{
				await migrator.Context.Database.CreateAsync(handle);
			}

			// Phase 2a: plugins contribute their own Arango migrations (provider-tagged via the
			// PluginCatalog). Their assemblies join the engine's in one upgrade pass, so plugin schema/seed
			// migrations interleave with the engine's by Id.
			var migrationAssemblies = new List<Assembly> { typeof(ArangoDatabase).Assembly };
			migrationAssemblies.AddRange(PluginMigrationSources
				.Select(source => source.ArangoMigrationAssembly)
				.OfType<Assembly>());

			await ApplyMigrationsAsync(migrator, migrationAssemblies, ct);

			// Phase 2a: seed plugin-contributed flags (IFlagSource) alongside the built-in flag set.
			// Idempotent UPSERT keyed on Name so re-migration (or a flag also seeded by a migration) is safe.
			await SeedPluginFlagsAsync(ct);

			// Seed the default FORMAT`* attributes on the Ancestor Player (#4) so a plain player inherits
			// the PennMUSH-style say/pose/semipose/emit render templates. Idempotent.
			await AncestorSeed.SeedAncestorPlayerFormatsAsync(this, ct);

			logger.LogInformation("Migration Completed.");
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Migration Failed. Check details for further information.");
			throw;
		}
	}

	/// <summary>The migrator's history collection — one document per applied migration, keyed by its Id.</summary>
	private const string MigrationHistoryCollection = "MigrationHistory";

	/// <summary>
	/// Applies every migration in <paramref name="assemblies"/> that this database has not already run,
	/// in Id order, recording each in <see cref="MigrationHistoryCollection"/>.
	/// </summary>
	/// <remarks>
	/// <para>This deliberately replaces <c>ArangoMigrator.UpgradeAsync</c>, which cannot install a plugin
	/// into an already-migrated world. Core.Arango's implementation reads a single HIGH-WATER MARK — the
	/// newest key in the history collection — and then runs only migrations whose <c>Id</c> is greater than
	/// it. It never asks whether an individual migration has been applied. So a plugin whose migration Id
	/// predates the engine's newest applied migration (the Scene plugin's <c>20260619_001</c> against an
	/// engine already at <c>20260713_001</c>) is skipped forever: migration is reported as completed, no
	/// history row appears, and the plugin's collections never exist — every scene query then dies with
	/// "collection or view not found". Only a database migrated from empty WITH the plugin present worked.</para>
	/// <para>Applying per-Id instead is a superset of the high-water behaviour and needs no data fix-up:
	/// <c>UpgradeAsync</c> already wrote one row per applied migration, so an existing world's history is
	/// exactly the set of Ids to skip. The trade-off is that a migration deleted from the codebase is no
	/// longer implicitly "already applied" by virtue of an older sibling having run — which is the correct
	/// reading anyway, and every migration here is written to be idempotent regardless.</para>
	/// </remarks>
	private async Task ApplyMigrationsAsync(
		IArangoMigrator migrator, IReadOnlyList<Assembly> assemblies, CancellationToken ct)
	{
		if (!await arangoDb.Collection.ExistAsync(handle, MigrationHistoryCollection))
		{
			await arangoDb.Collection.CreateAsync(handle, new ArangoCollection
			{
				Name = MigrationHistoryCollection,
				Type = ArangoCollectionType.Document
			});
		}

		var applied = (await arangoDb.Query.ExecuteAsync<string>(handle,
				"FOR x IN @@c RETURN x._key",
				new Dictionary<string, object> { { "@c", MigrationHistoryCollection } },
				cancellationToken: ct))
			.ToHashSet(StringComparer.Ordinal);

		var migrations = assemblies
			.SelectMany(LoadableTypes)
			.Where(type => typeof(IArangoMigration).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
			.Select(type => (IArangoMigration)Activator.CreateInstance(type, nonPublic: true)!)
			.OrderBy(migration => migration.Id)
			.ToList();

		foreach (var migration in migrations)
		{
			var key = migration.Id.ToString(CultureInfo.InvariantCulture);
			if (!applied.Add(key))
			{
				continue;
			}

			logger.LogInformation("Applying migration {MigrationId} ({MigrationName}).", key, migration.Name);
			await migration.Up(migrator, handle);

			// Same document shape ArangoMigrator writes, so a rollback to the stock upgrade path still reads
			// this history correctly.
			await arangoDb.Document.CreateAsync(handle, MigrationHistoryCollection, new
			{
				_key = key,
				Name = migration.Name,
				Created = DateTime.UtcNow
			}, cancellationToken: ct);
		}
	}

	/// <summary>
	/// The types of <paramref name="assembly"/>, keeping whatever loaded when some did not. A plugin
	/// assembly is built against contract assemblies the host resolves at runtime, so one unresolvable
	/// dependency would otherwise take the whole migration pass — and with it server startup — down. The
	/// failure is logged loudly rather than swallowed: a migration that silently does not run is exactly
	/// the class of bug this method exists to eliminate.
	/// </summary>
	private IEnumerable<Type> LoadableTypes(Assembly assembly)
	{
		try
		{
			return assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException ex)
		{
			logger.LogError(ex, "Only part of {Assembly} could be inspected for migrations; " +
				"any migration in an unloadable type will NOT be applied.", assembly.FullName);
			return ex.Types.OfType<Type>();
		}
	}

	/// <summary>
	/// Seed flags contributed by plugins (Phase 2a <see cref="IFlagSource"/>) into the object-flag
	/// collection, mirroring the built-in flag shape. Idempotent: UPSERT keyed on the flag Name so it is
	/// safe to run on every migration and never duplicates a flag a plugin migration already created.
	/// </summary>
	private async Task SeedPluginFlagsAsync(CancellationToken ct)
	{
		foreach (var flag in PluginFlags)
		{
			try
			{
				await arangoDb.Query.ExecuteAsync<object>(
					handle,
					"UPSERT { Name: @name } INSERT @doc UPDATE @doc IN @@c",
					bindVars: new Dictionary<string, object>
					{
						{ "@c", DatabaseConstants.ObjectFlags },
						{ "name", flag.Name },
						{
							"doc", new
							{
								Name = flag.Name,
								Symbol = flag.Symbol,
								Aliases = flag.Aliases.ToArray(),
								System = flag.System,
								SetPermissions = flag.SetPermissions.ToArray(),
								UnsetPermissions = flag.UnsetPermissions.ToArray(),
								TypeRestrictions = flag.TypeRestrictions.ToArray()
							}
						}
					},
					cancellationToken: ct);

				logger.LogInformation("Seeded plugin flag '{Flag}'.", flag.Name);
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Failed to seed plugin flag '{Flag}'; continuing.", flag.Name);
			}
		}
	}

	#endregion
}
