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

public class WebAppFactory : IAsyncInitializer, IAsyncDisposable
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
					Handle: 1,
					CallDepth: new InvocationCounter(),
					FunctionRecursionDepths: new Dictionary<string, int>(),
					TotalInvocations: new InvocationCounter(),
					LimitExceeded: new LimitExceededFlag()
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
					Handle: 1,
					CallDepth: new InvocationCounter(),
					FunctionRecursionDepths: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
					TotalInvocations: new InvocationCounter(),
					LimitExceeded: new LimitExceededFlag()
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

	public async ValueTask DisposeAsync()
	{
		// Output telemetry summary before disposing
		try
		{
			await OutputTelemetrySummaryAsync();
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"Error outputting telemetry summary: {ex.Message}");
		}
		
		// Shutdown the Quartz scheduler gracefully with a timeout
		if (_server?.Services != null)
		{
			var schedulerFactory = _server.Services.GetService<ISchedulerFactory>();
			if (schedulerFactory != null)
			{
				var scheduler = await schedulerFactory.GetScheduler();
				if (scheduler.IsStarted)
				{
					// Force shutdown after 5 seconds if jobs don't complete
					using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
					try
					{
						await scheduler.Shutdown(waitForJobsToComplete: false, cts.Token);
					}
					catch (OperationCanceledException)
					{
						// Timeout occurred, forcefully stop
					}
				}
			}
		}
		
		GC.SuppressFinalize(this);
	}

	private async Task OutputTelemetrySummaryAsync()
	{
		try
		{
			if (_server?.Services == null)
			{
				return;
			}

			var prometheusService = _server.Services.GetService<IPrometheusQueryService>();
			if (prometheusService == null)
			{
				return;
			}

			// Use Console.Error to ensure output is visible even if stdout is redirected
			Console.Error.WriteLine();
			Console.Error.WriteLine("═══════════════════════════════════════════════════════════════");
			Console.Error.WriteLine("                  TEST SESSION TELEMETRY SUMMARY");
			Console.Error.WriteLine("═══════════════════════════════════════════════════════════════");
			Console.Error.WriteLine();

			// Get health status
			var healthStatus = await prometheusService.GetHealthStatusAsync();
			if (healthStatus.Count > 0)
			{
				Console.Error.WriteLine("┌─ Health Status");
				foreach (var (service, status) in healthStatus)
				{
					var statusText = status == 1 ? "✓ Healthy" : "✗ Unhealthy";
					Console.Error.WriteLine($"│  {service,-20}: {statusText}");
				}
				Console.Error.WriteLine();
			}

			// Get connection metrics
			var (activeConnections, loggedInPlayers) = await prometheusService.GetConnectionMetricsAsync();
			Console.Error.WriteLine("┌─ Connection Metrics");
			Console.Error.WriteLine($"│  Active Connections    : {activeConnections}");
			Console.Error.WriteLine($"│  Logged In Players     : {loggedInPlayers}");
			Console.Error.WriteLine();

			// Get most called functions (test session duration)
			var mostCalledFunctions = await prometheusService.GetMostCalledFunctionsAsync("1h", 10);
			if (mostCalledFunctions.Count > 0)
			{
				Console.Error.WriteLine("┌─ Most Called Functions (Top 10)");
				Console.Error.WriteLine("│  Function                    Calls/sec");
				Console.Error.WriteLine("│  ────────────────────────────────────────");
				foreach (var (functionName, callsPerSecond) in mostCalledFunctions)
				{
					Console.Error.WriteLine($"│  {functionName,-28} {callsPerSecond,9:F3}");
				}
				Console.Error.WriteLine();
			}

			// Get slowest functions
			var slowestFunctions = await prometheusService.GetSlowestFunctionsAsync("1h", 10);
			if (slowestFunctions.Count > 0)
			{
				Console.Error.WriteLine("┌─ Slowest Functions (Top 10)");
				Console.Error.WriteLine("│  Function                    Avg Time (ms)");
				Console.Error.WriteLine("│  ────────────────────────────────────────────");
				foreach (var (functionName, avgDuration) in slowestFunctions)
				{
					Console.Error.WriteLine($"│  {functionName,-28} {avgDuration,13:F3}");
				}
				Console.Error.WriteLine();
			}

			// Get most called commands
			var mostCalledCommands = await prometheusService.GetMostCalledCommandsAsync("1h", 10);
			if (mostCalledCommands.Count > 0)
			{
				Console.Error.WriteLine("┌─ Most Called Commands (Top 10)");
				Console.Error.WriteLine("│  Command                     Calls/sec");
				Console.Error.WriteLine("│  ────────────────────────────────────────");
				foreach (var (commandName, callsPerSecond) in mostCalledCommands)
				{
					Console.Error.WriteLine($"│  {commandName,-28} {callsPerSecond,9:F3}");
				}
				Console.Error.WriteLine();
			}

			// Get slowest commands
			var slowestCommands = await prometheusService.GetSlowestCommandsAsync("1h", 10);
			if (slowestCommands.Count > 0)
			{
				Console.Error.WriteLine("┌─ Slowest Commands (Top 10)");
				Console.Error.WriteLine("│  Command                     Avg Time (ms)");
				Console.Error.WriteLine("│  ────────────────────────────────────────────");
				foreach (var (commandName, avgDuration) in slowestCommands)
				{
					Console.Error.WriteLine($"│  {commandName,-28} {avgDuration,13:F3}");
				}
				Console.Error.WriteLine();
			}

			Console.Error.WriteLine("═══════════════════════════════════════════════════════════════");
			Console.Error.WriteLine("                    END TELEMETRY SUMMARY");
			Console.Error.WriteLine("═══════════════════════════════════════════════════════════════");
			Console.Error.WriteLine();
		}
		catch (Exception ex)
		{
			// Gracefully handle any errors - don't let telemetry reporting break test cleanup
			Console.Error.WriteLine($"Note: Unable to retrieve telemetry summary: {ex.Message}");
		}
	}
}