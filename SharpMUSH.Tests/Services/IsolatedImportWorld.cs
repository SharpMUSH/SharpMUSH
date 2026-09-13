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
using SharpMUSH.Database.SurrealDB;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Server;
using SurrealDb.Net;
using System.Runtime.ExceptionServices;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// A freshly migrated world of its own, with the engine the PennMUSH converter needs around it, for
/// import tests that must not write into the session's shared world.
/// </summary>
/// <remarks>
/// <para>The converter is not only a database client: attributes go through
/// <see cref="IAttributeService"/> and placement through the Mediator, so a private database handed to
/// the shared host's services would still land those writes in the shared world. This runs the
/// server's own <see cref="Startup.ConfigureServices"/> on a bare collection with the provider pointed
/// at a private world, which gives the converter a whole engine of its own.</para>
/// <para>No host is built, so no hosted service starts: nothing joins the shared host's durable NATS
/// consumers or its Quartz scheduler. The NATS URL is deliberately unreachable, so anything in the
/// converter's graph that still reached for NATS would fail rather than join the shared bus.</para>
/// </remarks>
public sealed class IsolatedImportWorld : IAsyncDisposable
{
	private const string UnreachableNats = "nats://127.0.0.1:1";

	private readonly ServiceProvider _services;
	private readonly string? _lightningPath;
	private readonly ServiceProvider? _surrealServices;

	private IsolatedImportWorld(ServiceProvider services, string? lightningPath, ServiceProvider? surrealServices)
	{
		_services = services;
		_lightningPath = lightningPath;
		_surrealServices = surrealServices;
	}

	public IPennMUSHDatabaseConverter Converter => _services.GetRequiredService<IPennMUSHDatabaseConverter>();

	public PennMUSHDatabaseParser Parser => _services.GetRequiredService<PennMUSHDatabaseParser>();

	public ISharpDatabase Database => _services.GetRequiredService<ISharpDatabase>();

	/// <summary>The world's engine, whose object cache fronts <see cref="Database"/>.</summary>
	public IMediator Mediator => _services.GetRequiredService<IMediator>();

	public static Task<IsolatedImportWorld> CreateAsync()
	{
		var useSurreal = string.Equals(Environment.GetEnvironmentVariable("SHARPMUSH_DATABASE_PROVIDER"),
			"surrealdb", StringComparison.OrdinalIgnoreCase);
		return CreateAsync(useSurreal ? DatabaseProvider.SurrealDB : DatabaseProvider.Lightning);
	}

	internal string? LightningPath => _lightningPath;

	internal static async Task<IsolatedImportWorld> CreateAsync(DatabaseProvider databaseProvider,
		Action<IServiceCollection>? configureServices = null,
		Action<IServiceCollection>? configureSurrealServices = null)
	{
		var useSurreal = databaseProvider == DatabaseProvider.SurrealDB;

		var environment = Substitute.For<IHostEnvironment>();
		environment.EnvironmentName.Returns(Environments.Development);
		environment.ApplicationName.Returns("SharpMUSH.Server");
		environment.ContentRootPath.Returns(AppContext.BaseDirectory);

		var services = new ServiceCollection();
		new Startup(
				colorFile: Path.Join(AppContext.BaseDirectory, "colors.json"),
				natsUrl: UnreachableNats,
				databaseProvider: useSurreal ? DatabaseProvider.SurrealDB : DatabaseProvider.Lightning)
			.ConfigureServices(services, new ConfigurationBuilder().Build(), environment);

		string? lightningPath = null;
		ServiceProvider? surrealServices = null;
		ServiceProvider? provider = null;
		try
		{
			if (useSurreal)
			{
				// Always the embedded in-memory engine, whatever endpoint the shared world is on.
				var surrealCollection = new ServiceCollection();
				surrealCollection.AddSurreal($"Endpoint=mem://;Namespace=sharpmush;Database=import_{Guid.NewGuid():N}")
					.AddInMemoryProvider();
				configureSurrealServices?.Invoke(surrealCollection);
				surrealServices = surrealCollection.BuildServiceProvider();
				var client = surrealServices.GetRequiredService<ISurrealDbClient>();
				await client.Connect();

				services.AddSingleton(sp => new SurrealDatabase(
					sp.GetRequiredService<ILogger<SurrealDatabase>>(), client,
					sp.GetRequiredService<IPasswordService>(), sp.GetRequiredService<IObjectRelationLoader>(),
					sp.GetRequiredService<PluginCatalog>().MigrationSources,
					sp.GetRequiredService<PluginCatalog>().AllFlags));
			}
			else
			{
				lightningPath = Path.Join(Path.GetTempPath(), $"sharpmush-import-isolated-{Guid.NewGuid():N}");
				var path = lightningPath;
				services.AddSingleton(sp => new LightningDatabase(
					sp.GetRequiredService<ILogger<LightningDatabase>>(),
					new LightningStoreOptions { Path = path, MapSize = 1L << 30 },
					sp.GetRequiredService<IPasswordService>(), sp.GetRequiredService<IObjectRelationLoader>(),
					sp.GetRequiredService<PluginCatalog>().MigrationSources,
					sp.GetRequiredService<PluginCatalog>().AllFlags));
			}

			// The same configuration the shared test host runs on. A world being built has nobody connected
			// to it, so nothing here talks to NATS: no notifier, no message bus, and no connection store,
			// whose only implementation is the shared key-value bucket and which ConnectionService runs
			// without.
			var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
			options.CurrentValue.Returns(ReadPennMushConfig.Create(
				Path.Join(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst")));
			services.RemoveAll<IOptionsWrapper<SharpMUSHOptions>>();
			services.AddSingleton(options);
			services.RemoveAll<INotifyService>();
			services.AddSingleton(TestHelpers.CreateNotifyServiceSubstitute());
			services.RemoveAll<IMessageBus>();
			services.AddSingleton(Substitute.For<IMessageBus>());
			services.RemoveAll<IConnectionStateStore>();
			// Nor a queue, and with it no Quartz: resolving Quartz's scheduler factory points Quartz's
			// process-wide log provider at this container's logger factory, which dies with the world and
			// takes every other host's Quartz logging down with it. Removing the factory makes anything that
			// still reaches for Quartz fail here rather than repoint that static.
			services.RemoveAll<ITaskScheduler>();
			services.AddSingleton(Substitute.For<ITaskScheduler>());
			services.RemoveAll<ISchedulerFactory>();
			// As on the shared test host: the advisor is a delayed diagnostic task that outlives a short-lived
			// container and faults once it is disposed.
			services.PostConfigureAll<FusionCacheOptions>(cache => cache.EnableBestPracticesAdvisor = false);

			configureServices?.Invoke(services);
			provider = services.BuildServiceProvider();
			await provider.GetRequiredService<IDatabaseLifecycle>().Migrate();
			// The generated Mediator builds its handler table on first use and, if that throws, never
			// resets: every later Send spins forever. The host's first Send is at startup; this is ours, so
			// a failure to build the table surfaces here as the exception it is.
			await provider.GetRequiredService<IMediator>().Send(new GetObjectNodeQuery(new DBRef(0)));
			return new IsolatedImportWorld(provider, lightningPath, surrealServices);
		}
		catch (Exception initializationFailure)
		{
			var cleanupFailures = await CleanupAsync(provider, surrealServices, lightningPath);
			if (cleanupFailures.Count > 0)
				throw new AggregateException("Import world initialization and cleanup failed.", [initializationFailure, .. cleanupFailures]);
			throw;
		}
	}

	public async ValueTask DisposeAsync()
	{
		var failures = await CleanupAsync(_services, _surrealServices, _lightningPath);
		if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
		if (failures.Count > 1) throw new AggregateException("Import world cleanup failed.", failures);
	}

	private static async ValueTask<List<Exception>> CleanupAsync(ServiceProvider? services,
		ServiceProvider? surrealServices, string? lightningPath)
	{
		var failures = new List<Exception>();
		// Each provider and the owned directory need an attempt even if another resource fails.
		try
		{
			if (services is not null) await services.DisposeAsync();
		}
		catch (Exception failure) { failures.Add(failure); }
		try
		{
			if (surrealServices is not null) await surrealServices.DisposeAsync();
		}
		catch (Exception failure) { failures.Add(failure); }
		try
		{
			if (lightningPath is not null && Directory.Exists(lightningPath)) Directory.Delete(lightningPath, recursive: true);
		}
		catch (Exception failure) { failures.Add(failure); }
		return failures;
	}
}
