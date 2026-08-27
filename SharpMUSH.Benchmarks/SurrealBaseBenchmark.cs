using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Benchmarks;

/// <summary>
/// Base class for all SurrealDB-backed benchmarks.
/// SurrealDB runs embedded in-process (mem://) - no database Testcontainer, unlike the ArangoDB and
/// Memgraph base classes. Still spins up NATS and wires up the full DI stack, providing a
/// ready-to-use <see cref="IMUSHCodeParser"/>.
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

	[GlobalSetup]
	public virtual async ValueTask Setup()
	{
		_natsContainer = await BenchmarkHelpers.StartNatsContainerAsync().ConfigureAwait(false);
		Environment.SetEnvironmentVariable("NATS_URL",
			$"nats://localhost:{_natsContainer.GetMappedPublicPort(4222)}");

		var configFile = Path.Combine(AppContext.BaseDirectory, "mushcnf.dst");

		_server = new TestWebApplicationBuilderFactory<Server.Program>(
			acnf: null,
			configFile: configFile,
			databaseProvider: DatabaseProvider.SurrealDB,
			surrealEndpoint: "mem://");

		_database = _server!.Services.GetRequiredService<ISharpDatabase>();
	}

	[GlobalCleanup]
	public async ValueTask Cleanup()
	{
		if (_natsContainer is not null)
			await _natsContainer.DisposeAsync().ConfigureAwait(false);

		_server?.Dispose();
		Environment.SetEnvironmentVariable("NATS_URL", null);
	}

	protected async Task<IMUSHCodeParser?> TestParser() =>
		await BenchmarkHelpers.CreateTestParser(_database!, _server!.Services).ConfigureAwait(false);
}
