using KafkaFlow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>
/// Factory for ConnectionServer in integration tests.
/// Starts the ConnectionServer as a background service using HostBuilder.
/// </summary>
public class ConnectionServerFactory : IAsyncInitializer, IAsyncDisposable
{
[ClassDataSource<RedPandaTestServer>(Shared = SharedType.PerTestSession)]
public required RedPandaTestServer RedPandaTestServer { get; init; }

[ClassDataSource<RedisTestServer>(Shared = SharedType.PerTestSession)]
public required RedisTestServer RedisTestServer { get; init; }

public int TelnetPort { get; private set; }
public int HttpPort { get; private set; }

private IHost? _host;
private Task? _hostTask;

public async Task InitializeAsync()
{
var log = new LoggerConfiguration()
.Enrich.FromLogContext()
.WriteTo.Console(theme: AnsiConsoleTheme.Code)
.MinimumLevel.Debug()
.CreateLogger();

Log.Logger = log;

// Get test infrastructure addresses
var redisPort = RedisTestServer.Instance.GetMappedPublicPort(6379);
var redisConnection = $"localhost:{redisPort}";
Environment.SetEnvironmentVariable("REDIS_CONNECTION", redisConnection);

var kafkaHost = RedPandaTestServer.Instance.GetBootstrapAddress();
Environment.SetEnvironmentVariable("KAFKA_HOST", kafkaHost);

// Use random available ports for testing
TelnetPort = GetAvailablePort();
HttpPort = GetAvailablePort();

// Set environment variables for ConnectionServer configuration
Environment.SetEnvironmentVariable("ConnectionServer__TelnetPort", TelnetPort.ToString());
Environment.SetEnvironmentVariable("ConnectionServer__HttpPort", HttpPort.ToString());

// Start ConnectionServer as a background task
_hostTask = Task.Run(async () =>
{
var args = new string[] { };
// Call the ConnectionServer's Program.Main (which is generated for top-level statements)
await SharpMUSH.ConnectionServer.Program.Main(args);
});

// Give the server time to start listening
await Task.Delay(2000);
}

private static int GetAvailablePort()
{
using var socket = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
socket.Start();
var port = ((System.Net.IPEndPoint)socket.LocalEndpoint).Port;
socket.Stop();
return port;
}

public async ValueTask DisposeAsync()
{
if (_host != null)
{
await _host.StopAsync();
_host.Dispose();
}
}
}
