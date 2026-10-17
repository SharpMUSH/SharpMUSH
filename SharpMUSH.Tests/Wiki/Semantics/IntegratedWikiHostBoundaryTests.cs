using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quartz;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Wiki.Semantics;

// Host dependencies live for this class only: provider-only admission tests need no containers.
public class IntegratedWikiHostBoundaryTests
{
	[ClassDataSource<NatsTestServer>(Shared = SharedType.PerTestSession)]
	public required NatsTestServer NatsTestServer { get; init; }
	[ClassDataSource<MySqlTestServer>(Shared = SharedType.PerTestSession)]
	public required MySqlTestServer MySqlTestServer { get; init; }

	private static string ScratchRoot => Path.Combine(Directory.GetCurrentDirectory(), "work", "semantic-wiki-host-boundary");
	private static string ConfigFile => Path.Join(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst");
	private SemanticWikiPrototypeHost NewHost(string? config = null, Action<IServiceCollection>? configure = null)
	{
		Environment.SetEnvironmentVariable("NATS_URL", $"nats://localhost:{NatsTestServer.Instance.GetMappedPublicPort(4222)}");
		return new SemanticWikiPrototypeHost(ScratchRoot, MySqlTestServer.Instance.GetConnectionString(), config ?? ConfigFile, null, "mysql", configure);
	}

	private static async Task AssertForwarders(IServiceProvider services)
	{
		var provider = services.GetRequiredService<LightningDatabase>();
		foreach (var type in new[] { typeof(ISharpDatabase), typeof(IWikiStore), typeof(IDatabaseLifecycle),
			typeof(IPackageRegistryService), typeof(ILightningStorageAccessor) })
			await Assert.That(ReferenceEquals(provider, services.GetRequiredService(type))).IsTrue();
	}

	[Test]
	public async Task ActualHostRetainsIssuedWorldAcrossFullCloseAndReopen()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var first = host.Services.GetRequiredService<LightningDatabase>();
		await AssertForwarders(host.Services);
		var wiki = host.Services.GetRequiredService<IWikiService>();
		var page = (await wiki.CreateAsync("Retained fixture page", "Fixture legacy body.", "#1")).Expect<WikiPage>();
		await Assert.That((await wiki.GetByIdAsync(page.Id)).Expect<WikiPage>().MarkdownSource).IsEqualTo("Fixture legacy body.");
		await Assert.That(first.Store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("prototype.semantic.fixture.identity/v1"),
			out var bytes) ? bytes : [])).IsEquivalentTo(host.FixtureId.ToByteArray(bigEndian: true));

		// This real package consumer reads the same persisted wiki and registry through normal DI.
		var plan = await host.Services.GetRequiredService<IPackageWikiService>().PlanAsync("fixture-content", "1.0.0",
			[new PackageWikiPageSpec(page.Slug, WikiNamespace.Main, page.Title, [], null, page.MarkdownSource, PackageWikiRequires.None)], []);
		await Assert.That(plan.Single().Action).IsEqualTo(PackageWikiAction.Adopt);
		_ = host.Services.GetRequiredService<IPackageInstallService>();
		await AssertCommand(host.Services);

		await host.CloseAsync();
		await Assert.That(() => first.Store.Read(_ => true)).Throws<ObjectDisposedException>();
		await Assert.That(() => { _ = host.Services; }).Throws<InvalidOperationException>();
		await Assert.That(IntegratedWikiBoundaryTests.OnDiskNames(host.WorldPath)
			.Any(name => name.Contains("semantic", StringComparison.OrdinalIgnoreCase))).IsFalse();
		await host.OpenAsync();
		var second = host.Services.GetRequiredService<LightningDatabase>();
		await Assert.That(ReferenceEquals(first, second)).IsFalse();
		await AssertForwarders(host.Services);
		await Assert.That((await host.Services.GetRequiredService<IWikiService>().GetByIdAsync(page.Id))
			.Expect<WikiPage>().MarkdownSource).IsEqualTo("Fixture legacy body.");
	}

	private static async Task AssertCommand(IServiceProvider services)
	{
		var scheduler = await services.GetRequiredService<ISchedulerFactory>().GetScheduler();
		if (!scheduler.IsStarted) await scheduler.Start();
		var player = (await services.GetRequiredService<ISharpDatabase>().GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();
		using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		var result = await services.GetRequiredService<IPortalCommandService>().RunAsync("fixture-account", player, null,
			new PortalCommandRequest("think retained-command", "add(20,22)"), deadline.Token);
		await Assert.That(result.Expect<PortalCommandResponse>().Result).IsEqualTo("42");
		await Assert.That(result.Expect<PortalCommandResponse>().Output.Any(line => line.Contains("retained-command", StringComparison.Ordinal))).IsTrue();
	}

	[Test]
	public async Task OrdinaryActualHostRetainsDefaultProviderAndNoSemanticTables()
	{
		Environment.SetEnvironmentVariable("NATS_URL", $"nats://localhost:{NatsTestServer.Instance.GetMappedPublicPort(4222)}");
		var owned = Path.Combine(ScratchRoot, "ordinary-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(owned);
		var world = Path.Combine(owned, "world");
		var closureProven = false;
		try
		{
			LightningDatabase provider;
			await using (var server = new ServerTestWebApplicationBuilderFactory<SharpMUSH.Server.Program>(
				MySqlTestServer.Instance.GetConnectionString(), ConfigFile, null, worldPath: world))
			{
				await AssertForwarders(server.Services);
				provider = server.Services.GetRequiredService<LightningDatabase>();
				await Assert.That(provider.Store.Read(tx => tx.TryGet(Tables.Meta,
					Keys.Str("prototype.semantic.fixture.identity/v1"), out _))).IsFalse();
			}
			await Assert.That(() => provider.Store.Read(_ => true)).Throws<ObjectDisposedException>();
			closureProven = true;
			await Assert.That(IntegratedWikiBoundaryTests.OnDiskNames(world)
				.Any(name => name.Contains("semantic", StringComparison.OrdinalIgnoreCase))).IsFalse();
		}
		finally { if (closureProven) Directory.Delete(owned, true); }
	}

	[Test]
	public async Task ConcurrentOpenAndTerminalOwnerUseAreRejected()
	{
		var host = NewHost();
		var opening = host.OpenAsync();
		try
		{
			await Assert.That(() => host.OpenAsync()).Throws<InvalidOperationException>();
			await opening;
			await Assert.That(() => host.OpenAsync()).Throws<InvalidOperationException>();
		}
		finally
		{
			try { await opening; }
			finally { await host.DisposeAsync(); }
		}
		await Assert.That(() => host.OpenAsync()).Throws<ObjectDisposedException>();
		await Assert.That(() => { _ = host.Services; }).Throws<InvalidOperationException>();
		await Assert.That(Directory.Exists(host.WorldPath)).IsFalse();
	}

	[Test]
	public async Task PendingImportArtifactsRefuseStartupBeforeEngineLeaseOrProviderWrites()
	{
		await using var host = NewHost();
		var imports = host.WorldPath + ".imports";
		Directory.CreateDirectory(imports);
		var pending = Path.Combine(imports, "pending.json");
		File.WriteAllText(pending, "unowned pending intent");
		try
		{
			await Assert.That(() => host.OpenAsync()).Throws<InvalidOperationException>();
			await Assert.That(File.ReadAllText(pending)).IsEqualTo("unowned pending intent");
			await Assert.That(File.Exists(host.WorldPath + ".engine.lock")).IsFalse();
			await Assert.That(File.Exists(Path.Combine(host.WorldPath, "data.mdb"))).IsFalse();
			await host.DisposeAsync();
			await Assert.That(File.Exists(pending)).IsTrue();
		}
		finally { Directory.Delete(imports, true); }
	}

	[Test]
	public async Task SuccessfulClosePreservesUnknownSiblingAndOwnerDisposalRemovesOnlyReservedArtifacts()
	{
		await using var host = NewHost();
		var unknown = host.WorldPath + ".backups";
		Directory.CreateDirectory(unknown);
		var sentinel = Path.Combine(unknown, "unknown.txt");
		File.WriteAllText(sentinel, "keep");
		try
		{
			await host.OpenAsync();
			var provider = host.Services.GetRequiredService<LightningDatabase>();
			await host.CloseAsync();
			await Assert.That(() => provider.Store.Read(_ => true)).Throws<ObjectDisposedException>();
			await Assert.That(Directory.Exists(host.WorldPath)).IsTrue();
			await Assert.That(File.Exists(host.WorldPath + ".engine.lock")).IsTrue();
			await host.DisposeAsync();
			await Assert.That(Directory.Exists(host.WorldPath)).IsFalse();
			await Assert.That(File.Exists(host.WorldPath + ".engine.lock")).IsFalse();
			await Assert.That(Directory.Exists(host.WorldPath + ".dataprotection-keys")).IsFalse();
			await Assert.That(File.ReadAllText(sentinel)).IsEqualTo("keep");
		}
		finally { Directory.Delete(unknown, true); }
	}

	[Test]
	public async Task LateStartupFailureClosesMigratedProviderBeforeSameLineageReopen()
	{
		var failure = new HostFailureEvidence();
		var host = NewHost(configure: services =>
		{
			CaptureActualOwners(services, failure);
			services.AddSingleton<IHostedService>(sp => new FailFirstStartup(sp, failure));
		});
		await AssertFailureClosesBeforeReopenAsync(host, failure);
	}

	[Test]
	public async Task FailureBeforeRunAsyncClosesMigratedProviderBeforeSameLineageReopen()
	{
		var failure = new HostFailureEvidence();
		var host = NewHost(configure: services =>
		{
			CaptureActualOwners(services, failure);
			services.RemoveAll<ILogger<SharpMUSH.Server.Program>>();
			services.AddSingleton<ILogger<SharpMUSH.Server.Program>>(sp =>
			{
				if (Interlocked.Increment(ref failure.Starts) != 1)
					return sp.GetRequiredService<ILoggerFactory>().CreateLogger<SharpMUSH.Server.Program>();
				failure.StartupObserved = true;
				failure.MigratedBeforeFailure = IdentityMatches(sp.GetRequiredService<LightningDatabase>(), failure.ExpectedIdentity);
				// Program103 resolves this logger after awaited migration and before RunAsync108.
				throw new FixtureStartupFault("Managed failure after migration, before RunAsync.");
			});
		});
		await AssertFailureClosesBeforeReopenAsync(host, failure);
	}

	private static void CaptureActualOwners(IServiceCollection services, HostFailureEvidence failure)
	{
		// This independent test capture retains ALL actual owners even when a failure prevents the
		// ordinary test factory from publishing its host, or a second host fails during reopening.
		var providerDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(LightningDatabase));
		services.Remove(providerDescriptor);
		services.AddSingleton(sp =>
		{
			var actualHost = sp.GetRequiredService<IHost>();
			failure.CapturedHost ??= actualHost;
			failure.CapturedHosts.Add(actualHost);
			var provider = (LightningDatabase)providerDescriptor.ImplementationFactory!(sp);
			failure.Provider ??= provider;
			failure.ReturnedProviders.Add(provider);
			return provider;
		});
	}

	private static bool IdentityMatches(LightningDatabase provider, Guid identity)
		=> provider.Store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("prototype.semantic.fixture.identity/v1"), out var bytes)
			&& bytes.SequenceEqual(identity.ToByteArray(bigEndian: true)));

	private static async Task AssertFailureClosesBeforeReopenAsync(SemanticWikiPrototypeHost host, HostFailureEvidence failure)
	{
		failure.ExpectedIdentity = host.FixtureId;
		var closureProven = false;
		Exception? primary = null;
		var cleanupErrors = new List<Exception>();
		try
		{
			Exception? startup = null;
			try { await host.OpenAsync(); }
			catch (Exception error) { startup = error; }
			if (!failure.StartupObserved)
				throw new InvalidOperationException("Controlled startup failure was not reached; original exception follows.", startup);
			await Assert.That(startup is FixtureStartupFault).IsTrue();
			await Assert.That(failure.MigratedBeforeFailure).IsTrue();
			await Assert.That(() => { _ = host.Services; }).Throws<InvalidOperationException>();
			await host.CloseAsync();
			// Read enters the Store's managed gate before accessing native state. A disposed gate must
			// reject here; a live Store safely returns, exposing the missed failed-host cleanup.
			await Assert.That(() => failure.Provider!.Store.Read(_ => true)).Throws<ObjectDisposedException>();
			closureProven = true;
			await host.OpenAsync();
			await AssertForwarders(host.Services);
		}
		catch (Exception error)
		{
			primary = error;
		}
		finally
		{
			// Always own the deliberately failed actual managed host, independently of factory capture.
			// Drain and close every returned owner before fixture cleanup, including a reopened host.
			try
			{
				foreach (var captured in failure.CapturedHosts.AsEnumerable().Reverse().Distinct())
				{
					try
					{
						if (!closureProven || !ReferenceEquals(captured, failure.CapturedHost)) await captured.StopAsync();
					}
					catch (Exception error) { cleanupErrors.Add(error); }
					finally
					{
						try
						{
							if (captured is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync();
							else captured.Dispose();
						}
						catch (Exception error) { cleanupErrors.Add(error); }
					}
				}
			}
			catch (Exception error) { cleanupErrors.Add(error); }
			finally
			{
				foreach (var provider in failure.ReturnedProviders.Distinct())
				{
					try { await provider.DisposeAsync(); }
					catch (Exception error) { cleanupErrors.Add(error); }
				}
				try { await host.DisposeAsync(); }
				catch (Exception error) { cleanupErrors.Add(error); }
			}
		}
		if (cleanupErrors.Count != 0)
		{
			if (primary is not null) cleanupErrors.Insert(0, primary);
			throw new AggregateException("Startup observation and cleanup failures; original observation is retained.", cleanupErrors);
		}
		if (primary is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
	}

	private sealed class FixtureStartupFault(string message) : InvalidOperationException(message);

	private sealed class HostFailureEvidence
	{
		public Guid ExpectedIdentity { get; set; }
		public bool MigratedBeforeFailure { get; set; }
		public bool StartupObserved { get; set; }
		public List<IHost> CapturedHosts { get; } = [];
		public List<LightningDatabase> ReturnedProviders { get; } = [];
		public IHost? CapturedHost { get; set; }
		public LightningDatabase? Provider { get; set; }
		public int Starts;
	}

	private sealed class FailFirstStartup(IServiceProvider services, HostFailureEvidence failure) : IHostedService
	{
		public Task StartAsync(CancellationToken cancellationToken)
		{
			if (Interlocked.Increment(ref failure.Starts) != 1) return Task.CompletedTask;
			failure.StartupObserved = true;
			failure.MigratedBeforeFailure = IdentityMatches(services.GetRequiredService<LightningDatabase>(), failure.ExpectedIdentity);
			throw new FixtureStartupFault("Managed late-start failure after fixture migration.");
		}

		public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}

	[Test]
	public async Task FailedStartPublishesNoServicesAndDisposesBeforeOwnedCleanup()
	{
		await using var host = NewHost(Path.Combine(ScratchRoot, "missing-config-" + Guid.NewGuid().ToString("N")));
		await Assert.That(() => host.OpenAsync()).Throws<Exception>();
		await Assert.That(() => { _ = host.Services; }).Throws<InvalidOperationException>();
		await Assert.That(() => host.OpenAsync()).Throws<InvalidOperationException>();
		await host.CloseAsync();
		await host.DisposeAsync();
		await Assert.That(Directory.Exists(host.WorldPath)).IsFalse();
		await Assert.That(File.Exists(host.WorldPath + ".engine.lock")).IsFalse();
	}
}
