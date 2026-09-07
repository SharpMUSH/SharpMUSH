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
			logger, arangoDb, stagingHandle, relations, passwordService,
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
			// PluginCatalog). Each contributing assembly is its own migration stream — see
			// ApplyMigrationsAsync — so plugin migrations neither wait on nor block the engine's.
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
	/// Separates a source name from the migration Id in a history key. The engine's rows carry no prefix at
	/// all, so every key ever written for it keeps meaning exactly what it meant.
	/// </summary>
	private const char SourceSeparator = ':';

	/// <summary>
	/// One contributing assembly's migrations. Ordering is meaningful inside a stream and meaningless
	/// between streams.
	/// </summary>
	/// <param name="Source">The contributing assembly's simple name; also what a failure message names.</param>
	/// <param name="KeyPrefix">What this stream's history keys start with — empty for the engine.</param>
	/// <param name="Migrations">The stream's migrations, ascending by Id.</param>
	private sealed record MigrationStream(string Source, string KeyPrefix, IReadOnlyList<IArangoMigration> Migrations);

	/// <summary>
	/// Applies the migrations in <paramref name="assemblies"/> that this database has not already run,
	/// recording one row per applied migration in <see cref="MigrationHistoryCollection"/>.
	/// </summary>
	/// <remarks>
	/// <para>A migration's identity is (source, Id), where the source is the assembly that contributes it —
	/// the engine is one source, each plugin another. That is Django's <c>(app, name)</c> rather than a
	/// single global sequence, and it is what the engine-plus-plugins shape demands: plugin authors cannot
	/// coordinate Ids with each other or with a release they were written before.</para>
	/// <para>WITHIN a source, order is a guarantee. Migrations run ascending by Id, and one dated before an
	/// Id that source has already applied is REFUSED — Flyway's default for a back-dated version, and for
	/// Flyway's reason: applying it retroactively changes what that source's recorded version means, so two
	/// deployments at the "same" version can hold different schemas. The refusal is loud and names the
	/// applied Id it would have preceded; it is never a silent skip, because a silent skip is the bug this
	/// method replaces.</para>
	/// <para>ACROSS sources there is no order at all. A plugin's <c>20260619_001</c> is untouched by the
	/// engine sitting at <c>20260713_001</c>, and two plugins may both ship an <c>0001</c>.</para>
	/// <para>This deliberately replaces <c>ArangoMigrator.UpgradeAsync</c>, which cannot install a plugin
	/// into an already-migrated world. Core.Arango's implementation reads a single HIGH-WATER MARK across
	/// the whole history — the newest key — and runs only migrations with a greater <c>Id</c>, never asking
	/// whether an individual migration ran. So the Scene plugin's <c>20260619_001</c>, installed into a world
	/// already at <c>20260713_001</c>, was skipped forever: migration reported success, no history row
	/// appeared, and every scene query died with "collection or view not found". Only a database migrated
	/// from empty WITH the plugin present ever worked.</para>
	/// <para>The recorded shape is unchanged — one document per applied migration, as
	/// <c>UpgradeAsync</c> already wrote — so an existing world needs no data fix-up and a rollback to the
	/// stock upgrade path still reads the engine's history correctly.</para>
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

		var engineAssembly = typeof(ArangoDatabase).Assembly;

		// Distinct: two plugins shipped in one assembly each contribute that assembly, and the same type
		// discovered twice is one migration rather than an Id collision. Grouped by name because the name is
		// what the history key carries — two assemblies answering to one name are one stream, and their Ids
		// therefore have to be unique between them.
		var streams = assemblies
			.Distinct()
			.GroupBy(assembly => assembly.GetName().Name ?? assembly.FullName!, StringComparer.Ordinal)
			.Select(group => new MigrationStream(
				group.Key,
				group.Contains(engineAssembly) ? string.Empty : group.Key + SourceSeparator,
				group.SelectMany(DiscoverMigrations).OrderBy(migration => migration.Id).ToList()))
			.ToList();

		foreach (var stream in streams)
		{
			GuardDuplicateIds(stream);
		}

		// An unprefixed history key is the engine's, with no ambiguity: every key written under per-source
		// tracking carries its source's prefix, and the one database shape that could hold an unprefixed
		// PLUGIN key — a world migrated before this tracking existed — is not supported. SharpMUSH is
		// pre-production; such a database is dropped and re-migrated rather than repaired, so no adoption
		// or history-rewriting path exists here to guess an owner and get it wrong.
		foreach (var stream in streams)
		{
			await ApplyStreamAsync(migrator, stream, applied, ct);
		}
	}

	/// <summary>The <see cref="IArangoMigration"/> instances <paramref name="assembly"/> contributes.</summary>
	private IEnumerable<IArangoMigration> DiscoverMigrations(Assembly assembly) =>
		LoadableTypes(assembly)
			.Where(type => typeof(IArangoMigration).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
			.Select(type => (IArangoMigration)Activator.CreateInstance(type, nonPublic: true)!);

	/// <summary>
	/// Refuses a stream in which two migrations claim one Id. Within a source the Id is the history key, so
	/// the second would silently never run and reflection order would decide which schema change goes
	/// missing. The guard is per-source on purpose: an Id collision between two sources is legal, they
	/// record under different keys and never interact.
	/// </summary>
	private static void GuardDuplicateIds(MigrationStream stream)
	{
		var collisions = stream.Migrations
			.GroupBy(migration => migration.Id)
			.Where(group => group.Skip(1).Any())
			.Select(group => $"{group.Key.ToString(CultureInfo.InvariantCulture)} claimed by " +
				string.Join(", ", group.Select(migration => migration.GetType().FullName)))
			.ToList();

		if (collisions.Count > 0)
		{
			throw new InvalidOperationException(
				$"Duplicate Arango migration Ids in {stream.Source}; an Id must be unique within the source " +
				"that ships it (two sources may share an Id — they are independent streams). " +
				string.Join("; ", collisions));
		}
	}

	/// <summary>
	/// Applies whatever <paramref name="stream"/> still owes this database, ascending by Id, refusing any
	/// migration dated before the newest Id that stream has already applied.
	/// </summary>
	private async Task ApplyStreamAsync(
		IArangoMigrator migrator, MigrationStream stream, HashSet<string> applied, CancellationToken ct)
	{
		var appliedIds = applied
			.Select(key => StreamId(stream, key))
			.Where(id => id.HasValue)
			.Select(id => id!.Value)
			.ToList();
		var newestApplied = appliedIds.Count > 0 ? appliedIds.Max() : (long?)null;

		foreach (var migration in stream.Migrations)
		{
			var id = migration.Id.ToString(CultureInfo.InvariantCulture);
			var key = stream.KeyPrefix + id;
			if (applied.Contains(key))
			{
				continue;
			}

			if (newestApplied is { } blocker && migration.Id < blocker)
			{
				throw new InvalidOperationException(
					$"Arango migration {id} ({migration.Name}) from {stream.Source} is dated before " +
					$"{blocker.ToString(CultureInfo.InvariantCulture)}, which {stream.Source} has already " +
					"applied to this database. A source's migrations must be added in increasing Id order: " +
					"running this one now would change what that source's recorded version means and leave " +
					"two deployments at the same version holding different schemas. Re-date it above " +
					$"{blocker.ToString(CultureInfo.InvariantCulture)}. Ids belonging to other sources are " +
					"unrelated and never block it.");
			}

			logger.LogInformation("Applying migration {MigrationId} ({MigrationName}) from {Source}.",
				id, migration.Name, stream.Source);
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
	/// The migration Id <paramref name="key"/> records for <paramref name="stream"/>, or <c>null</c> when the
	/// key belongs to another stream (or to nothing this build knows how to read).
	/// </summary>
	private static long? StreamId(MigrationStream stream, string key)
	{
		if (!key.StartsWith(stream.KeyPrefix, StringComparison.Ordinal))
		{
			return null;
		}

		var id = key[stream.KeyPrefix.Length..];

		// An unprefixed key is the engine's; a key carrying any separator at all is some other source's.
		return id.Contains(SourceSeparator)
			? null
			: long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
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
