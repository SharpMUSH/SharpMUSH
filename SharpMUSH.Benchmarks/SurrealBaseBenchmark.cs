using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Benchmarks;

/// <summary>
/// Base class for all SurrealDB-backed benchmarks.
/// SurrealDB runs embedded in-process, defaulting to mem:// for isolation.
/// No database Testcontainer is used, unlike the ArangoDB and Memgraph base classes.
/// Still spins up NATS and wires up the full DI stack, providing a ready-to-use <see cref="IMUSHCodeParser"/>.
///
/// The SurrealDB endpoint is read from the <c>SHARPMUSH_SURREALDB_BENCH_ENDPOINT</c> environment variable,
/// defaulting to <c>mem://</c>. To benchmark the production on-disk engine (RocksDB), set the variable to
/// <c>rocksdb://{absolute_path}</c> where the directory is on a real filesystem (not tmpfs), so fsync cost
/// is not hidden. The directory will be created in <see cref="Setup"/> and deleted in <see cref="Cleanup"/>.
/// </summary>
[Config(typeof(AdaptiveBenchmarkConfig))]
public class SurrealBaseBenchmark
{
	public SurrealBaseBenchmark() =>
		Log.Logger = new LoggerConfiguration()
			.WriteTo.Console()
			.MinimumLevel.Information()
			.CreateLogger();

	protected TestWebApplicationBuilderFactory<Server.Program>? _server;
	protected ISharpDatabase? _database;
	private IContainer? _natsContainer;
	private string _surrealEndpoint = null!;
	private string? _rocksDbPath;
	private bool _createdRocksDbDirectory;

	[GlobalSetup]
	public virtual async ValueTask Setup()
	{
		_natsContainer = await BenchmarkHelpers.StartNatsContainerAsync().ConfigureAwait(false);
		Environment.SetEnvironmentVariable("NATS_URL",
			$"nats://localhost:{_natsContainer.GetMappedPublicPort(4222)}");

		_surrealEndpoint = Environment.GetEnvironmentVariable("SHARPMUSH_SURREALDB_BENCH_ENDPOINT") ?? "mem://";

		// If using rocksdb:// backend, create the directory for the benchmark
		if (_surrealEndpoint.StartsWith("rocksdb://", StringComparison.OrdinalIgnoreCase))
		{
			_rocksDbPath = _surrealEndpoint.Substring("rocksdb://".Length);
			if (!string.IsNullOrEmpty(_rocksDbPath) && !Directory.Exists(_rocksDbPath))
			{
				Directory.CreateDirectory(_rocksDbPath);
				_createdRocksDbDirectory = true;
			}
		}

		var configFile = Path.Combine(AppContext.BaseDirectory, "mushcnf.dst");

		_server = new TestWebApplicationBuilderFactory<Server.Program>(
			acnf: null,
			configFile: configFile,
			databaseProvider: DatabaseProvider.SurrealDB,
			surrealEndpoint: _surrealEndpoint);

		_database = _server!.Services.GetRequiredService<ISharpDatabase>();
	}

	[GlobalCleanup]
	public async ValueTask Cleanup()
	{
		if (_natsContainer is not null)
			await _natsContainer.DisposeAsync().ConfigureAwait(false);

		_server?.Dispose();
		Environment.SetEnvironmentVariable("NATS_URL", null);

		// Clean up the rocksdb directory only if this benchmark created it
		if (_createdRocksDbDirectory && !string.IsNullOrEmpty(_rocksDbPath) && Directory.Exists(_rocksDbPath))
		{
			try
			{
				Directory.Delete(_rocksDbPath, recursive: true);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				// Ignore errors during cleanup
			}
		}
	}

	protected async Task<IMUSHCodeParser?> TestParser() =>
		await BenchmarkHelpers.CreateTestParser(_database!, _server!.Services).ConfigureAwait(false);
}
