using System.Collections.Concurrent;
using System.Text;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Core.Arango;
using Core.Arango.Migration;
using Core.Arango.Serialization.Json;
using Mediator;
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

/// <summary>
/// Per-class test factory that provides isolated test infrastructure for each test class.
/// Each test class gets:
/// - Its own ArangoDB database (on the shared container)
/// - Its own NotifyService mock (no shared state issues)
/// - Its own Parser instances (FunctionParser and CommandParser)
/// - Access to the shared TestContainers (5 containers total for entire test session)
/// </summary>
public class TestClassFactory : IAsyncInitializer, IAsyncDisposable
{
	// ===== INJECTED TESTCONTAINERS (PerTestSession - Shared across all test classes) =====
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

	// ===== CLASS-SPECIFIC RESOURCES =====
	private TestWebApplicationBuilderFactory<SharpMUSH.Server.Program>? _server;
	private INotifyService? _notifyServiceMock;
	private DBRef _one;
	private static int _databaseCounter = 0;
	private static int _handleCounter = 0;
	private long _connectionHandle;

	/// <summary>
	/// Service provider for this test class. Use this to get services like IMediator, IConnectionService, etc.
	/// </summary>
	public IServiceProvider Services => _server!.Services;

	/// <summary>
	/// NotifyService mock for this test class. Clear received calls between tests with ClearReceivedCalls().
	/// </summary>
	public INotifyService NotifyService => _notifyServiceMock!;

	/// <summary>
	/// Function parser for this test class. Use this to parse function calls.
	/// </summary>
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
					Handle: _connectionHandle,
					CallDepth: new InvocationCounter(),
					FunctionRecursionDepths: new Dictionary<string, int>(),
					TotalInvocations: new InvocationCounter(),
					LimitExceeded: new LimitExceededFlag()
				));
		}
	}

	/// <summary>
	/// Command parser for this test class. Use this to parse command calls.
	/// </summary>
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
					Handle: _connectionHandle,
					CallDepth: new InvocationCounter(),
					FunctionRecursionDepths: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
					TotalInvocations: new InvocationCounter(),
					LimitExceeded: new LimitExceededFlag()
				));
		}
	}

	/// <summary>
	/// The unique database name for this test class.
	/// Format: SharpMUSH_Test_{Counter}_{ShortGuid}
	/// </summary>
	public string DatabaseName { get; private set; } = string.Empty;

	/// <summary>
	/// Initialize the test class factory. This runs once per test class.
	/// Creates a unique database, runs migrations, and sets up test infrastructure.
	/// </summary>
	public async Task InitializeAsync()
	{
		// Configure logging
		var log = new LoggerConfiguration()
			.Enrich.FromLogContext()
			.WriteTo.Console(theme: AnsiConsoleTheme.Code)
			.MinimumLevel.Debug()
			.CreateLogger();

		Log.Logger = log;

		// Generate unique database name
		DatabaseName = GenerateDatabaseName();
		Console.WriteLine($"[TestClassFactory] Initializing test class with database: {DatabaseName}");

		// Configure ArangoDB connection
		var config = new ArangoConfiguration
		{
			ConnectionString = $"Server={ArangoDbTestServer.Instance.GetTransportAddress()};User=root;Realm=;Password=password;",
			Serializer = new ArangoJsonSerializer(new ArangoJsonDefaultPolicy())
		};

		// Create ArangoDB context for database creation
		var arangoContext = new ArangoContext(config);
		var handle = new ArangoHandle(DatabaseName);

		// Create the database if it doesn't exist
		if (!await arangoContext.Database.ExistAsync(handle))
		{
			await arangoContext.Database.CreateAsync(handle);
			Console.WriteLine($"[TestClassFactory] Created database: {DatabaseName}");
		}

		// Run database migration
		var migrator = new ArangoMigrator(arangoContext)
		{
			HistoryCollection = "MigrationHistory"
		};
		migrator.AddMigrations(typeof(SharpMUSH.Database.ArangoDB.ArangoDatabase).Assembly);
		await migrator.UpgradeAsync(handle);
		Console.WriteLine($"[TestClassFactory] Migration completed for database: {DatabaseName}");

		// Set up configuration file path
		var configFile = Path.Join(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst");

		// Get Prometheus URL
		var prometheusUrl = $"http://localhost:{PrometheusTestServer.Instance.GetMappedPublicPort(9090)}";

		// Set up Redis connection
		var redisPort = RedisTestServer.Instance.GetMappedPublicPort(6379);
		var redisConnection = $"localhost:{redisPort}";
		Environment.SetEnvironmentVariable("REDIS_CONNECTION", redisConnection);

		// Set up Kafka connection
		var kafkaHost = RedPandaTestServer.Instance.GetBootstrapAddress();
		Environment.SetEnvironmentVariable("KAFKA_HOST", kafkaHost);

		// Create Kafka topics
		await CreateKafkaTopicsAsync(kafkaHost);

		// Create class-specific NotifyService mock
		_notifyServiceMock = Substitute.For<INotifyService>();

		// Create TestWebApplicationBuilderFactory with class-specific database
		_server = new TestWebApplicationBuilderFactory<SharpMUSH.Server.Program>(
			MySqlTestServer.Instance.GetConnectionString(),
			configFile,
			_notifyServiceMock,
			prometheusUrl,
			DatabaseName); // Pass the unique database name

		var provider = _server.Services;
		var connectionService = provider.GetRequiredService<IConnectionService>();
		var databaseService = provider.GetRequiredService<ISharpDatabase>();

		// Set the current Commands and Functions instances for this async context
		// This ensures static command/function methods access the correct instance from this DI container
		// Note: We no longer set current Commands/Functions instances here
		// CommandParse will set them automatically when commands/functions execute
		// This ensures the correct scoped instance is used on the execution thread

		// Migrate the database (this will use the class-specific database)
		await databaseService.Migrate();

		// Get the #1 object to bind to
		var realOne = await databaseService.GetObjectNodeAsync(new DBRef(1));
		_one = realOne.Object()!.DBRef;

		// Generate unique connection handle for this test class to avoid conflicts
		_connectionHandle = Interlocked.Increment(ref _handleCounter);

		// Register and bind connection with unique handle
		await connectionService.Register(_connectionHandle, "localhost", "localhost", "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		await connectionService.Bind(_connectionHandle, _one);

		// Start Quartz scheduler
		var schedulerFactory = provider.GetRequiredService<ISchedulerFactory>();
		var scheduler = await schedulerFactory.GetScheduler();
		if (!scheduler.IsStarted)
		{
			await scheduler.Start();
		}

		Console.WriteLine($"[TestClassFactory] Initialization complete for database: {DatabaseName}");
	}

	/// <summary>
	/// Generates a unique database name for this test class.
	/// Format: SharpMUSH_Test_{Counter}_{ShortGuid}
	/// Uses a counter and short GUID to ensure uniqueness across test classes.
	/// </summary>
	private string GenerateDatabaseName()
	{
		var counter = Interlocked.Increment(ref _databaseCounter);
		var shortGuid = Guid.NewGuid().ToString("N")[..8];
		return $"SharpMUSH_Test_{counter}_{shortGuid}";
	}

	/// <summary>
	/// Creates Kafka topics for the test session.
	/// This is safe to call multiple times (idempotent).
	/// </summary>
	private static async Task CreateKafkaTopicsAsync(string bootstrapServers)
	{
		// Format can be: "//127.0.0.1:9092/", "kafka://127.0.0.1:9092", or "127.0.0.1:9092"
		var cleanedAddress = bootstrapServers;

		if (cleanedAddress.Contains("://"))
		{
			cleanedAddress = cleanedAddress[(cleanedAddress.IndexOf("://", StringComparison.Ordinal) + 3)..];
		}

		cleanedAddress = cleanedAddress.TrimStart('/').TrimEnd('/');

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

	/// <summary>
	/// Dispose the test class factory. Cleans up resources.
	/// </summary>
	public async ValueTask DisposeAsync()
	{
		Console.WriteLine($"[TestClassFactory] Disposing test class with database: {DatabaseName}");

		// Disconnect the test connection to clean up ConnectionService state
		if (_server?.Services != null)
		{
			try
			{
				var connectionService = _server.Services.GetService<IConnectionService>();
				if (connectionService != null)
				{
					await connectionService.Disconnect(_connectionHandle);
					Console.WriteLine($"[TestClassFactory] Disconnected connection handle: {_connectionHandle}");
				}
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"Error disconnecting connection: {ex.Message}");
			}
		}

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

		// Note: We intentionally do NOT delete the test database here
		// This allows for debugging failed tests by inspecting the database state
		// The database will be cleaned up when the ArangoDB container is disposed at the end of the test session

		Console.WriteLine($"[TestClassFactory] Disposal complete for database: {DatabaseName}");
		GC.SuppressFinalize(this);
	}

	private async Task OutputTelemetrySummaryAsync()
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

		await TelemetryOutputHelper.OutputTelemetrySummaryAsync(prometheusService);
	}
}
