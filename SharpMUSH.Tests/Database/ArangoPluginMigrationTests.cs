using Core.Arango;
using Core.Arango.Migration;
using Core.Arango.Protocol;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services.Interfaces;
using System.Globalization;
using System.Reflection;
using SharpArangoDatabase = SharpMUSH.Database.ArangoDB.ArangoDatabase;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// Installing a plugin into a world that was already migrated WITHOUT it must bring the plugin's schema up
/// on the next boot. It did not: Core.Arango's <c>ArangoMigrator.UpgradeAsync</c> tracks a single
/// high-water mark (the newest key in <c>MigrationHistory</c>) and runs only migrations with a greater Id,
/// so a plugin migration whose Id predates the engine's newest applied one was skipped forever — migration
/// logged "Completed", no history row appeared, and every query against the plugin's collections failed
/// with ArangoDB 1203 "collection or view not found".
///
/// <para>The reproduction is the Id relationship, not the plugin: <see cref="MarkerMigration"/> is dated
/// well before every engine migration, so a high-water check can never reach it.</para>
/// </summary>
[NotInParallel]
public class ArangoPluginMigrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactory { get; init; }

	/// <summary>Collection the fake plugin's migration creates — stands in for the Scene plugin's schema.</summary>
	private const string MarkerCollection = "plugin_migration_marker";

	[Test]
	public async Task PluginInstalledIntoAnAlreadyMigratedWorld_StillGetsItsSchema()
	{
		// Only the ArangoDB provider registers an IArangoContext; the Memgraph/SurrealDB runs of this suite
		// apply their plugin statements unconditionally on every boot and never had this defect.
		if (WebAppFactory.Services.GetService<IArangoContext>() is not { } context)
		{
			return;
		}

		var mediator = WebAppFactory.Services.GetRequiredService<IMediator>();
		var password = WebAppFactory.Services.GetRequiredService<IPasswordService>();
		var handle = new ArangoHandle($"plugin_migration_{Guid.NewGuid():N}"[..24]);

		try
		{
			// Boot 1: the world is migrated with no plugins present.
			await new SharpArangoDatabase(NullLogger<SharpArangoDatabase>.Instance, context, handle, mediator, password)
				.Migrate();

			await Assert.That(await context.Collection.ExistAsync(handle, MarkerCollection)).IsFalse();

			// Boot 2: same world, plugin now installed.
			await new SharpArangoDatabase(NullLogger<SharpArangoDatabase>.Instance, context, handle, mediator, password,
					[new MarkerMigrationSource()])
				.Migrate();

			await Assert.That(await context.Collection.ExistAsync(handle, MarkerCollection))
				.IsTrue()
				.Because("a plugin migration older than the newest applied engine migration must still run");

			var history = await context.Query.ExecuteAsync<string>(handle, "FOR x IN MigrationHistory RETURN x._key",
				new Dictionary<string, object>());
			await Assert.That(history).Contains(MarkerMigration.MigrationId.ToString(CultureInfo.InvariantCulture));

			// Boot 3: nothing left to do — the migration must not re-run now that it is recorded.
			await new SharpArangoDatabase(NullLogger<SharpArangoDatabase>.Instance, context, handle, mediator, password,
					[new MarkerMigrationSource()])
				.Migrate();

			var rerunHistory = await context.Query.ExecuteAsync<string>(handle,
				"FOR x IN MigrationHistory FILTER x._key == @key RETURN x._key",
				new Dictionary<string, object> { { "key", MarkerMigration.MigrationId.ToString(CultureInfo.InvariantCulture) } });
			await Assert.That(rerunHistory.Count).IsEqualTo(1);
		}
		finally
		{
			if (await context.Database.ExistAsync(handle))
			{
				await context.Database.DropAsync(handle);
			}
		}
	}

	/// <summary>
	/// Two plugins shipped in one assembly each contribute that same assembly, so every migration in it is
	/// discovered twice. That is one migration seen twice, not two migrations claiming one Id, and the
	/// duplicate-Id guard must not refuse the boot over it.
	/// </summary>
	[Test]
	public async Task PluginsSharingOneAssembly_DoNotTripTheDuplicateIdGuard()
	{
		if (WebAppFactory.Services.GetService<IArangoContext>() is not { } context)
		{
			return;
		}

		var mediator = WebAppFactory.Services.GetRequiredService<IMediator>();
		var password = WebAppFactory.Services.GetRequiredService<IPasswordService>();
		var handle = new ArangoHandle($"plugin_migration_{Guid.NewGuid():N}"[..24]);

		try
		{
			await new SharpArangoDatabase(NullLogger<SharpArangoDatabase>.Instance, context, handle, mediator, password,
					[new MarkerMigrationSource(), new MarkerMigrationSource()])
				.Migrate();

			var history = await context.Query.ExecuteAsync<string>(handle,
				"FOR x IN MigrationHistory FILTER x._key == @key RETURN x._key",
				new Dictionary<string, object>
					{ { "key", MarkerMigration.MigrationId.ToString(CultureInfo.InvariantCulture) } });
			await Assert.That(history.Count).IsEqualTo(1);
		}
		finally
		{
			if (await context.Database.ExistAsync(handle))
			{
				await context.Database.DropAsync(handle);
			}
		}
	}

	/// <summary>A plugin that contributes exactly one Arango migration, this assembly's <see cref="MarkerMigration"/>.</summary>
	private sealed class MarkerMigrationSource : IMigrationSource
	{
		public Assembly? ArangoMigrationAssembly => typeof(MarkerMigration).Assembly;
	}
}

/// <summary>
/// The fake plugin's schema migration. Public and top-level because the migrator instantiates discovered
/// <see cref="IArangoMigration"/> types by reflection. Its Id is deliberately older than every engine
/// migration — that is the condition the old high-water upgrade could not handle.
/// </summary>
public class MarkerMigration : IArangoMigration
{
	public const long MigrationId = 20200101_001;

	public long Id => MigrationId;

	public string Name => "plugin_migration_marker";

	public async Task Up(IArangoMigrator migrator, ArangoHandle handle)
	{
		if (!await migrator.Context.Collection.ExistAsync(handle, "plugin_migration_marker"))
		{
			await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
			{
				Name = "plugin_migration_marker",
				Type = ArangoCollectionType.Document
			});
		}
	}

	public Task Down(IArangoMigrator migrator, ArangoHandle handle) => Task.CompletedTask;
}
