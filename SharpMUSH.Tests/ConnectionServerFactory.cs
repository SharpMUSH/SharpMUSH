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
/// Overrides CreateHost to create a real host that listens on actual ports (not just TestServer).
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

	private IHost? _host;
	
	protected override IHost CreateHost(IHostBuilder builder)
	{
		// Create a real host instead of a TestServer
		// This is necessary so Kestrel actually listens on real ports for telnet connections
		_host = builder.Build();
		
		// Start the host - this actually makes it listen on ports
		_host.StartAsync().GetAwaiter().GetResult();
		
		return _host;
	}

	public async Task InitializeAsync()
	{
		Console.WriteLine("ConnectionServerFactory: Starting initialization...");
		
		// The _host should already be started from CreateHost()
		// Don't access Server property as that creates a TestServer
		if (_host == null)
		{
			throw new InvalidOperationException("Host was not created - CreateHost should have been called");
		}
		
		// Wait a bit for the host to fully start
		await Task.Delay(5000);
		
		Console.WriteLine($"ConnectionServerFactory: Waiting for server to become healthy (telnet:{TelnetPort}, http:{HttpPort})...");
		
		// Wait for server to start listening - use a real HttpClient for actual HTTP port
		using var httpClient = new HttpClient { BaseAddress = new Uri($"http://localhost:{HttpPort}") };
		httpClient.Timeout = TimeSpan.FromSeconds(5);
		var retries = 30;
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
				else
				{
					Console.WriteLine($"ConnectionServerFactory: Health check returned {response.StatusCode}");
				}
			}
			catch (Exception ex)
			{
				if (retries % 5 == 0 || retries < 5)
				{
					Console.WriteLine($"ConnectionServerFactory: Still waiting... ({retries} retries left, error: {ex.Message})");
				}
			}
			await Task.Delay(1000); // 1 second between attempts
		}
		
		if (retries <= 0)
		{
			Console.WriteLine("ConnectionServerFactory: ❌ Server failed to become healthy within timeout!");
			throw new TimeoutException("ConnectionServer did not become healthy within the timeout period");
		}
		
		// Give telnet listener time to fully initialize
		await Task.Delay(2000);
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
