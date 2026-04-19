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
/// Base class for all ArangoDB-backed benchmarks.
/// Spins up ArangoDB and NATS Testcontainers, wires up the full DI stack,
/// and provides a ready-to-use <see cref="IMUSHCodeParser"/>.
/// </summary>
[Config(typeof(AdaptiveBenchmarkConfig))]
public class BaseBenchmark
{
	public BaseBenchmark() =>
		Log.Logger = new LoggerConfiguration()
			.WriteTo.Console()
			.MinimumLevel.Information()
			.CreateLogger();

	protected TestWebApplicationBuilderFactory<Server.Program>? _server;
	protected ISharpDatabase? _database;
	private ArangoDbContainer? _arangoContainer;
	private IContainer? _natsContainer;

	[GlobalSetup]
	public virtual async ValueTask Setup()
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

	protected async Task<IMUSHCodeParser?> TestParser() =>
		await BenchmarkHelpers.CreateTestParser(_database!, _server!.Services).ConfigureAwait(false);
}
