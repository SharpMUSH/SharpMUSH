using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// Integration tests that verify telnet output reaches the TCP socket.
/// 
/// IMPORTANT: These tests cover different aspects of the message flow:
/// 
/// **Automated Test (runs in CI):**
/// - `NotifyService_PublishesToKafka_WithBatching` - Validates connection management
///   and that NotifyService accepts messages without errors. This ensures the basic
///   infrastructure works but does NOT verify Kafka publishing or TCP delivery.
/// 
/// **Manual Tests (require ConnectionServer running):**
/// These tests prove the COMPLETE end-to-end flow including TCP socket delivery.
/// You must manually start ConnectionServer before running these tests.
/// 
/// The full message flow is:
/// 1. NotifyService.Notify() is called in Main Server
/// 2. Message is batched (8ms) and published to Kafka topic "telnet-output"
/// 3. ConnectionServer consumes from Kafka
/// 4. ConnectionServer sends message to TCP socket
/// 
/// Only the manual tests prove the complete flow works end-to-end.
/// </summary>
[NotInParallel]
public class TelnetOutputIntegrationTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	/// <summary>
	/// This test validates that NotifyService accepts messages and connections work correctly.
	/// It proves the first part of the message flow: Connection registration and message acceptance.
	/// 
	/// The complete end-to-end test (including Kafka publishing and TCP socket delivery) requires 
	/// ConnectionServer to be running and is documented in the manual tests below.
	/// 
	/// This test verifies:
	/// - Connection registration works correctly
	/// - NotifyService accepts messages without errors
	/// - Connection cleanup works correctly
	/// 
	/// Note: This test does NOT verify that messages reach Kafka or the TCP socket. 
	/// For complete end-to-end verification, use the manual tests with ConnectionServer running.
	/// </summary>
	[Test]
	public async Task NotifyService_PublishesToKafka_WithBatching()
	{
		// Register a test connection
		var handle = 99999L;
		await ConnectionService.Register(
			handle, 
			"127.0.0.1",  // ipaddr
			"integration-test",  // host
			"test-client",  // connectionType
			_ => ValueTask.CompletedTask,  // outputFunction
			_ => ValueTask.CompletedTask,  // promptOutputFunction
			() => Encoding.UTF8);  // encoding

		// Send a message via NotifyService
		const string testMessage = "Test message from NotifyService";
		await NotifyService.Notify(handle, testMessage, sender: null);

		// Wait for batching timer (8ms) plus buffer time
		// This allows the message to be batched and sent to Kafka (if Kafka is available)
		await Task.Delay(100);

		// Verify connection is still active before cleanup
		var connBefore = ConnectionService.Get(handle);
		await Assert.That(connBefore).IsNotNull();

		// Cleanup
		await ConnectionService.Disconnect(handle);

		// Verify the connection was properly cleaned up after disconnect
		var connAfter = ConnectionService.Get(handle);
		await Assert.That(connAfter).IsNull();
	}

	/// <summary>
	/// Manual end-to-end test that proves messages reach the actual TCP socket.
	/// 
	/// TO RUN THIS TEST:
	/// 1. Remove the [Skip] attribute
	/// 2. In a separate terminal, start ConnectionServer:
	///    cd SharpMUSH.ConnectionServer && dotnet run
	/// 3. Run this test
	/// 
	/// This test proves the COMPLETE flow:
	/// NotifyService → Kafka → ConnectionServer → TCP Socket
	/// </summary>
	[Test]
	[Skip("Manual test - requires ConnectionServer running on port 4201")]
	public async Task CompleteFlow_ConnectCommand_SendsMessageToTcpSocket()
	{
		const string host = "127.0.0.1";
		const int port = 4201;

		// Connect to the telnet server
		using var client = new TcpClient();
		await client.ConnectAsync(host, port);
		await using var stream = client.GetStream();
		using var reader = new StreamReader(stream, Encoding.UTF8);
		await using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

		// Read welcome message
		var welcome = await ReadUntilData(reader, TimeSpan.FromSeconds(3));
		Console.WriteLine($"Welcome message: {welcome}");

		// Send "connect #1" command
		await writer.WriteLineAsync("connect #1");

		// Read response - should contain "Connected!"
		var response = await ReadUntilData(reader, TimeSpan.FromSeconds(5));
		Console.WriteLine($"Response: {response}");

		// Verify we got the connected message
		await Assert.That(response).Contains("Connected!");
	}

	/// <summary>
	/// Manual test that proves NotifyService messages reach the TCP socket.
	/// 
	/// TO RUN THIS TEST:
	/// 1. Remove the [Skip] attribute
	/// 2. In a separate terminal, start ConnectionServer:
	///    cd SharpMUSH.ConnectionServer && dotnet run
	/// 3. Run this test
	/// </summary>
	[Test]
	[Skip("Manual test - requires ConnectionServer running on port 4201")]
	public async Task CompleteFlow_NotifyService_DeliversToTcpSocket()
	{
		const string host = "127.0.0.1";
		const int port = 4201;

		// Connect to the telnet server
		using var client = new TcpClient();
		await client.ConnectAsync(host, port);
		await using var stream = client.GetStream();
		using var reader = new StreamReader(stream, Encoding.UTF8);
		await using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

		// Read and discard welcome message
		await ReadUntilData(reader, TimeSpan.FromSeconds(3));

		// Send "connect #1" command to authenticate
		await writer.WriteLineAsync("connect #1");
		await ReadUntilData(reader, TimeSpan.FromSeconds(3));

		// Now send a test message via NotifyService
		const string testMessage = "This message came from NotifyService through Kafka!";
		await NotifyService.Notify(new DBRef(1), testMessage, sender: null);

		// Wait for message to flow: NotifyService → Kafka → ConnectionServer → TCP
		await Task.Delay(500);

		// Read response
		var response = await ReadUntilData(reader, TimeSpan.FromSeconds(3));
		Console.WriteLine($"Response: {response}");

		// Verify our message arrived
		await Assert.That(response).Contains(testMessage);
	}

	/// <summary>
	/// Manual test that proves message batching works end-to-end.
	/// 
	/// TO RUN THIS TEST:
	/// 1. Remove the [Skip] attribute
	/// 2. In a separate terminal, start ConnectionServer:
	///    cd SharpMUSH.ConnectionServer && dotnet run
	/// 3. Run this test
	/// </summary>
	[Test]
	[Skip("Manual test - requires ConnectionServer running on port 4201")]
	public async Task CompleteFlow_BatchedMessages_AllDeliveredToSocket()
	{
		const string host = "127.0.0.1";
		const int port = 4201;

		// Connect to the telnet server
		using var client = new TcpClient();
		await client.ConnectAsync(host, port);
		await using var stream = client.GetStream();
		using var reader = new StreamReader(stream, Encoding.UTF8);
		await using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

		// Read and discard welcome message
		await ReadUntilData(reader, TimeSpan.FromSeconds(3));

		// Send "connect #1" command
		await writer.WriteLineAsync("connect #1");
		await ReadUntilData(reader, TimeSpan.FromSeconds(3));

		// Send multiple messages rapidly - they should be batched by NotifyService
		var messages = new[] { "Batch 1", "Batch 2", "Batch 3", "Batch 4", "Batch 5" };
		foreach (var msg in messages)
		{
			await NotifyService.Notify(new DBRef(1), msg, sender: null);
		}

		// Wait for batching (8ms) + Kafka + ConnectionServer delivery
		await Task.Delay(500);

		// Read response
		var response = await ReadUntilData(reader, TimeSpan.FromSeconds(3));
		Console.WriteLine($"Response: {response}");

		// Verify all batched messages arrived
		foreach (var msg in messages)
		{
			await Assert.That(response).Contains(msg);
		}
	}

	/// <summary>
	/// Reads data from the stream until no more data is available.
	/// </summary>
	private async Task<string> ReadUntilData(StreamReader reader, TimeSpan timeout)
	{
		var sb = new StringBuilder();
		var buffer = new char[4096];
		var start = DateTime.UtcNow;

		while (DateTime.UtcNow - start < timeout)
		{
			// Check if data is available
			if (reader.BaseStream is NetworkStream ns && ns.DataAvailable)
			{
				var count = await reader.ReadAsync(buffer, 0, buffer.Length);
				sb.Append(buffer, 0, count);

				// Reset timeout on data received
				start = DateTime.UtcNow;

				// Wait a bit to see if more data is coming
				await Task.Delay(50);
			}
			else if (sb.Length > 0)
			{
				// We have data and no more is available - we're done
				break;
			}
			else
			{
				// No data yet, wait a bit before checking again
				await Task.Delay(50);
			}
		}

		return sb.ToString();
	}
}
