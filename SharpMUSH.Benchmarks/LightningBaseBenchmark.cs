using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Benchmarks;

/// <summary>
/// Base class for all Lightning-backed benchmarks.
/// Lightning (LMDB via Lightning.NET) runs embedded in-process against a plain directory - no
/// database Testcontainer, unlike the ArangoDB and Memgraph base classes. Still spins up NATS and
/// wires up the full DI stack, providing a ready-to-use <see cref="IMUSHCodeParser"/>.
/// </summary>
[Config(typeof(AdaptiveBenchmarkConfig))]
public class LightningBaseBenchmark
{
	public LightningBaseBenchmark() =>
		Log.Logger = new LoggerConfiguration()
			.WriteTo.Console()
			.MinimumLevel.Information()
			.CreateLogger();

	protected TestWebApplicationBuilderFactory<Server.Program>? _server;
	protected ISharpDatabase? _database;
	private IContainer? _natsContainer;
	private string? _lightningPath;

	[GlobalSetup]
	public virtual async ValueTask Setup()
	{
		_natsContainer = await BenchmarkHelpers.StartNatsContainerAsync().ConfigureAwait(false);
		Environment.SetEnvironmentVariable("NATS_URL",
			$"nats://localhost:{_natsContainer.GetMappedPublicPort(4222)}");

		var configFile = Path.Combine(AppContext.BaseDirectory, "mushcnf.dst");

		// No container, no shared server: LMDB is a plain directory. Give each run its own so
		// benchmark iterations never collide with a prior run's data.
		_lightningPath = Path.Combine(Path.GetTempPath(), "sharpmush-lightning-benchmarks-" + Guid.NewGuid().ToString("N"));

		_server = new TestWebApplicationBuilderFactory<Server.Program>(
			acnf: null,
			configFile: configFile,
			databaseProvider: DatabaseProvider.Lightning,
			lightningPath: _lightningPath);

		_database = _server!.Services.GetRequiredService<ISharpDatabase>();
	}

	[GlobalCleanup]
	public async ValueTask Cleanup()
	{
		if (_natsContainer is not null)
			await _natsContainer.DisposeAsync().ConfigureAwait(false);

		_server?.Dispose();
		Environment.SetEnvironmentVariable("NATS_URL", null);

		if (_lightningPath is not null && Directory.Exists(_lightningPath))
		{
			try
			{
				Directory.Delete(_lightningPath, recursive: true);
			}
			catch (IOException)
			{
				// Best-effort: a lingering LMDB lock file (mdb.lck) can outlive the writer thread's
				// join by a few milliseconds under load. Leaving the temp directory behind costs
				// disk, not correctness.
			}
		}
	}

	protected async Task<IMUSHCodeParser?> TestParser() =>
		await BenchmarkHelpers.CreateTestParser(_database!, _server!.Services).ConfigureAwait(false);
}
