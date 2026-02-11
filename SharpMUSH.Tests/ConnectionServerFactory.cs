using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
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
	private HttpClient? _httpClient;
	
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
				// Use Kestrel (not test server) to actually listen on ports
				builder.UseKestrel();
				builder.UseUrls($"http://localhost:{HttpPort}");
			});
		
		// Create HTTP client - this triggers the server to start
		_httpClient = _factory.CreateClient();
		
		// Wait for server to be ready by checking health endpoint
		var retries = 10;
		while (retries-- > 0)
		{
			try
			{
				var response = await _httpClient.GetAsync("/health");
				if (response.IsSuccessStatusCode)
				{
					break;
				}
			}
			catch
			{
				// Server not ready yet
			}
			await Task.Delay(500);
		}
		
		// Give the telnet listener a moment to start
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
		_httpClient?.Dispose();
		if (_factory != null)
		{
			await _factory.DisposeAsync();
		}
	}
}
