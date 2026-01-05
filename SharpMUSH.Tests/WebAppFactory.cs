using System.Collections.Concurrent;
using System.Text;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Core.Arango;
using Core.Arango.Serialization.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OneOf.Types;
using Quartz;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

public class WebAppFactory : IAsyncInitializer
{
	[ClassDataSource<ArangoDbTestServer>(Shared = SharedType.PerTestSession)]
	public required ArangoDbTestServer ArangoDbTestServer { get; init; }
	
	[ClassDataSource<RedPandaTestServer>(Shared = SharedType.PerTestSession)]
	public required RedPandaTestServer RedPandaTestServer { get; init; }
	
	[ClassDataSource<MySqlTestServer>(Shared = SharedType.PerTestSession)]
	public required MySqlTestServer MySqlTestServer { get; init; }
	
	[ClassDataSource<PrometheusTestServer>(Shared = SharedType.PerTestSession)]
	public required PrometheusTestServer PrometheusTestServer { get; init; }
	
	[ClassDataSource<RedisTestServer>(Shared = SharedType.PerTestSession)]
	public required RedisTestServer RedisTestServer { get; init; }

	public IServiceProvider Services => _server!.Services;
	private TestWebApplicationBuilderFactory<SharpMUSH.Server.Program>? _server;
	private DBRef _one;

	public IMUSHCodeParser FunctionParser
	{
		get
		{
			var integrationServer = _server!;
			return new MUSHCodeParser(
				integrationServer.Services.GetRequiredService<ILogger<MUSHCodeParser>>(),
				integrationServer.Services.GetRequiredService<LibraryService<string, FunctionDefinition>>(),
				integrationServer.Services.GetRequiredService<LibraryService<string, CommandDefinition>>(),
				integrationServer.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>(),
				integrationServer.Services,
				state: new ParserState(
					Registers: new ConcurrentStack<Dictionary<string, MString>>([[]]),
					IterationRegisters: [],
					RegexRegisters: [],
					ExecutionStack: [],
					EnvironmentRegisters: [],
					CurrentEvaluation: null,
					ParserFunctionDepth: 0,
					Function: null,
					Command: "think",
					CommandInvoker: _ => ValueTask.FromResult(new Option<CallState>(new None())),
					Switches: [],
					Arguments: [],
					Executor: _one,
					Enactor: _one,
					Caller: _one,
					Handle: 1
				));
		}
	}
	
	public IMUSHCodeParser CommandParser
	{
		get
		{
			var integrationServer = _server!;
			return new MUSHCodeParser(
				integrationServer.Services.GetRequiredService<ILogger<MUSHCodeParser>>(),
				integrationServer.Services.GetRequiredService<LibraryService<string, FunctionDefinition>>(),
				integrationServer.Services.GetRequiredService<LibraryService<string, CommandDefinition>>(),
				integrationServer.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>(),
				integrationServer.Services,
				state: new ParserState(
					Registers: new([[]]),
					IterationRegisters: [],
					RegexRegisters: [],
					ExecutionStack: [],
					EnvironmentRegisters: [],
					CurrentEvaluation: null,
					ParserFunctionDepth: 0,
					Function: null,
					Command: null,
					CommandInvoker: _ => ValueTask.FromResult(new Option<CallState>(new None())),
					Switches: [],
					Arguments: [],
					Executor: _one,
					Enactor: _one,
					Caller: _one,
					Handle: 1
				));
		}
	}
	
	public async Task InitializeAsync()
	{
		var log = new LoggerConfiguration()
			.Enrich.FromLogContext()
			.WriteTo.Console(theme: AnsiConsoleTheme.Code)
			.MinimumLevel.Debug()
			.CreateLogger();
		
		Log.Logger = log;
		
		var config = new ArangoConfiguration
		{
			ConnectionString = $"Server={ArangoDbTestServer.Instance.GetTransportAddress()};User=root;Realm=;Password=password;",
			Serializer = new ArangoJsonSerializer(new ArangoJsonDefaultPolicy())
		};

		var configFile = Path.Join(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst");

		var prometheusUrl = $"http://localhost:{PrometheusTestServer.Instance.GetMappedPublicPort(9090)}";

		var redisPort = RedisTestServer.Instance.GetMappedPublicPort(6379);
		var redisConnection = $"localhost:{redisPort}";
		Environment.SetEnvironmentVariable("REDIS_CONNECTION", redisConnection);

		var kafkaHost = RedPandaTestServer.Instance.GetBootstrapAddress();
		Environment.SetEnvironmentVariable("KAFKA_HOST", kafkaHost);

		await CreateKafkaTopicsAsync(kafkaHost);

		_server = new TestWebApplicationBuilderFactory<SharpMUSH.Server.Program>(
			MySqlTestServer.Instance.GetConnectionString(), 
			configFile,
			Substitute.For<INotifyService>(),
			prometheusUrl);

		var provider = _server.Services;
		var connectionService = provider.GetRequiredService<IConnectionService>();
		var databaseService = provider.GetRequiredService<ISharpDatabase>();
		
		// Migrate the database, which ensures we have a #1 object to bind to.
		await databaseService.Migrate();

		var realOne = await databaseService.GetObjectNodeAsync(new DBRef(1));
		_one = realOne.Object()!.DBRef;
		await connectionService.Register(1, "localhost", "locahost","test", _ => ValueTask.CompletedTask,  _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		await connectionService.Bind(1, _one);
		
		var schedulerFactory = provider.GetRequiredService<ISchedulerFactory>();
		var scheduler = await schedulerFactory.GetScheduler();
		if (!scheduler.IsStarted)
		{
			await scheduler.Start();
		}
	}

	private static async Task CreateKafkaTopicsAsync(string bootstrapServers)
	{
		// Format can be: "//127.0.0.1:9092/", "kafka://127.0.0.1:9092", or "127.0.0.1:9092"
		var cleanedAddress = bootstrapServers;
		
		if (cleanedAddress.Contains("://"))
		{
			cleanedAddress = cleanedAddress.Substring(cleanedAddress.IndexOf("://") + 3);
		}
		
		cleanedAddress = cleanedAddress.TrimStart('/');
		cleanedAddress = cleanedAddress.TrimEnd('/');
		
		var config = new AdminClientConfig
		{
			BootstrapServers = cleanedAddress,
			SocketTimeoutMs = 10000,
			ApiVersionRequestTimeoutMs = 10000
		};

		using var adminClient = new AdminClientBuilder(config).Build();

		var topics = new List<string>
		{
			"telnet-input",
			"telnet-output",
			"telnet-prompt",
			"websocket-input",
			"websocket-output",
			"websocket-prompt"
		};

		var topicSpecifications = topics.Select(topic => new TopicSpecification
		{
			Name = topic,
			NumPartitions = 1,
			ReplicationFactor = 1
		}).ToList();

		try
		{
			await adminClient.CreateTopicsAsync(topicSpecifications);
			
			await Task.Delay(2000);
		}
		catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists || r.Error.Code == ErrorCode.NoError))
		{
			// Topics already exist or were created successfully, which is fine
		}
	}
}