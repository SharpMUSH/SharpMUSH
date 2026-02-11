using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpMUSH.ConnectionServer;
using TUnit.Core;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>
/// Factory for ConnectionServer integration testing.
/// Uses WebApplicationFactory but configures it to use real Kestrel server instead of TestServer.
/// This is necessary because telnet connections require actual TCP ports, not in-memory TestServer.
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
			
		// CRITICAL: Use real Kestrel server instead of TestServer
		// This makes Kestrel actually listen on TCP ports
		builder.UseKestrel();
		builder.UseUrls($"http://localhost:{HttpPort}");
		
		// Don't use TestServer - we need real ports
		builder.UseSetting(WebHostDefaults.ApplicationKey, typeof(Program).Assembly.FullName);
	}

	protected override IHost CreateHost(IHostBuilder builder)
	{
		// Build the host - this will use the Kestrel configuration from ConfigureWebHost
		var host = builder.Build();
		
		// Start the host asynchronously
		host.StartAsync().GetAwaiter().GetResult();
		
		Console.WriteLine("ConnectionServerFactory: Host created and started");
		
		return host;
	}

	public async Task InitializeAsync()
	{
		Console.WriteLine("ConnectionServerFactory: Starting initialization...");
		
		// Trigger host creation by accessing Services
		// This will call CreateHost() which starts the server
		var services = Services;
		
		Console.WriteLine("ConnectionServerFactory: Checking server addresses...");
		
		// Get the actual server to verify it's listening
		var server = services.GetRequiredService<IServer>();
		var addresses = server.Features.Get<IServerAddressesFeature>();
		if (addresses != null)
		{
			Console.WriteLine($"ConnectionServerFactory: Server listening on: {string.Join(", ", addresses.Addresses)}");
		}
		
		// Wait a bit for the host to fully start
		await Task.Delay(3000);
		
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
