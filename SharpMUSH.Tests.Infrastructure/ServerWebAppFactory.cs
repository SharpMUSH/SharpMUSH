using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Quartz;
using Serilog;
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
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

public class ServerWebAppFactory : IAsyncInitializer, IAsyncDisposable
{
	[ClassDataSource<DockerNetwork>(Shared = SharedType.PerTestSession)]
	public required DockerNetwork DockerNetwork { get; init; }

	[ClassDataSource<SurrealDbTestServer>(Shared = SharedType.PerTestSession)]
	public required SurrealDbTestServer SurrealDbTestServer { get; init; }

	[ClassDataSource<NatsTestServer>(Shared = SharedType.PerTestSession)]
	public required NatsTestServer NatsTestServer { get; init; }

	[ClassDataSource<MySqlTestServer>(Shared = SharedType.PerTestSession)]
	public required MySqlTestServer MySqlTestServer { get; init; }

	// Shared by every host variant; TUnit disposes storage after all consuming hosts.
	[ClassDataSource<TestDatabaseStorage>(Shared = SharedType.PerTestSession)]
	public required TestDatabaseStorage DatabaseStorage { get; init; }

	/// <summary>Integration fixtures can retain the production sender/perception pipeline.</summary>
	protected virtual bool UseRealNotifications => false;

	/// <summary>Secondary session hosts reuse the primary host's database and cache.</summary>
	protected virtual IServiceProvider? SharedWorldServices => null;

	public IServiceProvider Services => _server!.Services;

	/// <summary>
	/// Returns an <see cref="HttpClient"/> that targets the in-process test server directly,
	/// bypassing real network I/O. Use this for controller-level HTTP integration tests.
	/// </summary>
	public HttpClient CreateHttpClient()
	{
		HttpClient client;
		// WebApplicationFactory records every client it creates in a plain List<T> and disposes them
		// on teardown. Parallel tests adding to it concurrently leave null slots that fault DisposeAsync.
		lock (_createClientLock)
			client = _server!.CreateClient();
		// Exercise the endpoint directly; HTTP-to-HTTPS redirect behavior has dedicated coverage.
		client.BaseAddress = new Uri("https://localhost");
		return client;
	}
	private ServerTestWebApplicationBuilderFactory<SharpMUSH.Server.Program>? _server;
	private readonly Lock _createClientLock = new();
	private DBRef _one;

	/// <summary>
	/// The DBRef of the executor bound to connection handle 1 (the God player).
	/// Use this in test assertions instead of Arg.Any&lt;DBRef&gt;() to verify that
	/// notifications are sent to the correct specific recipient.
	/// </summary>
	public DBRef ExecutorDBRef => _one;

	/// <summary>
	/// What the notify substitute was asked to deliver, bucketed by recipient. Read this instead of
	/// enumerating <c>ReceivedCalls()</c> when a test needs the text of what was said: the substitute is
	/// one singleton shared by every test in the session, and NSubstitute does not support enumerating it
	/// while other threads are still recording calls into it.
	/// </summary>
	public TestHelpers.NotificationRecorder Notifications { get; } = new();

	// Metrics collected via MeterListener — static so they persist across all factory instances
	// and can be written from the ProcessExit handler regardless of disposal order.
	private MeterListener? _meterListener;
	private static readonly ConcurrentDictionary<string, ConcurrentBag<double>> _functionDurations = new(StringComparer.OrdinalIgnoreCase);
	private static readonly ConcurrentDictionary<string, ConcurrentBag<double>> _commandDurations = new(StringComparer.OrdinalIgnoreCase);
	private static readonly ConcurrentDictionary<string, long> _connectionEventCounts = new(StringComparer.OrdinalIgnoreCase);

	static ServerWebAppFactory()
	{
		// Register once at process exit to write telemetry regardless of disposal order.
		// Keep the report after all host variants have contributed their final measurements.
		AppDomain.CurrentDomain.ProcessExit += (_, _) => WriteTelemetryFile();
	}

	protected string? _customSqlConnectionString;
	private readonly string _sqlPlatform;
	public ServerWebAppFactory() : this(null, "mysql")
	{
	}

	public ServerWebAppFactory(string? sqlConnectionString, string sqlPlatform = "mysql")
	{
		_customSqlConnectionString = sqlConnectionString;
		_sqlPlatform = sqlPlatform;
	}

	/// <summary>
	/// The one <see cref="MUSHCodeParser"/> the four accessors below hand out. They differed only in the
	/// executor, the connection handle, and whether <c>Command</c> was set — three parameters against
	/// thirty-five lines of identical <see cref="ParserState"/>, copied four times.
	/// </summary>
	/// <param name="executor">Executor, enactor and caller. Tests that need a mortal pass one here.</param>
	/// <param name="handle">Connection handle; only the command parsers bind a real one.</param>
	/// <param name="command">
	/// <c>"think"</c> for the function parsers and <see langword="null"/> for the command parsers, which
	/// set their own as they dispatch.
	/// </param>
	/// <param name="commands">The command table; the host's own unless a test supplies one.</param>
	private IMUSHCodeParser BuildParser(DBRef executor, long handle, string? command,
		LibraryService<string, CommandDefinition>? commands = null)
	{
		var integrationServer = _server!;
		return new MUSHCodeParser(
			integrationServer.Services.GetRequiredService<ILogger<MUSHCodeParser>>(),
			integrationServer.Services.GetRequiredService<LibraryService<string, FunctionDefinition>>(),
			commands ?? integrationServer.Services.GetRequiredService<LibraryService<string, CommandDefinition>>(),
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
				Command: command,
				CommandInvoker: _ => ValueTask.FromResult(new Option<CallState>(new None())),
				Switches: [],
				Arguments: [],
				Executor: executor,
				Enactor: executor,
				Caller: executor,
				Handle: handle,
				CallDepth: new InvocationCounter(),
				FunctionRecursionDepths: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
				TotalInvocations: new InvocationCounter(),
				LimitExceeded: new LimitExceededFlag(),
				Flags: ParserStateFlags.DirectInput
			)
			{
				MoveDepth = new InvocationCounter()
			});
	}

	/// <summary>Evaluates functions as God.</summary>
	public IMUSHCodeParser FunctionParser => BuildParser(_one, handle: 1, command: "think");

	/// <summary>Runs commands as God.</summary>
	public IMUSHCodeParser CommandParser => BuildParser(_one, handle: 1, command: null);

	/// <summary>
	/// <see cref="FunctionParser"/> with a chosen executor. Permission tests need to evaluate a function
	/// AS a mortal: a channel function gated at the command surface but open to mortal softcode is only
	/// testable this way.
	/// </summary>
	public IMUSHCodeParser FunctionParserFor(DBRef executor) => BuildParser(executor, handle: 1, command: "think");

	/// <summary><see cref="CommandParser"/> with a chosen executor and its bound connection handle.</summary>
	public IMUSHCodeParser CommandParserFor(DBRef executor, long handle) => BuildParser(executor, handle, command: null);

	/// <summary>
	/// <see cref="CommandParserFor"/> dispatching through <paramref name="commands"/> instead of the host's
	/// own table: a command whose effect reaches the whole session, run against services a test controls.
	/// </summary>
	public IMUSHCodeParser CommandParserWith(LibraryService<string, CommandDefinition> commands, DBRef executor, long handle)
		=> BuildParser(executor, handle, command: null, commands);

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
		Log.Logger = TestDiagnostics.CreateLogger();

		var dbProviderStr = Environment.GetEnvironmentVariable("SHARPMUSH_DATABASE_PROVIDER");
		var useSurrealDb = string.Equals(dbProviderStr, "surrealdb", StringComparison.OrdinalIgnoreCase);

		if (useSurrealDb)
		{
			Environment.SetEnvironmentVariable("SHARPMUSH_DATABASE_PROVIDER", "surrealdb");
			// Tests run the embedded in-memory engine for isolation/speed; production defaults to a
			// durable file-backed RocksDB store (see Startup). Default to mem:// unless a test explicitly
			// sets the endpoint (e.g. to smoke-test the rocksdb:// path), or SurrealDbTestServer is
			// opted into via SHARPMUSH_SURREALDB_USE_TESTCONTAINER - a real server process instead of
			// the embedded engine sharing the test host's own process/thread pool.
			if (Environment.GetEnvironmentVariable("SHARPMUSH_SURREALDB_ENDPOINT") is null)
			{
				Environment.SetEnvironmentVariable("SHARPMUSH_SURREALDB_ENDPOINT",
					SurrealDbTestServer.IsEnabled ? SurrealDbTestServer.Endpoint : "mem://");
			}
		}

		var configFile = Path.Join(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst");

		var natsPort = NatsTestServer.Instance.GetMappedPublicPort(4222);
		var natsUrl = $"nats://localhost:{natsPort}";
		Environment.SetEnvironmentVariable("NATS_URL", natsUrl);

		_server = new ServerTestWebApplicationBuilderFactory<SharpMUSH.Server.Program>(
			_customSqlConnectionString ?? MySqlTestServer.Instance.GetConnectionString(),
			configFile,
			UseRealNotifications ? null : TestHelpers.CreateNotifyServiceSubstitute(Notifications),
			_sqlPlatform, SharedWorldServices);

		var provider = _server.Services;
		var connectionService = provider.GetRequiredService<IConnectionService>();
		var databaseService = provider.GetRequiredService<ISharpDatabase>();

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

	public async ValueTask DisposeAsync()
	{
		_meterListener?.Dispose();
		// The factory stops hosted services (including Quartz) and disposes its service provider.
		// Its shared TestDatabaseStorage dependency outlives every host using that database.
		if (Interlocked.Exchange(ref _server, null) is { } server)
			await server.DisposeAsync();
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
	/// runs synchronously after fixture shutdown, including measurements from all host variants.
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
			using var logger = TestDiagnostics.CreateLogger();
			logger.Fatal(ex, "Failed to write requested test telemetry summary to {OutputPath}", outputPath);
		}
	}
}
