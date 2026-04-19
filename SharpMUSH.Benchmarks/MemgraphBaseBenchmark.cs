using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Benchmarks;

/// <summary>
/// Base class for all Memgraph-backed benchmarks.
/// Spins up Memgraph and NATS Testcontainers, wires up the full DI stack,
/// and provides a ready-to-use <see cref="IMUSHCodeParser"/>.
/// </summary>
[Config(typeof(AdaptiveBenchmarkConfig))]
public class MemgraphBaseBenchmark
{
	public MemgraphBaseBenchmark() =>
		Log.Logger = new LoggerConfiguration()
			.WriteTo.Console()
			.MinimumLevel.Information()
			.CreateLogger();

	protected TestWebApplicationBuilderFactory<Server.Program>? _server;
	protected ISharpDatabase? _database;
	private IContainer? _memgraphContainer;
	private IContainer? _natsContainer;

	[GlobalSetup]
	public virtual async ValueTask Setup()
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
		var colorFile = Path.Combine(AppContext.BaseDirectory, "colors.json");

		_server = new TestWebApplicationBuilderFactory<Server.Program>(
			acnf: null,
			configFile: configFile,
			colorFile: colorFile,
			databaseProvider: DatabaseProvider.Memgraph,
			memgraphUri: memgraphUri);

		_database = _server!.Services.GetRequiredService<ISharpDatabase>();
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

	protected async Task<IMUSHCodeParser?> TestParser() =>
		await BenchmarkHelpers.CreateTestParser(_database!, _server!.Services).ConfigureAwait(false);
}
