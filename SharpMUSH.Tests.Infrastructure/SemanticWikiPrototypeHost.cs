using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>Owns a retained actual test host with explicit fixture-only projection activation.</summary>
public sealed class SemanticWikiPrototypeHost : IAsyncDisposable
{
	private readonly SemanticWikiPrototypeFixture _fixture;
	private readonly string _sqlConnectionString;
	private readonly string _configFile;
	private readonly INotifyService? _notifier;
	private readonly string _sqlPlatform;
	private readonly Action<IServiceCollection>? _configureTestServices;
	private readonly SemaphoreSlim _lifecycle = new(1, 1);
	private readonly Dictionary<string, bool> _ownedAuxiliaryPaths = new(StringComparer.Ordinal);
	private ServerTestWebApplicationBuilderFactory<SharpMUSH.Server.Program>? _server;
	private IServiceProvider? _services;
	private bool _disposed;

	public SemanticWikiPrototypeHost(string ownedScratchRoot, string sqlConnectionString, string configFile,
		INotifyService? notifier, string sqlPlatform = "mysql")
		: this(ownedScratchRoot, sqlConnectionString, configFile, notifier, sqlPlatform, null) { }

	internal SemanticWikiPrototypeHost(string ownedScratchRoot, string sqlConnectionString, string configFile,
		INotifyService? notifier, string sqlPlatform, Action<IServiceCollection>? configureTestServices)
	{
		_configureTestServices = configureTestServices;
		_fixture = new SemanticWikiPrototypeFixture(ownedScratchRoot);
		_sqlConnectionString = sqlConnectionString;
		_configFile = configFile;
		_notifier = notifier;
		_sqlPlatform = sqlPlatform;
	}

	public string WorldPath => _fixture.WorldPath;
	public Guid FixtureId => _fixture.FixtureId;
	internal Action<LightningDatabase>? ProviderOpened { get; set; }
	public string CleanupDiagnostic => _fixture.CleanupDiagnostic;
	public IServiceProvider Services => Volatile.Read(ref _services)
		?? throw new InvalidOperationException("Fixture host is closed or opening.");

	public async Task OpenAsync()
	{
		EnterLifecycle();
		try
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			if (_server is not null) throw new InvalidOperationException("Close the existing or failed host before reopening.");
			// Yield with the gate held: another open/close/dispose cannot race host construction.
			await Task.Yield();
			RejectReplacementOperations();
			ReserveAuxiliaryPaths();
			_server = new ServerTestWebApplicationBuilderFactory<SharpMUSH.Server.Program>(_sqlConnectionString, _configFile,
				_notifier, _sqlPlatform, WorldPath, ConfigureFixtureServices);
			// Program awaits migration. Publish only after the entire real host has started.
			Volatile.Write(ref _services, _server.Services);
		}
		finally { _lifecycle.Release(); }
	}

	public async Task ActivateAtomicWikiProjectionAsync(CancellationToken ct = default)
	{
		EnterLifecycle();
		try
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			RejectReplacementOperations();
			if (Services.GetRequiredService<IPackageInstallService>() is not SemanticWikiPackageAdmission installer
				|| Services.GetRequiredService<IPackageOperationRunner>() is not SemanticWikiPackageOperationRunner runner
				|| !ReferenceEquals(installer.Owner, _fixture.Capability) || !ReferenceEquals(runner.Owner, _fixture.Capability))
				throw SemanticFixtureCapability.Unavailable();
			await Services.GetRequiredService<LightningDatabase>().ActivateAtomicWikiProjectionAsync(_fixture.Capability, ct);
		}
		finally { _lifecycle.Release(); }
	}

	private void ConfigureFixtureServices(IServiceCollection services)
	{
		services.RemoveAll<LightningDatabase>();
		services.AddSingleton(sp =>
		{
			var catalog = sp.GetRequiredService<PluginCatalog>();
			var provider = _fixture.OpenProvider(sp.GetRequiredService<ILogger<LightningDatabase>>(),
				new LightningStoreOptions { Path = WorldPath, Sync = LightningSyncMode.Full },
				sp.GetRequiredService<IPasswordService>(), sp.GetRequiredService<IObjectRelationLoader>(),
				catalog.MigrationSources, catalog.AllFlags, sp.GetRequiredService<AccountCredentialGate>(),
				new Lazy<IEnumerable<IAccountCredentialRevocation>>(() => sp.GetServices<IAccountCredentialRevocation>().ToArray()));
			ProviderOpened?.Invoke(provider);
			return provider;
		});
		services.AddSingleton<PackageInstallService>();
		services.AddSingleton<PackageOperationRunner>();
		services.RemoveAll<IPackageInstallService>();
		services.RemoveAll<IPackageOperationRunner>();
		services.AddSingleton<IPackageInstallService>(sp => new SemanticWikiPackageAdmission(sp.GetRequiredService<PackageInstallService>(), _fixture.Capability, sp.GetRequiredService<LightningDatabase>()));
		services.AddSingleton<IPackageOperationRunner>(sp => new SemanticWikiPackageOperationRunner(sp.GetRequiredService<PackageOperationRunner>(), _fixture.Capability, sp.GetRequiredService<LightningDatabase>()));
		_configureTestServices?.Invoke(services);
	}

	private void RejectReplacementOperations()
	{
		// Program applies import intents before provider resolution. A provider admission guard is too late.
		if (Path.Exists(WorldPath + ".imports"))
			throw new InvalidOperationException("Import-operation artifacts are forbidden for a retained fixture lineage.");
		if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PENNMUSH_DATABASE_PATH")))
			throw new InvalidOperationException("Startup database conversion is forbidden for a retained fixture lineage.");
	}

	private void ReserveAuxiliaryPaths()
	{
		// These exact paths belong to this owner only if we created them while absent. Unknown siblings
		// remain untouched, including on failed startup. Environment-selected external paths are never owned.
		ReserveDirectory(WorldPath + ".dataprotection-keys");
		ReserveDirectory(WorldPath + ".backups");
		var engineLock = WorldPath + ".engine.lock";
		SemanticFixtureCapability.RejectLinkedAncestors(engineLock);
		if (Path.Exists(engineLock)) return;
		using (new FileStream(engineLock, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
		_ownedAuxiliaryPaths.Add(engineLock, false);
	}

	private void ReserveDirectory(string path)
	{
		SemanticFixtureCapability.RejectLinkedAncestors(path);
		if (Path.Exists(path)) return;
		Directory.CreateDirectory(path);
		_ownedAuxiliaryPaths.Add(path, true);
	}

	public async ValueTask CloseAsync()
	{
		EnterLifecycle();
		try { await CloseCoreAsync(); }
		finally { _lifecycle.Release(); }
	}

	private async ValueTask CloseCoreAsync()
	{
		Volatile.Write(ref _services, null);
		if (_server is not { } server) return;
		// The lower factory owns one shared actual-host shutdown/disposal completion, including failed
		// startup. Retain its reference until the entire lifetime has completed successfully.
		await server.DisposeAsync();
		_server = null;
	}

	public async ValueTask DisposeAsync()
	{
		EnterLifecycle();
		try
		{
			if (_disposed) return;
			await CloseCoreAsync();
			await _fixture.DisposeAsync();
			if (!_fixture.HasQuarantinedArtifacts)
			{
				foreach (var (path, directory) in _ownedAuxiliaryPaths)
				{
					SemanticFixtureCapability.RejectLinkedAncestors(path);
					if (directory && Directory.Exists(path)) Directory.Delete(path, true);
					else if (!directory && File.Exists(path)) File.Delete(path);
				}
			}
			_disposed = true;
		}
		finally { _lifecycle.Release(); }
	}

	private void EnterLifecycle()
	{
		if (!_lifecycle.Wait(0)) throw new InvalidOperationException("A fixture host lifecycle operation is already running.");
	}
}
