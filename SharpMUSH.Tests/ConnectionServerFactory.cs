using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using SharpMUSH.ConnectionServer;
using TUnit.Core;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>
/// Factory for ConnectionServer integration testing using TUnit infrastructure.
/// Starts the actual ConnectionServer application (not a test server) so it
/// listens on real ports for telnet connections.
/// </summary>
public class ConnectionServerFactory : IAsyncInitializer, IAsyncDisposable
{
	[ClassDataSource<RedPandaTestServer>(Shared = SharedType.PerTestSession)]
	public required RedPandaTestServer RedPandaTestServer { get; init; }

	[ClassDataSource<RedisTestServer>(Shared = SharedType.PerTestSession)]
	public required RedisTestServer RedisTestServer { get; init; }

	private Task? _serverTask;
	
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
		var kafkaPort = RedPandaTestServer.Instance.GetMappedPublicPort(9092);
		var redisPort = RedisTestServer.Instance.GetMappedPublicPort(6379);
		var redisConnection = $"localhost:{redisPort}";

		// Set environment variables for test infrastructure
		Environment.SetEnvironmentVariable("KAFKA_HOST", $"{kafkaHost}:{kafkaPort}");
		Environment.SetEnvironmentVariable("REDIS_CONNECTION", redisConnection);

		// Use random available ports for testing
		TelnetPort = GetAvailablePort();
		HttpPort = GetAvailablePort();

		// Set environment variables for ConnectionServer configuration
		Environment.SetEnvironmentVariable("ConnectionServer__TelnetPort", TelnetPort.ToString());
		Environment.SetEnvironmentVariable("ConnectionServer__HttpPort", HttpPort.ToString());

		// Start the actual ConnectionServer application in a background task
		// We can't pass cancellation token to Main(), so we'll use the host's lifetime instead
		_serverTask = Task.Run(async () =>
		{
			try
			{
				await Program.Main([]);
			}
			catch (OperationCanceledException)
			{
				// Expected when stopping
			}
		});
		
		// Wait for server to start listening
		using var httpClient = new HttpClient { BaseAddress = new Uri($"http://localhost:{HttpPort}") };
		var retries = 40;
		while (retries-- > 0)
		{
			try
			{
				var response = await httpClient.GetAsync("/health");
				if (response.IsSuccessStatusCode)
				{
					Console.WriteLine($"ConnectionServer started successfully on telnet:{TelnetPort}, http:{HttpPort}");
					break;
				}
			}
			catch
			{
				// Server not ready yet
			}
			await Task.Delay(500);
		}
		
		// Give telnet listener time to fully initialize
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
		// To stop the server, we need to trigger a shutdown
		// We can do this by sending a stop signal to the HTTP endpoint or
		// by killing the process. For now, we'll just wait a bit and let it timeout.
		
		if (_serverTask != null)
		{
			try
			{
				// Send shutdown request to the server
				using var httpClient = new HttpClient { BaseAddress = new Uri($"http://localhost:{HttpPort}") };
				httpClient.Timeout = TimeSpan.FromSeconds(2);
				try
				{
					// Try to trigger graceful shutdown by calling a special endpoint
					// If this doesn't exist, the timeout will handle it
					await httpClient.PostAsync("/shutdown", null!);
				}
				catch
				{
					// Ignore errors - server might not have shutdown endpoint
				}
				
				// Wait for server task to complete
				await _serverTask.WaitAsync(TimeSpan.FromSeconds(10));
			}
			catch
			{
				// Timeout or other error - that's okay, process will be cleaned up
			}
		}
	}
}
