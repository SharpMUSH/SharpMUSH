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

		await service.RegisterAsync(
			handle,
			ipAddress,
			hostname,
			connectionType,
			outputFunction,
			promptFunction,
			() => Encoding.UTF8,
			() => { });

		var connection = service.Get(handle);
		await Assert.That(connection).IsNotNull();
		await Assert.That(connection!.Handle).IsEqualTo(handle);

		var testData = Encoding.UTF8.GetBytes("Hello WebSocket");
		await connection.OutputFunction(testData);
		await Assert.That(receivedData).IsNotNull();
		await Assert.That(receivedData).IsEqualTo(testData);

		var testPrompt = Encoding.UTF8.GetBytes("> ");
		await connection.PromptOutputFunction(testPrompt);
		await Assert.That(receivedPrompt).IsNotNull();
		await Assert.That(receivedPrompt).IsEqualTo(testPrompt);
	}

	[Test]
	public async Task CanDisconnectWebSocketConnection()
	{
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

		await service.DisconnectAsync(handle);

		var connection = service.Get(handle);
		await Assert.That(connection).IsNull();
		await Assert.That(disconnected).IsTrue();
	}

	[Test]
	public async Task GetAllReturnsAllConnections()
	{
		var logger = new LoggerFactory().CreateLogger<ConnectionServerService>();
		var publishEndpoint = Substitute.For<IMessageBus>();
		var service = new ConnectionServerService(logger, publishEndpoint);

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

		var allConnections = service.GetAll().ToList();

		await Assert.That(allConnections.Count).IsGreaterThanOrEqualTo(5);
		await Assert.That(allConnections.Any(c => c.Handle == 1000010)).IsTrue();
		await Assert.That(allConnections.Any(c => c.Handle == 1000014)).IsTrue();
	}

	[Test]
	public async Task WebSocketConnectionUsesCorrectConnectionType()
	{
		var logger = new LoggerFactory().CreateLogger<ConnectionServerService>();
		var publishEndpoint = Substitute.For<IMessageBus>();
		var service = new ConnectionServerService(logger, publishEndpoint);

		var handle = 1000003L;

		await service.RegisterAsync(
			handle,
			"192.168.1.100",
			"websocket.test",
			"websocket",
			async (data) => await ValueTask.CompletedTask,
			async (data) => await ValueTask.CompletedTask,
			() => Encoding.UTF8,
			() => { });

		// verify ConnectionEstablishedMessage was published with correct connection type
		await publishEndpoint.Received(1).Publish(
			Arg.Is<SharpMUSH.Messaging.Messages.ConnectionEstablishedMessage>(m =>
				m.Handle == handle &&
				m.ConnectionType == "websocket" &&
				m.IpAddress == "192.168.1.100"),
			Arg.Any<CancellationToken>());
	}
}
