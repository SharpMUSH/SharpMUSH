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
			await migrator.UpgradeAsync(handle);

			logger.LogInformation("Migration Completed.");
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Migration Failed. Check details for further information.");
			throw;
		}
	}

	#endregion
}
