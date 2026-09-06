using Core.Arango;
using Core.Arango.Serialization.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
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

	/// <summary>Base address of the ArangoDB container, for the profile harness's request counter.</summary>
	protected string? ArangoBaseAddress =>
		_arangoContainer?.GetTransportAddress();

	protected async Task<IMUSHCodeParser?> TestParser() =>
		await BenchmarkHelpers.CreateTestParser(_database!, _server!.Services).ConfigureAwait(false);

	private IMUSHCodeParser? _baseParser;
	private DBRef _executor;

	/// <summary>
	/// A parser over a fresh top-level state, as every player command gets one. Call this per
	/// operation rather than caching one parser: the invocation and call-depth counters live on the
	/// state and are cumulative, so a parser reused across a benchmark's thousands of iterations
	/// crosses the function-invocation limit and thereafter times only the short-circuit.
	/// </summary>
	protected IMUSHCodeParser FreshParser()
	{
		if (_baseParser is null)
		{
			_baseParser = _server!.Services.GetRequiredService<IMUSHCodeParser>();
			_executor = BenchmarkHelpers.ExecutorDbRef(_database!).ConfigureAwait(false).GetAwaiter().GetResult();
		}

		return _baseParser.FromState(BenchmarkHelpers.FreshState(_executor));
	}
}
