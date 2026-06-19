using Core.Arango;
using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Database.ArangoDB;

public partial class ArangoDatabase(
ILogger<ArangoDatabase> logger,
IArangoContext arangoDb,
ArangoHandle handle,
IMediator mediator,
IPasswordService passwordService,
IReadOnlyList<IMigrationSource>? migrationSources = null,
IReadOnlyList<PluginFlag>? pluginFlags = null
) : ISharpDatabase, ISharpDatabaseWithLogging
{
	private const string StartVertex = "startVertex";

	// Phase 2a plugin contributions threaded through from the pre-build PluginCatalog. Empty for staging
	// databases (created from a live DB) and any host that does not load plugins.
	private IReadOnlyList<IMigrationSource> PluginMigrationSources => migrationSources ?? [];
	private IReadOnlyList<PluginFlag> PluginFlags => pluginFlags ?? [];
}
