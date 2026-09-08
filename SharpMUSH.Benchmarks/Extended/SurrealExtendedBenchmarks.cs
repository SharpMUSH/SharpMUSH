using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Benchmarks;

/// <summary>
/// The <see cref="ExtendedDatabaseBenchmarks"/> suite backed by <b>SurrealDB</b>. Bootstraps the
/// same way <see cref="SurrealBaseBenchmark"/> does (embedded <c>mem://</c>, no database
/// Testcontainer) rather than inheriting it, since a benchmark class can only have one base class
/// and <see cref="ExtendedDatabaseBenchmarks"/> already claims that slot for the shared benchmark
/// bodies.
/// </summary>
[BenchmarkCategory("Database Extended", "SurrealDB")]
public class SurrealExtendedBenchmarks : ExtendedDatabaseBenchmarks
{
	public SurrealExtendedBenchmarks() =>
		Log.Logger = new LoggerConfiguration()
			.WriteTo.Console()
			.MinimumLevel.Information()
			.CreateLogger();

	private TestWebApplicationBuilderFactory<Server.Program>? _server;
	private ISharpDatabase? _database;
	private IContainer? _natsContainer;

	protected override ISharpDatabase Database => _database!;

	[GlobalSetup]
	public async ValueTask Setup()
	{
		_natsContainer = await BenchmarkHelpers.StartNatsContainerAsync().ConfigureAwait(false);
		Environment.SetEnvironmentVariable("NATS_URL",
			$"nats://localhost:{_natsContainer.GetMappedPublicPort(4222)}");

		var configFile = Path.Combine(AppContext.BaseDirectory, "mushcnf.dst");

		_server = new TestWebApplicationBuilderFactory<Server.Program>(
			configFile: configFile,
			databaseProvider: DatabaseProvider.SurrealDB,
			surrealEndpoint: "mem://");

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
	}
}
