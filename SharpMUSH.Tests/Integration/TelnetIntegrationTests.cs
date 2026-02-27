using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MySqlConnector;
using NSubstitute;
using Quartz;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.ConnectionServer.ProtocolHandlers;
using SharpMUSH.Library;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TUnit.AspNetCore;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// TestWebApplicationFactory variant that configures the SharpMUSH Server
/// for integration testing without mocking <see cref="INotifyService"/>.
/// This allows the real notification path (connect.txt → NATS → TCP) to function.
/// </summary>
/// <param name="sqlConnectionString">MySQL connection string for the test database.</param>
/// <param name="configFile">Path to the mushcnf.dst test configuration file.</param>
/// <param name="natsUrl">
/// NATS URL of the shared test NATS server.  Set via
/// <see cref="Environment.SetEnvironmentVariable"/> inside
/// <see cref="ConfigureWebHost"/> so it is guaranteed to be in place before
/// <c>Program.Main()</c> calls <c>NatsStrategyProvider.GetStrategy()</c>.
/// </param>
internal class TelnetIntegrationServerBuilderFactory<TProgram>(
	string sqlConnectionString,
	string configFile,
	string natsUrl) :
	TestWebApplicationFactory<TProgram> where TProgram : class
{
	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		var logConfig = new LoggerConfiguration()
			.Enrich.FromLogContext()
			.MinimumLevel.Verbose();

		var enableConsole = Environment.GetEnvironmentVariable("SHARPMUSH_ENABLE_TEST_CONSOLE_LOGGING");
		if (!string.IsNullOrEmpty(enableConsole) &&
				(enableConsole.Equals("true", StringComparison.OrdinalIgnoreCase) || enableConsole == "1"))
		{
			logConfig.WriteTo.Console(theme: AnsiConsoleTheme.Code);
		}

		Log.Logger = logConfig.CreateLogger();

		// Point the Server at the shared NATS instance.
		// Setting the env var here (inside ConfigureWebHost) mirrors the approach used by
		// ConnectionServerTestWebApplicationBuilderFactory and ensures the value is in place
		// before Program.Main() calls NatsStrategyProvider.GetStrategy(), which reads it.
		Environment.SetEnvironmentVariable("NATS_URL", natsUrl);

		// Ensure colors.json exists in the test output directory (required by Server startup)
		var colorFile = Path.Combine(AppContext.BaseDirectory, "colors.json");
		if (!File.Exists(colorFile))
		{
			var temp = Path.Combine(Path.GetTempPath(), "colors.json");
			File.WriteAllText(temp, "{}");
			try { File.Copy(temp, colorFile, true); } catch { /* best-effort */ }
		}

		builder.ConfigureServices(sc =>
		{
			// Override SharpMUSH options to point at the test MySQL database
			var optionsSubstitute = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
			var optionsMonitor = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();
			var config = ReadPennMushConfig.Create(configFile);

			var csb = new MySqlConnectionStringBuilder(sqlConnectionString);
			var sqlHost = csb.Port > 0 ? $"{csb.Server}:{csb.Port}" : (csb.Server ?? "localhost");
			var sqlConfigOverride = config with
			{
				Net = config.Net with
				{
					SqlHost = sqlHost,
					SqlDatabase = csb.Database ?? "",
					SqlUsername = csb.UserID ?? "",
					SqlPassword = csb.Password ?? "",
					SqlPlatform = "mysql"
				}
			};

			optionsSubstitute.CurrentValue.Returns(config);
			optionsMonitor.CurrentValue.Returns(sqlConfigOverride);

			sc.RemoveAll<IOptionsWrapper<SharpMUSHOptions>>();
			sc.AddSingleton(optionsSubstitute);

			sc.RemoveAll<ISqlService>();
			sc.AddSingleton<ISqlService>(new SqlService(optionsMonitor));

			// INotifyService is intentionally NOT overridden here so the real
			// NotifyService (registered by Startup.cs) is used.  This allows
			// connect.txt content to flow via NATS to the TCP connection.
		});
	}
}

/// <summary>
/// Combined integration fixture that starts both the SharpMUSH Server and the
/// ConnectionServer with a dedicated, class-scoped NATS instance so that the
/// two services can communicate without interfering with the session-wide test
/// infrastructure used by other test classes.
/// </summary>
public class TelnetIntegrationFixture : IAsyncInitializer, IAsyncDisposable
{
	/// <summary>Dedicated NATS for this test class — not shared with session-wide tests.</summary>
	[ClassDataSource<NatsTestServer>(Shared = SharedType.PerClass)]
	public required NatsTestServer NatsTestServer { get; init; }

	/// <summary>Dedicated MySQL for this test class.</summary>
	[ClassDataSource<MySqlTestServer>(Shared = SharedType.PerClass)]
	public required MySqlTestServer MySqlTestServer { get; init; }

	/// <summary>Telnet port assigned to the ConnectionServer during initialisation.</summary>
	public int TelnetPort { get; private set; }

	private TelnetIntegrationServerBuilderFactory<SharpMUSH.Server.Program>? _serverFactory;
	private WebApplication? _connectionServerApp;

	public async Task InitializeAsync()
	{
		var natsPort = NatsTestServer.Instance.GetMappedPublicPort(4222);
		var natsUrl = $"nats://localhost:{natsPort}";

		// ── 1. Start the SharpMUSH Server (game engine) ──────────────────────
		// natsUrl is passed to the factory so it can be set inside ConfigureWebHost —
		// which runs immediately before Program.Main() — mirroring the approach used
		// by ConnectionServerTestWebApplicationBuilderFactory.
		var configFile = Path.Join(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst");
		_serverFactory = new TelnetIntegrationServerBuilderFactory<SharpMUSH.Server.Program>(
			MySqlTestServer.Instance.GetConnectionString(),
			configFile,
			natsUrl);

		// Accessing Services triggers the host build, which starts all hosted services
		// (including NatsJetStreamConsumerService that listens for ConnectionEstablishedMessage).
		var serverServices = _serverFactory.Services;

		var databaseService = serverServices.GetRequiredService<ISharpDatabase>();
		await databaseService.Migrate();

		var schedulerFactory = serverServices.GetRequiredService<ISchedulerFactory>();
		var scheduler = await schedulerFactory.GetScheduler();
		if (!scheduler.IsStarted) await scheduler.Start();

		// ── 2. Start the ConnectionServer (Kestrel TCP listener) ─────────────
		TelnetPort = FindFreePort();
		var httpPort = FindFreePort();

		var csArgs = new[]
		{
			$"--ConnectionServer:TelnetPort={TelnetPort}",
			$"--ConnectionServer:HttpPort={httpPort}"
		};

		_connectionServerApp = await SharpMUSH.ConnectionServer.Program.CreateHostBuilderAsync(csArgs, natsUrl);

		// Set up the same HTTP routes as Program.Main
		_connectionServerApp.UseWebSockets();
		var wsHandler = _connectionServerApp.Services.GetRequiredService<WebSocketServer>();
		_connectionServerApp.Map("/ws", wsHandler.HandleWebSocketAsync);
		_connectionServerApp.MapControllers();
		_connectionServerApp.MapGet("/", () => "SharpMUSH Connection Server");
		_connectionServerApp.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
		_connectionServerApp.MapGet("/ready", () => Results.Ok(new { status = "ready" }));
		_connectionServerApp.MapPrometheusScrapingEndpoint();

		await _connectionServerApp.StartAsync();

		// Allow NATS consumer subscriptions to settle before the test connects
		await Task.Delay(TimeSpan.FromSeconds(3));
	}

	public async ValueTask DisposeAsync()
	{
		if (_connectionServerApp != null)
		{
			await _connectionServerApp.StopAsync();
			await _connectionServerApp.DisposeAsync();
		}

		if (_serverFactory != null)
		{
			var schedulerFactory = _serverFactory.Services.GetService<ISchedulerFactory>();
			if (schedulerFactory != null)
			{
				var scheduler = await schedulerFactory.GetScheduler();
				if (scheduler.IsStarted)
				{
					using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
					try { await scheduler.Shutdown(waitForJobsToComplete: false, cts.Token); }
					catch (OperationCanceledException) { /* timeout during shutdown is OK */ }
				}
			}

			await _serverFactory.DisposeAsync();
		}

		GC.SuppressFinalize(this);
	}

	private static int FindFreePort()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		return port;
	}
}

/// <summary>
/// Full end-to-end integration tests for the telnet connection path.
/// </summary>
[NotInParallel]
public class TelnetIntegrationTests
{
	private const int ReceiveTimeoutMs = 20_000;
	private const int PollingIntervalMs = 200;

	[ClassDataSource<TelnetIntegrationFixture>(Shared = SharedType.PerClass)]
	public required TelnetIntegrationFixture Fixture { get; init; }

	/// <summary>
	/// Verifies that a newly opened TCP connection to the telnet port receives the
	/// SharpMUSH login screen (the content of connect.txt) within a reasonable timeout.
	/// </summary>
	[Test]
	[Timeout(60_000)]
	public async Task TelnetConnection_ReceivesLoginScreen(CancellationToken cancellationToken)
	{
		using var client = new TcpClient();
		await client.ConnectAsync(IPAddress.Loopback, Fixture.TelnetPort);
		client.ReceiveTimeout = ReceiveTimeoutMs;

		await using var stream = client.GetStream();
		var received = await ReadUntilAsync(stream, s => s.Contains("Welcome to SharpMUSH"), cancellationToken);

		await Assert.That(received).Contains("Welcome to SharpMUSH")
			.Because("A new telnet connection must receive the connect.txt login screen");
	}

	/// <summary>
	/// Verifies that a player can log in as God (empty password) via the telnet connection
	/// and that after login the first room description ("Room Zero") is sent automatically.
	/// This exercises the full end-to-end path:
	///   TCP → TelnetInputMessage → NATS → Server connect command → look → NATS → TCP.
	/// </summary>
	[Test]
	[Timeout(120_000)]
	public async Task TelnetConnection_CanLogin_AndSeesFirstRoom(CancellationToken cancellationToken)
	{
		using var client = new TcpClient();
		await client.ConnectAsync(IPAddress.Loopback, Fixture.TelnetPort);
		client.ReceiveTimeout = ReceiveTimeoutMs;

		await using var stream = client.GetStream();

		// ── Step 1: wait for the login screen ────────────────────────────────
		var loginScreen = await ReadUntilAsync(stream, s => s.Contains("Welcome to SharpMUSH"), cancellationToken);
		await Assert.That(loginScreen).Contains("Welcome to SharpMUSH")
			.Because("Login screen must appear before we can log in");

		// ── Step 2: send the connect command (God has an empty password) ─────
		await SendLineAsync(stream, "connect God", cancellationToken);

		// ── Step 3: wait for the room description that auto-look produces ────
		// ShowPostLoginMessages sends MOTD → WizMOTD → look, which outputs
		// the room name "Room Zero" followed by a description.
		var postLogin = await ReadUntilAsync(stream, s => s.Contains("Room Zero"), cancellationToken);

		await Assert.That(postLogin).Contains("Room Zero")
			.Because("After logging in as God, the auto-look should show Room Zero");
	}

	// ── Helpers ──────────────────────────────────────────────────────────────

	/// <summary>
	/// Reads from <paramref name="stream"/> polling every <see cref="PollingIntervalMs"/> ms,
	/// accumulating stripped text, until <paramref name="stopCondition"/> returns true or the
	/// cancellation token fires.
	/// </summary>
	private static async Task<string> ReadUntilAsync(
		NetworkStream stream,
		Func<string, bool> stopCondition,
		CancellationToken cancellationToken)
	{
		var buffer = new byte[4096];
		var received = new StringBuilder();

		while (!cancellationToken.IsCancellationRequested)
		{
			if (stream.DataAvailable)
			{
				var bytesRead = await stream.ReadAsync(buffer, cancellationToken);
				if (bytesRead > 0)
				{
					received.Append(StripTelnetControlBytes(buffer, bytesRead));
					if (stopCondition(received.ToString()))
						break;
				}
			}
			else
			{
				try { await Task.Delay(PollingIntervalMs, cancellationToken); }
				catch (OperationCanceledException) { break; }
			}
		}

		return received.ToString();
	}

	/// <summary>
	/// Writes a text line (appending CRLF) to the telnet stream.
	/// </summary>
	private static async Task SendLineAsync(NetworkStream stream, string line, CancellationToken cancellationToken)
	{
		var bytes = Encoding.UTF8.GetBytes(line + "\r\n");
		await stream.WriteAsync(bytes, cancellationToken);
		await stream.FlushAsync(cancellationToken);
	}

	/// <summary>
	/// Strips IAC telnet negotiation sequences from a raw byte buffer and returns
	/// the resulting printable text.
	/// </summary>
	private static string StripTelnetControlBytes(byte[] data, int length)
	{
		var result = new List<byte>(length);
		for (var i = 0; i < length; i++)
		{
			if (data[i] != 0xFF) // not IAC
			{
				result.Add(data[i]);
				continue;
			}

			// IAC — consume the command sequence without emitting bytes
			if (i + 1 >= length) break;
			var cmd = data[i + 1];

			if (cmd is 0xFB or 0xFC or 0xFD or 0xFE) // WILL / WONT / DO / DONT + option byte
			{
				i += 2;
			}
			else if (cmd == 0xFF) // escaped IAC (0xFF 0xFF → literal 0xFF)
			{
				result.Add(0xFF);
				i++;
			}
			else
			{
				i++; // 2-byte sequence: IAC + sub-command
			}
		}

		return Encoding.UTF8.GetString(result.ToArray());
	}
}
