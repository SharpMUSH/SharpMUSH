using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Benchmarks;

/// <summary>
/// The <see cref="ExtendedDatabaseBenchmarks"/> suite backed by <b>Lightning</b> (LMDB). Bootstraps
/// the same way <see cref="LightningBaseBenchmark"/> does (own data directory, no Testcontainer for
/// the database itself) rather than inheriting it, since a benchmark class can only have one base
/// class and <see cref="ExtendedDatabaseBenchmarks"/> already claims that slot for the shared
/// benchmark bodies.
/// </summary>
[BenchmarkCategory("Database Extended", "Lightning")]
public class LightningExtendedBenchmarks : ExtendedDatabaseBenchmarks
{
	public LightningExtendedBenchmarks() =>
		Log.Logger = new LoggerConfiguration()
			.WriteTo.Console()
			.MinimumLevel.Information()
			.CreateLogger();

	private TestWebApplicationBuilderFactory<Server.Program>? _server;
	private ISharpDatabase? _database;
	private IContainer? _natsContainer;
	private string? _lightningPath;

	protected override ISharpDatabase Database => _database!;

	[GlobalSetup]
	public async ValueTask Setup()
	{
		_natsContainer = await BenchmarkHelpers.StartNatsContainerAsync().ConfigureAwait(false);
		Environment.SetEnvironmentVariable("NATS_URL",
			$"nats://localhost:{_natsContainer.GetMappedPublicPort(4222)}");

		var configFile = Path.Combine(AppContext.BaseDirectory, "mushcnf.dst");

		// Same tmpfs-avoidance rule as LightningBaseBenchmark: a real, non-tmpfs directory so
		// fsync cost shows up in the numbers.
		_lightningPath = LightningBaseBenchmark.CreateDataDirectory();

		_server = new TestWebApplicationBuilderFactory<Server.Program>(
			acnf: null,
			configFile: configFile,
			databaseProvider: DatabaseProvider.Lightning,
			lightningPath: _lightningPath);

		_database = _server!.Services.GetRequiredService<ISharpDatabase>();

		await SeedAsync().ConfigureAwait(false);
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
				// Best-effort, same as LightningBaseBenchmark: a lingering mdb.lck can outlive the
				// writer thread's join by a few milliseconds under load.
			}
		}
	}
}
