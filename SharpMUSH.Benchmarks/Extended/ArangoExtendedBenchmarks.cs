using Core.Arango;
using Core.Arango.Serialization.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SharpMUSH.Library;
using Testcontainers.ArangoDb;

namespace SharpMUSH.Benchmarks;

/// <summary>
/// The <see cref="ExtendedDatabaseBenchmarks"/> suite backed by <b>ArangoDB</b>. Bootstraps the
/// same way <see cref="BaseBenchmark"/> does (ArangoDB + NATS Testcontainers) rather than
/// inheriting it, since a benchmark class can only have one base class and
/// <see cref="ExtendedDatabaseBenchmarks"/> already claims that slot for the shared benchmark
/// bodies.
/// </summary>
[BenchmarkCategory("Database Extended", "ArangoDB")]
public class ArangoExtendedBenchmarks : ExtendedDatabaseBenchmarks
{
	public ArangoExtendedBenchmarks() =>
		Log.Logger = new LoggerConfiguration()
			.WriteTo.Console()
			.MinimumLevel.Information()
			.CreateLogger();

	private TestWebApplicationBuilderFactory<Server.Program>? _server;
	private ISharpDatabase? _database;
	private ArangoDbContainer? _arangoContainer;
	private IContainer? _natsContainer;

	protected override ISharpDatabase Database => _database!;

	[GlobalSetup]
	public async ValueTask Setup()
	{
		_arangoContainer = new ArangoDbBuilder("arangodb:latest")
			.WithPassword("password")
			.Build();

		await _arangoContainer.StartAsync().ConfigureAwait(false);

		var config = new ArangoConfiguration
		{
			ConnectionString = $"Server={_arangoContainer.GetTransportAddress()};User=root;Realm=;Password=password;",
			Serializer = new ArangoJsonSerializer(new ArangoJsonDefaultPolicy())
		};

		_natsContainer = await BenchmarkHelpers.StartNatsContainerAsync().ConfigureAwait(false);
		Environment.SetEnvironmentVariable("NATS_URL",
			$"nats://localhost:{_natsContainer.GetMappedPublicPort(4222)}");

		var configFile = Path.Combine(AppContext.BaseDirectory, "mushcnf.dst");

		_server = new TestWebApplicationBuilderFactory<Server.Program>(config, configFile);
		_database = _server!.Services.GetRequiredService<ISharpDatabase>();

		await SeedAsync().ConfigureAwait(false);
	}

	[GlobalCleanup]
	public async ValueTask Cleanup()
	{
		if (_natsContainer is not null)
			await _natsContainer.DisposeAsync().ConfigureAwait(false);

		if (_arangoContainer is not null)
			await _arangoContainer.DisposeAsync().ConfigureAwait(false);

		_server?.Dispose();
		Environment.SetEnvironmentVariable("NATS_URL", null);
	}
}
