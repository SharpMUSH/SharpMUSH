using Core.Arango;
using Core.Arango.Migration;
using Microsoft.Extensions.Logging;

namespace SharpMUSH.Database
{
	public class ArangoDatabase(ILogger<ArangoDatabase> logger, IArangoContext arangodb) : ISharpDatabase
	{
		public async Task Migrate()
		{
			logger.LogInformation("Migrating Database");

			var migrator = new ArangoMigrator(arangodb)
			{
				HistoryCollection = "MigrationHistory"
			};

			// load all migrations from assembly
			migrator.AddMigrations(typeof(ArangoDatabase).Assembly);

			// apply all migrations up to latest
			await migrator.UpgradeAsync("CurrentSharpMUSHWorld");

			logger.LogInformation("Migration Completed.");
		}
	}
}
