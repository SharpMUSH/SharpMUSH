using System.Collections.Concurrent;
using System.Text;
using BenchmarkDotNet.Attributes;
using Core.Arango;
using Core.Arango.Serialization.Newtonsoft;
using Serilog;
using SharpMUSH.Library;
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

	private Infrastructure? _infrastructure;
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

		var config = new ArangoConfiguration()
		{
			ConnectionString = $"Server={_container.GetTransportAddress()};User=root;Realm=;Password=password;",
			Serializer = new ArangoNewtonsoftSerializer(new ArangoNewtonsoftDefaultContractResolver())
		};
		
		var configFile = Path.Combine(AppContext.BaseDirectory, "mushcnf.dst");
		_infrastructure = new Infrastructure(config, configFile);

		_database = _infrastructure!.Services.GetService(typeof(ISharpDatabase)) as ISharpDatabase;

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
		_infrastructure!.Dispose();
	}

	private async Task<(ISharpDatabase Database, Infrastructure Infrastructure)> IntegrationServer()
	{
		await Task.CompletedTask;

		return (_database!, _infrastructure!);
	}

	protected async Task<IMUSHCodeParser?> TestParser()
	{
		var (database, integrationServer) = await IntegrationServer();

		var realOne = await database.GetObjectNodeAsync(new DBRef(1));
		var one = realOne.Object()!.DBRef;

		var simpleConnectionService = new ConnectionService();
		simpleConnectionService.Register(1, _ => ValueTask.CompletedTask,  _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		simpleConnectionService.Bind(1, one);

		var parser = (IMUSHCodeParser)integrationServer.Services.GetService(typeof(IMUSHCodeParser))!;
		return parser.FromState(new ParserState(
			Registers: new ConcurrentStack<Dictionary<string, MString>>([[]]),
			IterationRegisters: new ConcurrentStack<IterationWrapper<MString>>(),
			RegexRegisters: new ConcurrentStack<Dictionary<string, MString>>(),
			ExecutionStack: new ConcurrentStack<Execution>(),
			CurrentEvaluation: null,
			ParserFunctionDepth: 0,
			Function: null,
			Command: "think",
			Switches: [],
			Arguments: [],
			Executor: one,
			Enactor: one,
			Caller: one,
			Handle: 1
		));
	}
}