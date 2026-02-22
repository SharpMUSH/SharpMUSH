using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messaging.Abstractions;
using System.Text;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>
/// Tests for WebSocket connection service functionality.
/// Verifies that connections are properly managed and data is sent correctly.
/// </summary>
public class WebSocketConnectionServiceTests
{
	[Test]
	public async Task CanRegisterWebSocketConnection()
	{
		// Arrange
		var logger = new LoggerFactory().CreateLogger<ConnectionServerService>();
		var publishEndpoint = Substitute.For<IMessageBus>();
		var service = new ConnectionServerService(logger, publishEndpoint);

		var handle = 1000001L;
		var ipAddress = "192.168.1.1";
		var hostname = "test.local";
		var connectionType = "websocket";

		byte[]? receivedData = null;
		Func<byte[], ValueTask> outputFunction = async (data) =>
		{
			receivedData = data;
			await ValueTask.CompletedTask;
		};

		byte[]? receivedPrompt = null;
		Func<byte[], ValueTask> promptFunction = async (data) =>
		{
			receivedPrompt = data;
			await ValueTask.CompletedTask;
		};

		// Act
		await service.RegisterAsync(
			handle,
			ipAddress,
			hostname,
			connectionType,
			outputFunction,
			promptFunction,
			() => Encoding.UTF8,
			() => { });

		// Assert
		var connection = service.Get(handle);
		await Assert.That(connection).IsNotNull();
		await Assert.That(connection!.Handle).IsEqualTo(handle);

		// Test output function
		var testData = Encoding.UTF8.GetBytes("Hello WebSocket");
		await connection.OutputFunction(testData);
		await Assert.That(receivedData).IsNotNull();
		await Assert.That(receivedData).IsEqualTo(testData);

		// Test prompt function
		var testPrompt = Encoding.UTF8.GetBytes("> ");
		await connection.PromptOutputFunction(testPrompt);
		await Assert.That(receivedPrompt).IsNotNull();
		await Assert.That(receivedPrompt).IsEqualTo(testPrompt);
	}

	[Test]
	public async Task CanDisconnectWebSocketConnection()
	{
		// Arrange
		var logger = new LoggerFactory().CreateLogger<ConnectionServerService>();
		var publishEndpoint = Substitute.For<IMessageBus>();
		var service = new ConnectionServerService(logger, publishEndpoint);

		var handle = 1000002L;
		var disconnected = false;

		await service.RegisterAsync(
			handle,
			"192.168.1.1",
			"test.local",
			"websocket",
			async (data) => await ValueTask.CompletedTask,
			async (data) => await ValueTask.CompletedTask,
			() => Encoding.UTF8,
			() => { disconnected = true; });

		// Act
		await service.DisconnectAsync(handle);

		// Assert
		var connection = service.Get(handle);
		await Assert.That(connection).IsNull();
		await Assert.That(disconnected).IsTrue();
	}

	[Test]
	public async Task GetAllReturnsAllConnections()
	{
		// Arrange
		var logger = new LoggerFactory().CreateLogger<ConnectionServerService>();
		var publishEndpoint = Substitute.For<IMessageBus>();
		var service = new ConnectionServerService(logger, publishEndpoint);

		// Register multiple connections
		for (long i = 1000010; i < 1000015; i++)
		{
			await service.RegisterAsync(
				i,
				$"192.168.1.{i % 256}",
				$"host{i}.local",
				"websocket",
				async (data) => await ValueTask.CompletedTask,
				async (data) => await ValueTask.CompletedTask,
				() => Encoding.UTF8,
				() => { });
		}

		// Act
		var allConnections = service.GetAll().ToList();

		// Assert
		await Assert.That(allConnections.Count).IsGreaterThanOrEqualTo(5);
		await Assert.That(allConnections.Any(c => c.Handle == 1000010)).IsTrue();
		await Assert.That(allConnections.Any(c => c.Handle == 1000014)).IsTrue();
	}

	[Test]
	public async Task WebSocketConnectionUsesCorrectConnectionType()
	{
		// Arrange
		var logger = new LoggerFactory().CreateLogger<ConnectionServerService>();
		var publishEndpoint = Substitute.For<IMessageBus>();
		var service = new ConnectionServerService(logger, publishEndpoint);

		var handle = 1000003L;

		// Act
		await service.RegisterAsync(
			handle,
			"192.168.1.100",
			"websocket.test",
			"websocket",  // Connection type
			async (data) => await ValueTask.CompletedTask,
			async (data) => await ValueTask.CompletedTask,
			() => Encoding.UTF8,
			() => { });

		// Assert - verify ConnectionEstablishedMessage was published with correct connection type
		await publishEndpoint.Received(1).Publish(
			Arg.Is<SharpMUSH.Messages.ConnectionEstablishedMessage>(m =>
				m.Handle == handle &&
				m.ConnectionType == "websocket" &&
				m.IpAddress == "192.168.1.100"),
			Arg.Any<CancellationToken>());
	}
}
