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
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text;
using TUnit.AspNetCore;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

public class ServerWebAppFactory : TestWebApplicationFactory<SharpMUSH.Server.Program>, IAsyncInitializer, IAsyncDisposable
{
	[ClassDataSource<DockerNetwork>(Shared = SharedType.PerTestSession)]
	public required DockerNetwork DockerNetwork { get; init; }

	[ClassDataSource<ArangoDbTestServer>(Shared = SharedType.PerTestSession)]
	public required ArangoDbTestServer ArangoDbTestServer { get; init; }

	[ClassDataSource<MemgraphTestServer>(Shared = SharedType.PerTestSession)]
	public required MemgraphTestServer MemgraphTestServer { get; init; }

	[ClassDataSource<NatsTestServer>(Shared = SharedType.PerTestSession)]
	public required NatsTestServer NatsTestServer { get; init; }

	[ClassDataSource<MySqlTestServer>(Shared = SharedType.PerTestSession)]
	public required MySqlTestServer MySqlTestServer { get; init; }

	public new IServiceProvider Services => _server!.Services;
	private ServerTestWebApplicationBuilderFactory<SharpMUSH.Server.Program>? _server;
	private DBRef _one;

	/// <summary>
	/// The DBRef of the executor bound to connection handle 1 (the God player).
	/// Use this in test assertions instead of Arg.Any&lt;DBRef&gt;() to verify that
	/// notifications are sent to the correct specific recipient.
	/// </summary>
	public DBRef ExecutorDBRef => _one;

	// Metrics collected via MeterListener — static so they persist across all factory instances
	// and can be written from the ProcessExit handler regardless of disposal order.
	private MeterListener? _meterListener;
	private static readonly ConcurrentDictionary<string, ConcurrentBag<double>> _functionDurations = new(StringComparer.OrdinalIgnoreCase);
	private static readonly ConcurrentDictionary<string, ConcurrentBag<double>> _commandDurations = new(StringComparer.OrdinalIgnoreCase);
	private static readonly ConcurrentDictionary<string, long> _connectionEventCounts = new(StringComparer.OrdinalIgnoreCase);

	static ServerWebAppFactory()
	{
		// Register once at process exit to write telemetry regardless of disposal order.
		// AppDomain.ProcessExit fires reliably when the dotnet process exits normally,
		// bypassing any IAsyncDisposable interface-dispatch issues in the test framework.
		AppDomain.CurrentDomain.ProcessExit += (_, _) => WriteTelemetryFile();
	}

	// Optional parameters for custom SQL connection
	protected string? _customSqlConnectionString;
	private readonly string _sqlPlatform;
	private readonly string? _customDatabaseName;

	public ServerWebAppFactory() : this(null, null, "mysql")
	{
	}

	public ServerWebAppFactory(string? sqlConnectionString, string? databaseName, string sqlPlatform = "mysql")
	{
		_customSqlConnectionString = sqlConnectionString;
		_customDatabaseName = databaseName;
		_sqlPlatform = sqlPlatform;
	}

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
					SwitchStack: [],
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
					LimitExceeded: new LimitExceededFlag(),
					Flags: ParserStateFlags.DirectInput
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
					SwitchStack: [],
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
					LimitExceeded: new LimitExceededFlag(),
					Flags: ParserStateFlags.DirectInput
				));
		}
	}

	public virtual async Task InitializeAsync()
	{
		// Set up a MeterListener to collect SharpMUSH metrics synchronously.
		// This is more reliable in tests than MeterProvider.ForceFlush() which
		// depends on PeriodicExportingMetricReader's background thread.
		_meterListener = new MeterListener();
		_meterListener.InstrumentPublished = (instrument, listener) =>
		{
			if (instrument.Meter.Name == "SharpMUSH")
				listener.EnableMeasurementEvents(instrument, null);
		};
		_meterListener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
		{
			if (instrument.Name == "sharpmush.function.invocation.duration")
				_functionDurations.GetOrAdd(GetTagValue(tags, "function.name"), _ => []).Add(measurement);
			else if (instrument.Name == "sharpmush.command.invocation.duration")
				_commandDurations.GetOrAdd(GetTagValue(tags, "command.name"), _ => []).Add(measurement);
		});
		_meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
		{
			if (instrument.Name == "sharpmush.connection.events")
				_connectionEventCounts.AddOrUpdate(GetTagValue(tags, "event.type"), measurement, (_, old) => old + measurement);
		});
		_meterListener.Start();
		var logConfig = new LoggerConfiguration()
			.Enrich.FromLogContext()
			.MinimumLevel.Verbose();

		// Only write to console if explicitly enabled via environment variable
		var enableConsoleLogging = Environment.GetEnvironmentVariable("SHARPMUSH_ENABLE_TEST_CONSOLE_LOGGING");
		var isConsoleEnabled = !string.IsNullOrEmpty(enableConsoleLogging) &&
													 (enableConsoleLogging.Equals("true", StringComparison.OrdinalIgnoreCase) || enableConsoleLogging == "1");

		if (isConsoleEnabled)
		{
			logConfig.WriteTo.Console(theme: AnsiConsoleTheme.Code);
		}

		var log = logConfig.CreateLogger();
		Log.Logger = log;

		// Determine database provider from environment variable
		var dbProviderStr = Environment.GetEnvironmentVariable("SHARPMUSH_DATABASE_PROVIDER");
		var useMemgraph = string.Equals(dbProviderStr, "memgraph", StringComparison.OrdinalIgnoreCase);

		if (useMemgraph)
		{
			Environment.SetEnvironmentVariable("SHARPMUSH_DATABASE_PROVIDER", "memgraph");
			Environment.SetEnvironmentVariable("MEMGRAPH_URI", MemgraphTestServer.BoltUri);
		}

		var configFile = Path.Join(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst");

		var natsPort = NatsTestServer.Instance.GetMappedPublicPort(4222);
		var natsUrl = $"nats://localhost:{natsPort}";
		Environment.SetEnvironmentVariable("NATS_URL", natsUrl);

		_server = new ServerTestWebApplicationBuilderFactory<SharpMUSH.Server.Program>(
			_customSqlConnectionString ?? MySqlTestServer.Instance.GetConnectionString(),
			configFile,
			Substitute.For<INotifyService>(),
			_customDatabaseName,
			_sqlPlatform);

		var provider = _server.Services;
		var connectionService = provider.GetRequiredService<IConnectionService>();
		var databaseService = provider.GetRequiredService<ISharpDatabase>();

		// Migrate the database, which ensures we have a #1 object to bind to.
		await databaseService.Migrate();

		var realOne = await databaseService.GetObjectNodeAsync(new DBRef(1));
		_one = realOne.Object()!.DBRef;
		await connectionService.Register(1, "localhost", "locahost", "test", _ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		await connectionService.Bind(1, _one);

		var schedulerFactory = provider.GetRequiredService<ISchedulerFactory>();
		var scheduler = await schedulerFactory.GetScheduler();
		if (!scheduler.IsStarted)
		{
			await scheduler.Start();
		}
	}

	public new async ValueTask DisposeAsync()
	{
		_meterListener?.Dispose();

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

	private static string GetTagValue(ReadOnlySpan<KeyValuePair<string, object?>> tags, string key)
	{
		foreach (var tag in tags)
			if (tag.Key == key) return tag.Value?.ToString() ?? "unknown";
		return "unknown";
	}

	/// <summary>
	/// Writes the telemetry summary file. Called from <see cref="AppDomain.ProcessExit"/> so it
	/// runs synchronously and is guaranteed to execute even if <see cref="DisposeAsync"/> is
	/// bypassed by TUnit's interface-based disposal.
	/// </summary>
	private static void WriteTelemetryFile()
	{
		var enableTelemetry = Environment.GetEnvironmentVariable("SHARPMUSH_ENABLE_TEST_TELEMETRY");
		if (string.IsNullOrEmpty(enableTelemetry) ||
			(!enableTelemetry.Equals("true", StringComparison.OrdinalIgnoreCase) && enableTelemetry != "1"))
			return;

		var outputPath = Environment.GetEnvironmentVariable("SHARPMUSH_TELEMETRY_OUTPUT_PATH") ?? "test-telemetry.md";

		try
		{
			var sb = new StringBuilder();
			sb.AppendLine("## 📊 Test Telemetry Summary");
			sb.AppendLine();

			if (_connectionEventCounts.Count > 0)
			{
				sb.AppendLine("### 🔌 Connection Events");
				sb.AppendLine();
				sb.AppendLine("| Event Type | Count |");
				sb.AppendLine("|------------|-------|");
				foreach (var (eventType, count) in _connectionEventCounts.OrderByDescending(x => x.Value))
					sb.AppendLine($"| {eventType} | {count} |");
				sb.AppendLine();
			}

			if (_functionDurations.Count > 0)
			{
				sb.AppendLine("### ⚡ Most Called Functions (Top 10)");
				sb.AppendLine();
				sb.AppendLine("| Function | Calls | Avg Duration (ms) |");
				sb.AppendLine("|----------|-------|-------------------|");
				foreach (var (name, count, avgMs) in _functionDurations
					.Select(kvp => (Name: kvp.Key, Count: kvp.Value.Count, AvgMs: kvp.Value.Average()))
					.OrderByDescending(x => x.Count).Take(10))
					sb.AppendLine($"| {name} | {count} | {avgMs:F2} |");
				sb.AppendLine();

				sb.AppendLine("### 🐌 Slowest Functions (Top 10 by Avg Duration)");
				sb.AppendLine();
				sb.AppendLine("| Function | Calls | Avg Duration (ms) |");
				sb.AppendLine("|----------|-------|-------------------|");
				foreach (var (name, count, avgMs) in _functionDurations
					.Select(kvp => (Name: kvp.Key, Count: kvp.Value.Count, AvgMs: kvp.Value.Average()))
					.OrderByDescending(x => x.AvgMs).Take(10))
					sb.AppendLine($"| {name} | {count} | {avgMs:F2} |");
				sb.AppendLine();
			}

			if (_commandDurations.Count > 0)
			{
				sb.AppendLine("### ⚡ Most Called Commands (Top 10)");
				sb.AppendLine();
				sb.AppendLine("| Command | Calls | Avg Duration (ms) |");
				sb.AppendLine("|---------|-------|-------------------|");
				foreach (var (name, count, avgMs) in _commandDurations
					.Select(kvp => (Name: kvp.Key, Count: kvp.Value.Count, AvgMs: kvp.Value.Average()))
					.OrderByDescending(x => x.Count).Take(10))
					sb.AppendLine($"| {name} | {count} | {avgMs:F2} |");
				sb.AppendLine();

				sb.AppendLine("### 🐌 Slowest Commands (Top 10 by Avg Duration)");
				sb.AppendLine();
				sb.AppendLine("| Command | Calls | Avg Duration (ms) |");
				sb.AppendLine("|---------|-------|-------------------|");
				foreach (var (name, count, avgMs) in _commandDurations
					.Select(kvp => (Name: kvp.Key, Count: kvp.Value.Count, AvgMs: kvp.Value.Average()))
					.OrderByDescending(x => x.AvgMs).Take(10))
					sb.AppendLine($"| {name} | {count} | {avgMs:F2} |");
				sb.AppendLine();
			}

			File.WriteAllText(outputPath, sb.ToString());
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[Telemetry] Error writing summary to '{outputPath}': {ex.Message}");
		}
	}
}