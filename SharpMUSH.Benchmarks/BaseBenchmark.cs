using System.Collections.Concurrent;
using System.Text;
using BenchmarkDotNet.Attributes;
using Core.Arango;
using Core.Arango.Serialization.Newtonsoft;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
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

		_server = new TestWebApplicationBuilderFactory<Server.Program>(config, configFile, colorFile);
		_database = _server!.Services.GetRequiredService<ISharpDatabase>();
	}

	[GlobalCleanup]
	public async ValueTask Cleanup()
	{
		await Task.CompletedTask;
	}

	protected async Task<IMUSHCodeParser?> TestParser()
	{
		var realOne = await _database!.GetObjectNodeAsync(new DBRef(1));
		var one = realOne.Object()!.DBRef;

		var mockPublisher = Substitute.For<IPublisher>();
		var simpleConnectionService = new ConnectionService(mockPublisher);
		simpleConnectionService.Register(1, "localhost", "localhost", "test",  _ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		simpleConnectionService.Bind(1, one);

		var parser = _server!.Services.GetRequiredService<IMUSHCodeParser>();
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