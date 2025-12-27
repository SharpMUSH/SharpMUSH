using System.Text;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.ConnectionServer.Consumers;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messages;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>
/// Tests for WebSocket message consumer functionality.
/// Verifies that WebSocket output and prompt messages are correctly processed.
/// </summary>
public class WebSocketConsumerTests
{
	[Test]
	public async Task WebSocketOutputConsumer_SendsDataToConnection()
	{
		// Arrange
		var logger = new LoggerFactory().CreateLogger<WebSocketOutputConsumer>();
		var connectionService = new TestConnectionService();
		var consumer = new WebSocketOutputConsumer(connectionService, logger);

		var handle = 12345L;
		var testMessage = "Hello, WebSocket!";
		
		connectionService.RegisterTestConnection(handle);

		var message = new WebSocketOutputMessage(handle, testMessage);
		var harness = new InMemoryTestHarness();
		var consumerHarness = harness.Consumer(() => consumer);

		try
		{
			await harness.Start();

			// Act
			await harness.InputQueueSendEndpoint.Send(message);

			// Assert
			await Assert.That(await harness.Consumed.Any<WebSocketOutputMessage>()).IsTrue();

			var sentData = connectionService.GetSentData(handle);
			await Assert.That(sentData).IsNotNull();
			
			var sentText = Encoding.UTF8.GetString(sentData!);
			await Assert.That(sentText).IsEqualTo(testMessage);
		}
		finally
		{
			await harness.Stop();
		}
	}

	[Test]
	public async Task WebSocketOutputConsumer_HandlesUnknownConnection()
	{
		// Arrange
		var logger = new LoggerFactory().CreateLogger<WebSocketOutputConsumer>();
		var connectionService = new TestConnectionService();
		var consumer = new WebSocketOutputConsumer(connectionService, logger);

		var handle = 99999L;
		var message = new WebSocketOutputMessage(handle, "Test");
		var harness = new InMemoryTestHarness();
		var consumerHarness = harness.Consumer(() => consumer);

		try
		{
			await harness.Start();

			// Act & Assert - Should not throw
			await harness.InputQueueSendEndpoint.Send(message);
			
			await Assert.That(await harness.Consumed.Any<WebSocketOutputMessage>()).IsTrue();
		}
		finally
		{
			await harness.Stop();
		}
	}

	[Test]
	public async Task WebSocketPromptConsumer_SendsDataToConnection()
	{
		// Arrange
		var logger = new LoggerFactory().CreateLogger<WebSocketPromptConsumer>();
		var connectionService = new TestConnectionService();
		var consumer = new WebSocketPromptConsumer(connectionService, logger);

		var handle = 12346L;
		var testPrompt = "> ";
		
		connectionService.RegisterTestConnection(handle);

		var message = new WebSocketPromptMessage(handle, testPrompt);
		var harness = new InMemoryTestHarness();
		var consumerHarness = harness.Consumer(() => consumer);

		try
		{
			await harness.Start();

			// Act
			await harness.InputQueueSendEndpoint.Send(message);

			// Assert
			await Assert.That(await harness.Consumed.Any<WebSocketPromptMessage>()).IsTrue();

			var sentData = connectionService.GetPromptData(handle);
			await Assert.That(sentData).IsNotNull();
			
			var sentText = Encoding.UTF8.GetString(sentData!);
			await Assert.That(sentText).IsEqualTo(testPrompt);
		}
		finally
		{
			await harness.Stop();
		}
	}

	[Test]
	public async Task WebSocketPromptConsumer_HandlesUnknownConnection()
	{
		// Arrange
		var logger = new LoggerFactory().CreateLogger<WebSocketPromptConsumer>();
		var connectionService = new TestConnectionService();
		var consumer = new WebSocketPromptConsumer(connectionService, logger);

		var handle = 99999L;
		var message = new WebSocketPromptMessage(handle, "> ");
		var harness = new InMemoryTestHarness();
		var consumerHarness = harness.Consumer(() => consumer);

		try
		{
			await harness.Start();

			// Act & Assert - Should not throw
			await harness.InputQueueSendEndpoint.Send(message);
			
			await Assert.That(await harness.Consumed.Any<WebSocketPromptMessage>()).IsTrue();
		}
		finally
		{
			await harness.Stop();
		}
	}

	/// <summary>
	/// Test implementation of IConnectionServerService for testing
	/// </summary>
	private class TestConnectionService : IConnectionServerService
	{
		private readonly Dictionary<long, TestConnectionData> _connections = new();

		public void RegisterTestConnection(long handle)
		{
			_connections[handle] = new TestConnectionData();
		}

		public byte[]? GetSentData(long handle)
		{
			return _connections.TryGetValue(handle, out var conn) ? conn.SentData : null;
		}

		public byte[]? GetPromptData(long handle)
		{
			return _connections.TryGetValue(handle, out var conn) ? conn.PromptData : null;
		}

		public Task RegisterAsync(long handle, string ipAddress, string hostname, string connectionType,
			Func<byte[], ValueTask> outputFunction, Func<byte[], ValueTask> promptOutputFunction,
			Func<Encoding> encodingFunction, Action disconnectFunction)
		{
			_connections[handle] = new TestConnectionData
			{
				OutputFunction = outputFunction,
				PromptOutputFunction = promptOutputFunction
			};
			return Task.CompletedTask;
		}

		public Task DisconnectAsync(long handle)
		{
			_connections.Remove(handle);
			return Task.CompletedTask;
		}

		public ConnectionServerService.ConnectionData? Get(long handle)
		{
			if (!_connections.TryGetValue(handle, out var testData))
				return null;

			return new ConnectionServerService.ConnectionData(
				handle,
				null,
				ConnectionServerService.ConnectionState.Connected,
				async data =>
				{
					testData.SentData = data;
					await ValueTask.CompletedTask;
				},
				async data =>
				{
					testData.PromptData = data;
					await ValueTask.CompletedTask;
				},
				() => Encoding.UTF8,
				() => { });
		}

		public IEnumerable<ConnectionServerService.ConnectionData> GetAll()
		{
			return _connections.Select(kvp => Get(kvp.Key)!);
		}

		private class TestConnectionData
		{
			public byte[]? SentData { get; set; }
			public byte[]? PromptData { get; set; }
			public Func<byte[], ValueTask>? OutputFunction { get; set; }
			public Func<byte[], ValueTask>? PromptOutputFunction { get; set; }
		}
	}
}
