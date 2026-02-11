using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using SharpMUSH.ConnectionServer;
using TUnit.Core;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>
/// Factory for ConnectionServer integration testing using ASP.NET Core testing infrastructure.
/// Uses WebApplicationFactory to properly start and manage ConnectionServer lifecycle.
/// </summary>
public class ConnectionServerFactory : WebApplicationFactory<Program>, IAsyncInitializer
{
	[ClassDataSource<RedPandaTestServer>(Shared = SharedType.PerTestSession)]
	public required RedPandaTestServer RedPandaTestServer { get; init; }
	
	[ClassDataSource<RedisTestServer>(Shared = SharedType.PerTestSession)]
	public required RedisTestServer RedisTestServer { get; init; }
	
	/// <summary>
	/// Port where Telnet server is listening.
	/// </summary>
	public int TelnetPort { get; private set; }
	
	/// <summary>
	/// Port where HTTP/WebSocket server is listening.
	/// </summary>
	public int HttpPort { get; private set; }

	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		// Get test infrastructure addresses
		var kafkaHost = RedPandaTestServer.Instance.Hostname;
		var kafkaPort = RedPandaTestServer.Instance.GetMappedPublicPort(9092);
		var redisPort = RedisTestServer.Instance.GetMappedPublicPort(6379);
		var redisConnection = $"localhost:{redisPort}";

		// Use random available ports for testing
		TelnetPort = GetAvailablePort();
		HttpPort = GetAvailablePort();

		Console.WriteLine($"ConnectionServerFactory: Configuring with Kafka={kafkaHost}:{kafkaPort}, Redis={redisConnection}");
		Console.WriteLine($"ConnectionServerFactory: Telnet port={TelnetPort}, HTTP port={HttpPort}");

		// Override configuration with test values
		var config = new Dictionary<string, string?>
		{
			["KAFKA_HOST"] = $"{kafkaHost}:{kafkaPort}",
			["REDIS_CONNECTION"] = redisConnection,
			["ConnectionServer:TelnetPort"] = TelnetPort.ToString(),
			["ConnectionServer:HttpPort"] = HttpPort.ToString(),
		};
		
		builder.UseConfiguration(new ConfigurationBuilder()
			.AddInMemoryCollection(config)
			.Build());
			
		builder.UseUrls($"http://localhost:{HttpPort}");
	}

	public async Task InitializeAsync()
	{
		Console.WriteLine("ConnectionServerFactory: Starting initialization...");
		
		// Create the server - this starts the application
		_ = Server;
		
		Console.WriteLine("ConnectionServerFactory: Waiting for server to become healthy...");
		
		// Wait for server to start listening
		using var httpClient = CreateClient();
		var retries = 40;
		while (retries-- > 0)
		{
			try
			{
				var response = await httpClient.GetAsync("/health");
				if (response.IsSuccessStatusCode)
				{
					Console.WriteLine($"ConnectionServerFactory: ✓ Server healthy on telnet:{TelnetPort}, http:{HttpPort}");
					break;
				}
			}
			catch (Exception ex)
			{
				if (retries % 10 == 0)
				{
					Console.WriteLine($"ConnectionServerFactory: Still waiting... ({retries} retries left, error: {ex.Message})");
				}
			}
			await Task.Delay(500);
		}
		
		if (retries <= 0)
		{
			Console.WriteLine("ConnectionServerFactory: ❌ Server failed to become healthy within timeout!");
			throw new TimeoutException("ConnectionServer did not become healthy within the timeout period");
		}
		
		// Give telnet listener time to fully initialize
		await Task.Delay(1000);
		Console.WriteLine("ConnectionServerFactory: Initialization complete");
	}

	private static int GetAvailablePort()
	{
		using var socket = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
		socket.Start();
		var port = ((System.Net.IPEndPoint)socket.LocalEndpoint).Port;
		socket.Stop();
		return port;
	}
}
