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

			// Re-create and re-migrate
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

			var migrator = new ArangoMigrator(arangoDb)
			{
				HistoryCollection = "MigrationHistory"
			};

			if (!await migrator.Context.Database.ExistAsync(handle))
			{
				await migrator.Context.Database.CreateAsync(handle);
			}

			migrator.AddMigrations(typeof(ArangoDatabase).Assembly);

			// Phase 2a: let plugins contribute their own Arango migrations (provider-tagged via the
			// PluginCatalog). Each source's assembly is added to the same migrator so plugin schema/seed
			// migrations run in the same upgrade pass as the engine's.
			foreach (var source in PluginMigrationSources)
			{
				var pluginAssembly = source.ArangoMigrationAssembly;
				if (pluginAssembly is not null)
				{
					migrator.AddMigrations(pluginAssembly);
				}
			}

			await migrator.UpgradeAsync(handle);

			// Phase 2a: seed plugin-contributed flags (IFlagSource) alongside the built-in flag set.
			// Idempotent UPSERT keyed on Name so re-migration (or a flag also seeded by a migration) is safe.
			await SeedPluginFlagsAsync(ct);

			logger.LogInformation("Migration Completed.");
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Migration Failed. Check details for further information.");
			throw;
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
