using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// Integration tests to measure actual connection timing and performance.
/// These tests validate the connection flow documented in TELNET_CONNECTION_TIMING_ANALYSIS.md
/// </summary>
[NotInParallel]
public class ConnectionTimingIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	[ClassDataSource<ConnectionServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ConnectionServerWebAppFactory ConnectionServerFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private ITelemetryService? TelemetryService => WebAppFactoryArg.Services.GetService<ITelemetryService>();

	/// <summary>
	/// Measures the time to establish a telnet connection and receive the first prompt.
	/// Expected: 120-200ms as documented in TELNET_CONNECTION_TIMING_ANALYSIS.md
	/// </summary>
	[Test]
	public async Task MeasureConnectionEstablishmentTime()
	{
		var sw = Stopwatch.StartNew();
		
		// Connect to telnet server
		using var client = new TcpClient();
		await client.ConnectAsync("127.0.0.1", 4201);
		var connectTime = sw.Elapsed.TotalMilliseconds;
		
		// Wait for connection to be established in the system
		await Task.Delay(200);
		sw.Stop();
		var totalTime = sw.Elapsed.TotalMilliseconds;
		
		Console.WriteLine($"TCP Connect Time: {connectTime:F2}ms");
		Console.WriteLine($"Total Connection Establishment: {totalTime:F2}ms");
		
		// Verify connection was registered
		var connections = await ConnectionService.GetAll().ToListAsync();
		await Assert.That(connections.Count).IsGreaterThanOrEqualTo(1);
		
		// Verify timing is within expected range (with generous margin for CI environments)
		await Assert.That(totalTime).IsLessThan(500); // Should be < 500ms even in slow CI
		
		client.Close();
	}

	/// <summary>
	/// Measures the time to disconnect a telnet connection.
	/// Expected: 60-100ms as documented in TELNET_CONNECTION_TIMING_ANALYSIS.md
	/// </summary>
	[Test]
	public async Task MeasureDisconnectionTime()
	{
		// Establish connection
		using var client = new TcpClient();
		await client.ConnectAsync("127.0.0.1", 4201);
		await Task.Delay(200); // Wait for connection to be fully established
		
		// Get initial connection count
		var initialCount = await ConnectionService.GetAll().CountAsync();
		
		// Measure disconnection time
		var sw = Stopwatch.StartNew();
		client.Close();
		
		// Wait for disconnection to be processed
		await Task.Delay(200);
		sw.Stop();
		var disconnectTime = sw.Elapsed.TotalMilliseconds;
		
		Console.WriteLine($"Disconnection Time: {disconnectTime:F2}ms");
		
		// Verify connection was removed
		var finalCount = await ConnectionService.GetAll().CountAsync();
		await Assert.That(finalCount).IsLessThan(initialCount);
		
		// Verify timing is within expected range
		await Assert.That(disconnectTime).IsLessThan(500); // Should be < 500ms even in slow CI
	}

	/// <summary>
	/// Tests rapid connect/disconnect pattern to verify no handle collisions or resource leaks.
	/// Expected: System handles this gracefully as documented in TELNET_CONNECTION_TIMING_ANALYSIS.md
	/// </summary>
	[Test]
	public async Task TestRapidConnectDisconnect()
	{
		var iterations = 10;
		var connections = new List<TcpClient>();
		
		var sw = Stopwatch.StartNew();
		
		// Rapidly connect
		for (int i = 0; i < iterations; i++)
		{
			var client = new TcpClient();
			await client.ConnectAsync("127.0.0.1", 4201);
			connections.Add(client);
		}
		
		var connectTime = sw.Elapsed.TotalMilliseconds;
		Console.WriteLine($"Connected {iterations} clients in {connectTime:F2}ms ({connectTime/iterations:F2}ms avg)");
		
		// Wait for all connections to be registered
		await Task.Delay(500);
		
		// Verify all connections are registered
		var activeConnections = await ConnectionService.GetAll().CountAsync();
		Console.WriteLine($"Active connections in system: {activeConnections}");
		await Assert.That(activeConnections).IsGreaterThanOrEqualTo(iterations);
		
		// Rapidly disconnect
		sw.Restart();
		foreach (var client in connections)
		{
			client.Close();
		}
		var disconnectTime = sw.Elapsed.TotalMilliseconds;
		Console.WriteLine($"Disconnected {iterations} clients in {disconnectTime:F2}ms ({disconnectTime/iterations:F2}ms avg)");
		
		// Wait for all disconnections to be processed
		await Task.Delay(500);
		
		// Verify connections were cleaned up
		var remainingConnections = await ConnectionService.GetAll().CountAsync();
		Console.WriteLine($"Remaining connections: {remainingConnections}");
		
		// All our test connections should be gone
		await Assert.That(remainingConnections).IsLessThan(activeConnections);
	}

	/// <summary>
	/// Tests the anomalous pattern: connect, immediately disconnect, then reconnect.
	/// Expected: No handle collision, new unique handle assigned as documented.
	/// </summary>
	[Test]
	public async Task TestConnectDisconnectReconnectPattern()
	{
		// First connection
		using var client1 = new TcpClient();
		await client1.ConnectAsync("127.0.0.1", 4201);
		await Task.Delay(200);
		
		var initialConnections = await ConnectionService.GetAll().ToListAsync();
		var handle1 = initialConnections.LastOrDefault()?.Handle;
		await Assert.That(handle1).IsNotNull();
		
		Console.WriteLine($"Connection 1 handle: {handle1}");
		
		// Immediate disconnect
		client1.Close();
		await Task.Delay(200);
		
		// Immediate reconnect
		using var client2 = new TcpClient();
		await client2.ConnectAsync("127.0.0.1", 4201);
		await Task.Delay(200);
		
		var newConnections = await ConnectionService.GetAll().ToListAsync();
		var handle2 = newConnections.LastOrDefault()?.Handle;
		await Assert.That(handle2).IsNotNull();
		
		Console.WriteLine($"Connection 2 handle: {handle2}");
		
		// Verify different handles (no collision)
		await Assert.That(handle1).IsNotEqualTo(handle2!);
		
		client2.Close();
		await Task.Delay(200);
	}

	/// <summary>
	/// Measures round-trip time for a simple command (input → output).
	/// Expected: 180-200ms as documented in TELNET_CONNECTION_TIMING_ANALYSIS.md
	/// </summary>
	[Test]
	public async Task MeasureCommandRoundTripTime()
	{
		// Connect to telnet server
		using var client = new TcpClient();
		await client.ConnectAsync("127.0.0.1", 4201);
		var stream = client.GetStream();
		var reader = new StreamReader(stream, Encoding.UTF8);
		var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
		
		// Wait for connection to be established
		await Task.Delay(200);
		
		// Consume any initial output (welcome message, etc.)
		while (stream.DataAvailable)
		{
			await reader.ReadAsync(new char[4096], 0, 4096);
		}
		
		// Send a simple command and measure round-trip time
		var sw = Stopwatch.StartNew();
		await writer.WriteLineAsync("think test");
		
		// Wait for response
		var responseReceived = false;
		var timeout = TimeSpan.FromSeconds(5);
		var startTime = DateTime.UtcNow;
		
		while (!responseReceived && DateTime.UtcNow - startTime < timeout)
		{
			if (stream.DataAvailable)
			{
				responseReceived = true;
				sw.Stop();
				break;
			}
			await Task.Delay(10);
		}
		
		var roundTripTime = sw.Elapsed.TotalMilliseconds;
		Console.WriteLine($"Command Round-Trip Time: {roundTripTime:F2}ms");
		
		await Assert.That(responseReceived).IsTrue();
		await Assert.That(roundTripTime).IsLessThan(1000); // Should be < 1s even in slow CI
		
		client.Close();
	}

	/// <summary>
	/// Tests connection timeout behavior - verifies idle connections remain open.
	/// Note: This test documents current behavior (no timeout). See TELNET_CONNECTION_TIMING_ANALYSIS.md
	/// for recommendations on implementing idle timeout.
	/// </summary>
	[Test]
	public async Task TestIdleConnectionBehavior()
	{
		// Connect but don't send any data
		using var client = new TcpClient();
		await client.ConnectAsync("127.0.0.1", 4201);
		await Task.Delay(200);
		
		var initialConnections = await ConnectionService.GetAll().ToListAsync();
		var handle = initialConnections.LastOrDefault()?.Handle;
		await Assert.That(handle).IsNotNull();
		
		Console.WriteLine($"Connection established with handle: {handle}");
		
		// Wait for a few seconds (idle)
		await Task.Delay(3000);
		
		// Verify connection is still alive (no timeout implemented)
		var currentConnections = await ConnectionService.GetAll().ToListAsync();
		var stillConnected = currentConnections.Any(c => c.Handle == handle);
		
		Console.WriteLine($"Connection still active after 3s idle: {stillConnected}");
		await Assert.That(stillConnected).IsTrue();
		
		client.Close();
		await Task.Delay(200);
	}
}
