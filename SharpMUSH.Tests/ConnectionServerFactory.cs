using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SharpMUSH.ConnectionServer;
using TUnit.Core;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>
/// Factory for ConnectionServer integration testing using TUnit infrastructure.
/// Provides access to ConnectionServer services while sharing test infrastructure
/// (Redis, Kafka, ArangoDB) across the test session.
/// </summary>
public class ConnectionServerFactory : IAsyncInitializer, IAsyncDisposable
{
	[ClassDataSource<RedPandaTestServer>(Shared = SharedType.PerTestSession)]
	public required RedPandaTestServer RedPandaTestServer { get; init; }

	[ClassDataSource<RedisTestServer>(Shared = SharedType.PerTestSession)]
	public required RedisTestServer RedisTestServer { get; init; }

	private WebApplicationFactory<Program>? _factory;
	
	/// <summary>
	/// Access to ConnectionServer services for testing.
	/// </summary>
	public IServiceProvider Services => _factory!.Services;
	
	/// <summary>
	/// Port where Telnet server is listening.
	/// </summary>
	public int TelnetPort { get; private set; }
	
	/// <summary>
	/// Port where HTTP/WebSocket server is listening.
	/// </summary>
	public int HttpPort { get; private set; }

	public async Task InitializeAsync()
	{
		// Get test infrastructure addresses
		var kafkaHost = RedPandaTestServer.Instance.Hostname;
		var redisPort = RedisTestServer.Instance.GetMappedPublicPort(6379);
		var redisConnection = $"localhost:{redisPort}";

		// Set environment variables for test infrastructure
		Environment.SetEnvironmentVariable("KAFKA_HOST", kafkaHost);
		Environment.SetEnvironmentVariable("REDIS_CONNECTION", redisConnection);

		// Use random available ports for testing
		TelnetPort = GetAvailablePort();
		HttpPort = GetAvailablePort();

		// Set environment variables for ConnectionServer configuration
		Environment.SetEnvironmentVariable("ConnectionServer__TelnetPort", TelnetPort.ToString());
		Environment.SetEnvironmentVariable("ConnectionServer__HttpPort", HttpPort.ToString());

		// Create ConnectionServer factory
		_factory = new WebApplicationFactory<Program>()
			.WithWebHostBuilder(builder =>
			{
				builder.UseEnvironment("Testing");
				// Additional configuration can go here if needed
			});
		
		// Ensure server is created (this triggers the build)
		_ = _factory.Server;
		
		// Give the server a moment to start listening
		await Task.Delay(1000);
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
		if (_factory != null)
		{
			await _factory.DisposeAsync();
		}
	}
}
