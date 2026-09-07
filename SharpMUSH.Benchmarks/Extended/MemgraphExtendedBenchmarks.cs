using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Benchmarks;

/// <summary>
/// The <see cref="ExtendedDatabaseBenchmarks"/> suite backed by <b>Memgraph</b>. Bootstraps the
/// same way <see cref="MemgraphBaseBenchmark"/> does (Memgraph + NATS Testcontainers) rather than
/// inheriting it, since a benchmark class can only have one base class and
/// <see cref="ExtendedDatabaseBenchmarks"/> already claims that slot for the shared benchmark
/// bodies.
/// </summary>
[BenchmarkCategory("Database Extended", "Memgraph")]
public class MemgraphExtendedBenchmarks : ExtendedDatabaseBenchmarks
{
	public MemgraphExtendedBenchmarks() =>
		Log.Logger = new LoggerConfiguration()
			.WriteTo.Console()
			.MinimumLevel.Information()
			.CreateLogger();

	private TestWebApplicationBuilderFactory<Server.Program>? _server;
	private ISharpDatabase? _database;
	private IContainer? _memgraphContainer;
	private IContainer? _natsContainer;

	protected override ISharpDatabase Database => _database!;

	[GlobalSetup]
	public async ValueTask Setup()
	{
		_memgraphContainer = new ContainerBuilder("memgraph/memgraph:3.8.1")
			.WithPortBinding(7687, true)
			.WithCommand(
				"--bolt-num-workers=4",
				"--storage-mode=IN_MEMORY_TRANSACTIONAL",
				"--memory-limit=1024",
				"--log-level=WARNING")
			.WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("You are running Memgraph"))
			.WithReuse(false)
			.Build();

		await _memgraphContainer.StartAsync().ConfigureAwait(false);

		var memgraphUri = $"bolt://localhost:{_memgraphContainer.GetMappedPublicPort(7687)}";

		_natsContainer = await BenchmarkHelpers.StartNatsContainerAsync().ConfigureAwait(false);
		Environment.SetEnvironmentVariable("NATS_URL",
			$"nats://localhost:{_natsContainer.GetMappedPublicPort(4222)}");

		var configFile = Path.Combine(AppContext.BaseDirectory, "mushcnf.dst");

		_server = new TestWebApplicationBuilderFactory<Server.Program>(
			acnf: null,
			configFile: configFile,
			databaseProvider: DatabaseProvider.Memgraph,
			memgraphUri: memgraphUri);

		_database = _server!.Services.GetRequiredService<ISharpDatabase>();

		await SeedAsync().ConfigureAwait(false);
	}

	[GlobalCleanup]
	public async ValueTask Cleanup()
	{
		if (_natsContainer is not null)
			await _natsContainer.DisposeAsync().ConfigureAwait(false);

		if (_memgraphContainer is not null)
			await _memgraphContainer.DisposeAsync().ConfigureAwait(false);

		_server?.Dispose();
		Environment.SetEnvironmentVariable("NATS_URL", null);
	}
}
