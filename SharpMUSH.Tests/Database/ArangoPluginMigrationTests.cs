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
using System.Reflection.Emit;
using SharpArangoDatabase = SharpMUSH.Database.ArangoDB.ArangoDatabase;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// A migration's identity is (source, Id): the engine is one source, each plugin's migration assembly
/// another. Ordering is a guarantee inside a source and means nothing between sources. These tests pin both
/// halves — a back-dated Id is refused within a source, and the identical Id relationship across sources is
/// applied without complaint.
///
/// <para>The cross-source half is F-04. Core.Arango's <c>ArangoMigrator.UpgradeAsync</c> tracks a single
/// high-water mark (the newest key in <c>MigrationHistory</c>) over the whole history and runs only
/// migrations with a greater Id, so a plugin migration whose Id predates the engine's newest applied one was
/// skipped forever — migration logged "Completed", no history row appeared, and every query against the
/// plugin's collections failed with ArangoDB 1203 "collection or view not found".</para>
/// </summary>
[NotInParallel]
public class ArangoPluginMigrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactory { get; init; }

	/// <summary>Collection the fake plugin's migration creates — stands in for the Scene plugin's schema.</summary>
	private const string MarkerCollection = "plugin_migration_marker";

	private const string HistoryCollection = "MigrationHistory";

	[Test]
	public async Task PluginInstalledIntoAnAlreadyMigratedWorld_StillGetsItsSchema()
	{
		// Only the ArangoDB provider registers an IArangoContext; the Memgraph/SurrealDB runs of this suite
		// apply their plugin statements unconditionally on every boot and never had this defect.
		if (WebAppFactory.Services.GetService<IArangoContext>() is not { } context)
		{
			return;
		}

		var handle = NewHandle();

		try
		{
			// Boot 1: the world is migrated with no plugins present.
			await Database(context, handle).Migrate();

			await Assert.That(await context.Collection.ExistAsync(handle, MarkerCollection)).IsFalse();

			// Boot 2: same world, plugin now installed.
			await Database(context, handle, new MarkerMigrationSource()).Migrate();

			await Assert.That(await context.Collection.ExistAsync(handle, MarkerCollection))
				.IsTrue()
				.Because("a plugin migration older than the newest applied engine migration must still run");

			await Assert.That(await HistoryKeys(context, handle)).Contains(MarkerMigrationKey);

			// Boot 3: nothing left to do — the migration must not re-run now that it is recorded.
			await Database(context, handle, new MarkerMigrationSource()).Migrate();

			await Assert.That((await HistoryKeys(context, handle)).Count(key => key == MarkerMigrationKey))
				.IsEqualTo(1);
		}
		finally
		{
			await DropAsync(context, handle);
		}
	}

	/// <summary>
	/// The same Id relationship as the back-dated refusal below, and the opposite verdict: the plugin's
	/// migration is dated before the newest Id the ENGINE has applied, and runs anyway. Across sources there
	/// is no order to violate, which is the whole reason the per-source model fixes F-04.
	/// </summary>
	[Test]
	public async Task PluginMigrationDatedBeforeTheEnginesNewest_RunsAnyway()
	{
		if (WebAppFactory.Services.GetService<IArangoContext>() is not { } context)
		{
			return;
		}

		var handle = NewHandle();

		try
		{
			await Database(context, handle).Migrate();

			// The mark Core.Arango would have compared against, and the reason it never reached the plugin.
			var newestEngineId = (await context.Query.ExecuteAsync<long>(handle,
				$"FOR x IN {HistoryCollection} FILTER !CONTAINS(x._key, ':') " +
				"SORT TO_NUMBER(x._key) DESC LIMIT 1 RETURN TO_NUMBER(x._key)",
				new Dictionary<string, object>())).Single();

			await Assert.That(newestEngineId)
				.IsGreaterThan(MarkerMigration.MigrationId)
				.Because("the test is only meaningful while the plugin's Id is the back-dated one");

			await Database(context, handle, new MarkerMigrationSource()).Migrate();

			await Assert.That(await context.Collection.ExistAsync(handle, MarkerCollection)).IsTrue();
			await Assert.That(await HistoryKeys(context, handle)).Contains(MarkerMigrationKey);
		}
		finally
		{
			await DropAsync(context, handle);
		}
	}

	/// <summary>
	/// Within one source, a migration dated before an Id that source has already applied is refused — Flyway's
	/// default, and for Flyway's reason: applying it retroactively changes what the recorded version means, so
	/// two deployments claiming the same version can hold different schemas. Refusing loudly is the point;
	/// silently skipping it is the F-04 bug.
	///
	/// <para>The engine's migrations cannot be back-dated from a test — they are compiled into the engine
	/// assembly — so the database is instead handed an engine history row from the future, which back-dates
	/// every engine migration relative to it. That is the same comparison the guard makes.</para>
	/// </summary>
	[Test]
	public async Task EngineMigrationDatedBeforeOneAlreadyApplied_IsRefused()
	{
		if (WebAppFactory.Services.GetService<IArangoContext>() is not { } context)
		{
			return;
		}

		var handle = NewHandle();

		try
		{
			await SeedHistoryAsync(context, handle, FutureId.ToString(CultureInfo.InvariantCulture), "from_the_future");

			var failure = await Assert.That(async () => await Database(context, handle).Migrate())
				.Throws<InvalidOperationException>();

			await Assert.That(failure!.Message).Contains("20240304001");
			await Assert.That(failure.Message).Contains("create_database");
			await Assert.That(failure.Message).Contains("29990101001");
			await Assert.That(failure.Message).Contains(typeof(SharpArangoDatabase).Assembly.GetName().Name!);

			await Assert.That(await context.Collection.ExistAsync(handle, "node_objects"))
				.IsFalse()
				.Because("a refused migration pass must not half-apply the schema");
		}
		finally
		{
			await DropAsync(context, handle);
		}
	}

	/// <summary>
	/// Two plugins cannot coordinate Ids with each other, so an Id collision between them is legal — Django's
	/// <c>(app, name)</c> identity, where two apps may both ship an <c>0001</c>. Each records under its own
	/// key and both migrations run.
	/// </summary>
	[Test]
	public async Task TwoPluginsMayShipOneMigrationId()
	{
		if (WebAppFactory.Services.GetService<IArangoContext>() is not { } context)
		{
			return;
		}

		var handle = NewHandle();

		try
		{
			await Database(context, handle,
					new EmittedMigrationSource("SharedIdPluginA", typeof(SharedIdEmittedMigration)),
					new EmittedMigrationSource("SharedIdPluginB", typeof(SharedIdEmittedMigration)))
				.Migrate();

			await Assert.That(await context.Collection.ExistAsync(handle, "emitted_SharedIdPluginA")).IsTrue();
			await Assert.That(await context.Collection.ExistAsync(handle, "emitted_SharedIdPluginB")).IsTrue();

			var keys = await HistoryKeys(context, handle);
			await Assert.That(keys).Contains("SharedIdPluginA:20200202001");
			await Assert.That(keys).Contains("SharedIdPluginB:20200202001");
		}
		finally
		{
			await DropAsync(context, handle);
		}
	}

	/// <summary>
	/// A world migrated from empty WITH a plugin present, before sources were tracked, recorded the plugin's
	/// migrations under a bare Id — indistinguishable from an engine row. Those rows are adopted into the
	/// plugin's stream rather than re-applied: the row keeps the Name the older build wrote, which the
	/// migration would have overwritten with its own had it run again.
	/// </summary>
	[Test]
	public async Task PluginHistoryWrittenBeforeSourcesWereTracked_IsAdopted()
	{
		if (WebAppFactory.Services.GetService<IArangoContext>() is not { } context)
		{
			return;
		}

		var handle = NewHandle();

		try
		{
			await SeedHistoryAsync(context, handle,
				MarkerMigration.MigrationId.ToString(CultureInfo.InvariantCulture), "recorded_by_an_older_build");

			await Database(context, handle, new MarkerMigrationSource()).Migrate();

			var keys = await HistoryKeys(context, handle);
			await Assert.That(keys).Contains(MarkerMigrationKey);
			await Assert.That(keys).DoesNotContain(MarkerMigration.MigrationId.ToString(CultureInfo.InvariantCulture));

			var name = (await context.Query.ExecuteAsync<string>(handle,
				$"FOR x IN {HistoryCollection} FILTER x._key == @key RETURN x.Name",
				new Dictionary<string, object> { { "key", MarkerMigrationKey } })).Single();
			await Assert.That(name)
				.IsEqualTo("recorded_by_an_older_build")
				.Because("the row was re-keyed, not written afresh by a re-run of the migration");
		}
		finally
		{
			await DropAsync(context, handle);
		}
	}

	/// <summary>
	/// The reason adoption is not optional. An older build wrote a plugin migration dated after every engine
	/// migration under a bare Id, so this build would read it as the engine's newest applied Id and refuse the
	/// engine's own migrations — see <see cref="EngineMigrationDatedBeforeOneAlreadyApplied_IsRefused"/>,
	/// which is this exact database without the plugin installed.
	/// </summary>
	[Test]
	public async Task EngineIsNotBlockedByAPluginRowWrittenBeforeSourcesWereTracked()
	{
		if (WebAppFactory.Services.GetService<IArangoContext>() is not { } context)
		{
			return;
		}

		var handle = NewHandle();

		try
		{
			await SeedHistoryAsync(context, handle, FutureId.ToString(CultureInfo.InvariantCulture), "from_the_future");

			await Database(context, handle, new EmittedMigrationSource("FutureIdPlugin", typeof(FutureIdEmittedMigration)))
				.Migrate();

			var keys = await HistoryKeys(context, handle);
			await Assert.That(keys).Contains($"FutureIdPlugin:{FutureId.ToString(CultureInfo.InvariantCulture)}");
			await Assert.That(keys).DoesNotContain(FutureId.ToString(CultureInfo.InvariantCulture));
			await Assert.That(keys)
				.Contains("20240304001")
				.Because("the engine's own stream is empty and must migrate from scratch");
		}
		finally
		{
			await DropAsync(context, handle);
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

		var handle = NewHandle();

		try
		{
			await Database(context, handle, new MarkerMigrationSource(), new MarkerMigrationSource()).Migrate();

			await Assert.That((await HistoryKeys(context, handle)).Count(key => key == MarkerMigrationKey))
				.IsEqualTo(1);
		}
		finally
		{
			await DropAsync(context, handle);
		}
	}

	/// <summary>An Id no engine migration will ever reach, standing in for a back-dating comparison.</summary>
	private const long FutureId = 29990101_001;

	/// <summary>The history key <see cref="MarkerMigration"/> records under — this assembly is its source.</summary>
	private static string MarkerMigrationKey =>
		$"{typeof(MarkerMigration).Assembly.GetName().Name}:" +
		MarkerMigration.MigrationId.ToString(CultureInfo.InvariantCulture);

	private static ArangoHandle NewHandle() => new($"plugin_migration_{Guid.NewGuid():N}"[..24]);

	private SharpArangoDatabase Database(IArangoContext context, ArangoHandle handle,
		params IMigrationSource[] sources) =>
		new(NullLogger<SharpArangoDatabase>.Instance, context, handle,
			WebAppFactory.Services.GetRequiredService<IMediator>(),
			WebAppFactory.Services.GetRequiredService<IPasswordService>(),
			sources);

	private static ValueTask<ArangoList<string>> HistoryKeys(IArangoContext context, ArangoHandle handle) =>
		context.Query.ExecuteAsync<string>(handle, $"FOR x IN {HistoryCollection} RETURN x._key",
			new Dictionary<string, object>());

	/// <summary>
	/// Puts a history row into an otherwise empty world, the way a build that did not track sources would
	/// have written it: bare Id key, no source anywhere on the document.
	/// </summary>
	private static async Task SeedHistoryAsync(IArangoContext context, ArangoHandle handle, string key, string name)
	{
		await context.Database.CreateAsync(handle);
		await context.Collection.CreateAsync(handle, new ArangoCollection
		{
			Name = HistoryCollection,
			Type = ArangoCollectionType.Document
		});
		await context.Document.CreateAsync(handle, HistoryCollection,
			new { _key = key, Name = name, Created = DateTime.UtcNow });
	}

	private static async Task DropAsync(IArangoContext context, ArangoHandle handle)
	{
		if (await context.Database.ExistAsync(handle))
		{
			await context.Database.DropAsync(handle);
		}
	}

	/// <summary>A plugin that contributes exactly one Arango migration, this assembly's <see cref="MarkerMigration"/>.</summary>
	private sealed class MarkerMigrationSource : IMigrationSource
	{
		public Assembly? ArangoMigrationAssembly => typeof(MarkerMigration).Assembly;
	}

	/// <summary>
	/// A plugin whose migration lives in an assembly of its own, emitted at run time. Distinct assemblies are
	/// what distinct sources are made of, and two of them are needed to say anything about Ids colliding
	/// between plugins; emitting an empty subclass of a base that carries the behaviour keeps that to a name.
	/// </summary>
	private sealed class EmittedMigrationSource : IMigrationSource
	{
		public EmittedMigrationSource(string assemblyName, Type migrationBase)
		{
			var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName), AssemblyBuilderAccess.Run);
			assembly.DefineDynamicModule(assemblyName)
				.DefineType($"{assemblyName}.Migration", TypeAttributes.Public, migrationBase)
				.CreateType();

			ArangoMigrationAssembly = assembly;
		}

		public Assembly? ArangoMigrationAssembly { get; }
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

/// <summary>
/// Behaviour for the migrations emitted into run-time assemblies: create one collection named after the
/// assembly that ships it, so which source ran is observable. Abstract, so the migration discovery in this
/// assembly ignores it.
/// </summary>
public abstract class EmittedMigration : IArangoMigration
{
	public abstract long Id { get; }

	public string Name => GetType().Assembly.GetName().Name!;

	public async Task Up(IArangoMigrator migrator, ArangoHandle handle)
	{
		var collection = $"emitted_{Name}";
		if (!await migrator.Context.Collection.ExistAsync(handle, collection))
		{
			await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
			{
				Name = collection,
				Type = ArangoCollectionType.Document
			});
		}
	}

	public Task Down(IArangoMigrator migrator, ArangoHandle handle) => Task.CompletedTask;
}

/// <summary>An Id two emitted plugins both claim.</summary>
public abstract class SharedIdEmittedMigration : EmittedMigration
{
	public override long Id => 20200202_001;
}

/// <summary>An Id later than every engine migration, for the history a pre-source build would have left.</summary>
public abstract class FutureIdEmittedMigration : EmittedMigration
{
	public override long Id => 29990101_001;
}
