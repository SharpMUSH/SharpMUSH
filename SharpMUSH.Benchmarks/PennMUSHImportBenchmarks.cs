using BenchmarkDotNet.Jobs;
using Mediator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Quartz;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Server;
using SharpMUSH.Tests.Services;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Benchmarks;

/// <summary>
/// Import throughput: a 10 MB PennMUSH database parsed and converted into a fresh Lightning world.
/// </summary>
/// <remarks>
/// <para>This is the number the test suite's 10 MB wall-clock budget used to guard. A shared CI runner's
/// clock cannot tell a slow runner from a regression, so the budget no longer runs there; the nightly
/// tracks the trend here instead.</para>
/// <para>The database is generated once, from a fixed seed, so every run imports the same objects. Each
/// iteration imports into a world of its own, built outside the measured time the way the import tests
/// build <c>IsolatedImportWorld</c>: the server's own service registrations on a bare collection, with
/// no host, no NATS and no scheduler, so nothing but the import is on the clock.</para>
/// </remarks>
[Config(typeof(ImportConfig))]
public class PennMUSHImportBenchmarks
{
	private const int DatabaseBytes = 10 * 1024 * 1024;
	private const int Seed = 1230;

	private string? _databaseFile;
	private ServiceProvider? _world;
	private string? _lightningPath;

	[GlobalSetup]
	public async Task Setup() =>
		_databaseFile = await PennMUSHDatabaseGenerator.GenerateLargeDatabaseFileAsync(DatabaseBytes, Seed);

	[GlobalCleanup]
	public void Cleanup()
	{
		if (_databaseFile is not null && File.Exists(_databaseFile))
			File.Delete(_databaseFile);
	}

	[IterationSetup]
	public void CreateWorld()
	{
		_lightningPath = LightningBaseBenchmark.CreateDataDirectory();
		_world = BuildWorld(_lightningPath);
		_world.GetRequiredService<IDatabaseLifecycle>().Migrate().GetAwaiter().GetResult();
		// The generated Mediator builds its handler table on first use; do that here, off the clock.
		_world.GetRequiredService<IMediator>().Send(new GetObjectNodeQuery(new DBRef(0))).AsTask().GetAwaiter().GetResult();
	}

	[IterationCleanup]
	public void DisposeWorld()
	{
		_world?.DisposeAsync().AsTask().GetAwaiter().GetResult();
		_world = null;

		if (_lightningPath is not null && Directory.Exists(_lightningPath))
			Directory.Delete(_lightningPath, recursive: true);
	}

	[Benchmark]
	public async Task<int> Import10MB()
	{
		var database = await _world!.GetRequiredService<PennMUSHDatabaseParser>().ParseFileAsync(_databaseFile!);
		var result = await _world!.GetRequiredService<IPennMUSHDatabaseConverter>().ConvertDatabaseAsync(database);

		// A failed import is fast; it must not be recorded as an improvement.
		if (!result.IsSuccessful || result.TotalObjects == 0)
			throw new InvalidOperationException(
				$"Import failed: {result.TotalObjects} objects, errors: {string.Join("; ", result.Errors)}");

		return result.TotalObjects;
	}

	private static ServiceProvider BuildWorld(string lightningPath)
	{
		var environment = Substitute.For<IHostEnvironment>();
		environment.EnvironmentName.Returns(Environments.Production);
		environment.ApplicationName.Returns("SharpMUSH.Server");
		environment.ContentRootPath.Returns(AppContext.BaseDirectory);

		var services = new ServiceCollection();
		new Startup(
				colorFile: Path.Join(AppContext.BaseDirectory, "colors.json"),
				// Unreachable on purpose: anything in the import's graph that reached for NATS should fail,
				// not quietly add network time to the measurement.
				natsUrl: "nats://127.0.0.1:1")
			.ConfigureServices(services, new ConfigurationBuilder().Build(), environment);

		services.AddSingleton(sp => new LightningDatabase(
			sp.GetRequiredService<ILogger<LightningDatabase>>(),
			new LightningStoreOptions { Path = lightningPath, MapSize = 1L << 30 },
			sp.GetRequiredService<IPasswordService>(), sp.GetRequiredService<IObjectRelationLoader>(),
			sp.GetRequiredService<PluginCatalog>().MigrationSources,
			sp.GetRequiredService<PluginCatalog>().AllFlags));

		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(ReadPennMushConfig.Create(Path.Join(AppContext.BaseDirectory, "mushcnf.dst")));
		services.RemoveAll<IOptionsWrapper<SharpMUSHOptions>>();
		services.AddSingleton(options);
		services.RemoveAll<INotifyService>();
		services.AddSingleton(Substitute.For<INotifyService>());
		services.RemoveAll<IMessageBus>();
		services.AddSingleton(Substitute.For<IMessageBus>());
		services.RemoveAll<IConnectionStateStore>();
		services.RemoveAll<ITaskScheduler>();
		services.AddSingleton(Substitute.For<ITaskScheduler>());
		services.RemoveAll<ISchedulerFactory>();
		// A delayed diagnostic task that faults once a short-lived container is disposed.
		services.PostConfigureAll<FusionCacheOptions>(cache => cache.EnableBestPracticesAdvisor = false);

		return services.BuildServiceProvider();
	}

	/// <summary>
	/// One import per iteration, each into a world built for it. An import takes seconds, so the nightly
	/// takes ten of them rather than letting the default job run up to a hundred.
	/// </summary>
	private sealed class ImportConfig() : AdaptiveBenchmarkConfig(job =>
	{
		var single = job.WithInvocationCount(1).WithUnrollFactor(1);
		return IsCi() ? single : single.WithWarmupCount(1).WithIterationCount(10);
	});
}
