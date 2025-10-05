using System.Collections.Concurrent;
using System.Text;
using BenchmarkDotNet.Attributes;
using Core.Arango;
using Core.Arango.Serialization.Newtonsoft;
using Microsoft.Extensions.DependencyInjection;
using OneOf.Types;
using Serilog;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using Testcontainers.ArangoDb;

namespace SharpMUSH.Benchmarks;

[MemoryDiagnoser]
[ThreadingDiagnoser]
[ExceptionDiagnoser]
public class BaseBenchmark
{
	public BaseBenchmark() =>
		Log.Logger = new LoggerConfiguration()
			.WriteTo.Console()
			.MinimumLevel.Information()
			.CreateLogger();

	private TestWebApplicationBuilderFactory<Server.Program>? _server;
	private ISharpDatabase? _database;
	private ArangoDbContainer? _container;
	
	[GlobalSetup]
	public async ValueTask Setup()
	{
		_container = new ArangoDbBuilder()
			.WithImage("arangodb:latest")
			.WithPassword("password")
			.Build();

		await _container.StartAsync()
			.ConfigureAwait(false);

		var config = new ArangoConfiguration
		{
			ConnectionString = $"Server={_container.GetTransportAddress()};User=root;Realm=;Password=password;",
			Serializer = new ArangoNewtonsoftSerializer(new ArangoNewtonsoftDefaultContractResolver())
		};
		
		var configFile = Path.Combine(AppContext.BaseDirectory, "mushcnf.dst");
		var colorFile = Path.Combine(AppContext.BaseDirectory, "colors.json");

		_server = new TestWebApplicationBuilderFactory<Server.Program>(config, configFile, colorFile, null);
		_database = _server!.Services.GetRequiredService<ISharpDatabase>();

		try
		{
			await _database!.Migrate();
		}
		catch (Exception ex)
		{
			Log.Fatal(ex, "Failed to migrate database");
		}
	}

	[GlobalCleanup]
	public async ValueTask Cleanup()
	{
		await Task.CompletedTask;
	}

	private async Task<(ISharpDatabase Database, TestWebApplicationBuilderFactory<Server.Program> Infrastructure)> IntegrationServer()
	{
		await Task.CompletedTask;

		return (_database!, _server!);
	}

	protected async Task<IMUSHCodeParser?> TestParser()
	{
		var (database, integrationServer) = await IntegrationServer();

		var realOne = await database.GetObjectNodeAsync(new DBRef(1));
		var one = realOne.Object()!.DBRef;

		var simpleConnectionService = new ConnectionService();
		simpleConnectionService.Register(1, _ => ValueTask.CompletedTask,  _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		simpleConnectionService.Bind(1, one);

		var parser = integrationServer.Services.GetRequiredService<IMUSHCodeParser>();
		return parser.FromState(new ParserState(
			Registers: new ConcurrentStack<Dictionary<string, MString>>([[]]),
			IterationRegisters: new ConcurrentStack<IterationWrapper<MString>>(),
			RegexRegisters: new ConcurrentStack<Dictionary<string, MString>>(),
			ExecutionStack: new ConcurrentStack<Execution>(),
			EnvironmentRegisters: [],
			CurrentEvaluation: null,
			ParserFunctionDepth: 0,
			Function: null,
			Command: "think",
			CommandInvoker: _ => ValueTask.FromResult(new Option<CallState>(new None())),
			Switches: [],
			Arguments: [],
			Executor: one,
			Enactor: one,
			Caller: one,
			Handle: 1
		));
	}
}