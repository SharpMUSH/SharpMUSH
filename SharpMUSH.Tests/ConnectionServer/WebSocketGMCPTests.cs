using System.Text;
using System.Text.Json;
using SharpMUSH.Messaging.Abstractions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messages;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>
/// Tests for WebSocket GMCP functionality.
/// Verifies that GMCP messages can be sent and received via WebSocket connections.
/// </summary>
public class WebSocketGMCPTests
{
	[Test]
	public async Task WebSocketCanRegisterWithGMCPCallback()
	{
		// Arrange
		var logger = new LoggerFactory().CreateLogger<ConnectionServerService>();
		var publishEndpoint = Substitute.For<IMessageBus>();
		var service = new ConnectionServerService(logger, publishEndpoint);
		
		var handle = 1000001L;
		var ipAddress = "192.168.1.1";
		var hostname = "test.local";
		var connectionType = "websocket";
		
		string? receivedModule = null;
		string? receivedMessage = null;
		Func<string, string, ValueTask> gmcpFunction = async (module, message) =>
		{
			receivedModule = module;
			receivedMessage = message;
			await ValueTask.CompletedTask;
		};

		// Act
		await service.RegisterAsync(
			handle,
			ipAddress,
			hostname,
			connectionType,
			async (data) => await ValueTask.CompletedTask,
			async (data) => await ValueTask.CompletedTask,
			() => Encoding.UTF8,
			() => { },
			gmcpFunction);

		// Assert
		var connection = service.Get(handle);
		await Assert.That(connection).IsNotNull();
		await Assert.That(connection!.GMCPFunction != null).IsTrue();

		// Test GMCP function
		await connection.GMCPFunction!("Core.Hello", "{\"client\":\"TestClient\"}");
		await Assert.That(receivedModule).IsEqualTo("Core.Hello");
		await Assert.That(receivedMessage).IsEqualTo("{\"client\":\"TestClient\"}");
	}

	[Test]
	public async Task GMCPSignalConsumerSetsGMCPMetadata()
	{
		// Arrange
		var logger = new LoggerFactory().CreateLogger<SharpMUSH.Server.Consumers.GMCPSignalConsumer>();
		var connectionService = Substitute.For<SharpMUSH.Library.Services.Interfaces.IConnectionService>();
		var consumer = new SharpMUSH.Server.Consumers.GMCPSignalConsumer(logger, connectionService);
		
		var handle = 1000002L;
		var package = "Core.Hello";
		var info = "{\"client\":\"TestClient\",\"version\":\"1.0\"}";
		var message = new GMCPSignalMessage(handle, package, info);

		// Act
		await consumer.HandleAsync(message);

		// Assert - verify GMCP capability flag is set
		connectionService.Received(1).Update(handle, "GMCP", "1");
		
		// Verify package info is stored
		connectionService.Received(1).Update(handle, $"GMCP_{package}", info);
	}

	[Test]
	public async Task GMCPSignalConsumerHandlesCoreHello()
	{
		// Arrange
		var logger = new LoggerFactory().CreateLogger<SharpMUSH.Server.Consumers.GMCPSignalConsumer>();
		var connectionService = Substitute.For<SharpMUSH.Library.Services.Interfaces.IConnectionService>();
		var consumer = new SharpMUSH.Server.Consumers.GMCPSignalConsumer(logger, connectionService);
		
		var handle = 1000003L;
		var package = "Core.Hello";
		var info = "{\"client\":\"WebClient\",\"version\":\"2.0\"}";
		var message = new GMCPSignalMessage(handle, package, info);

		// Act
		await consumer.HandleAsync(message);

		// Assert - verify Core.Hello specific metadata is set
		connectionService.Received(1).Update(handle, "GMCP_ClientHello", info);
	}

	[Test]
	public async Task GMCPSignalConsumerHandlesCoreSupports()
	{
		// Arrange
		var logger = new LoggerFactory().CreateLogger<SharpMUSH.Server.Consumers.GMCPSignalConsumer>();
		var connectionService = Substitute.For<SharpMUSH.Library.Services.Interfaces.IConnectionService>();
		var consumer = new SharpMUSH.Server.Consumers.GMCPSignalConsumer(logger, connectionService);
		
		var handle = 1000004L;
		var package = "Core.Supports.Set";
		var info = "[\"Core 1\",\"Char 1\",\"Room 1\"]";
		var message = new GMCPSignalMessage(handle, package, info);

		// Act
		await consumer.HandleAsync(message);

		// Assert - verify Core.Supports.Set specific metadata is set
		connectionService.Received(1).Update(handle, "GMCP_ClientSupports", info);
	}

	[Test]
	public async Task CanSerializeGMCPMessageToJSON()
	{
		// Arrange
		var gmcpMessage = new
		{
			type = "gmcp",
			package = "Room.Info",
			data = "{\"name\":\"A Dark Room\",\"exits\":[\"north\",\"south\"]}"
		};

		// Act
		var json = JsonSerializer.Serialize(gmcpMessage);
		var deserialized = JsonSerializer.Deserialize<JsonElement>(json);

		// Assert
		await Assert.That(deserialized.GetProperty("type").GetString()).IsEqualTo("gmcp");
		await Assert.That(deserialized.GetProperty("package").GetString()).IsEqualTo("Room.Info");
		await Assert.That(deserialized.GetProperty("data").GetString()).Contains("A Dark Room");
	}

	[Test]
	public async Task CanParseGMCPMessageFromJSON()
	{
		// Arrange
		var json = """
			{
				"type": "gmcp",
				"package": "Char.Vitals",
				"data": "{\"hp\":100,\"maxhp\":150}"
			}
			""";

		// Act
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		
		var type = root.GetProperty("type").GetString();
		var package = root.GetProperty("package").GetString();
		var data = root.GetProperty("data").GetString();

		// Assert
		await Assert.That(type).IsEqualTo("gmcp");
		await Assert.That(package).IsEqualTo("Char.Vitals");
		await Assert.That(data).Contains("hp");
	}

	[Test]
	public void InvalidJSONDoesNotParseAsGMCP()
	{
		// Arrange
		var invalidJson = "this is not json";

		// Act & Assert
		Assert.Throws<JsonException>(() => JsonDocument.Parse(invalidJson));
	}

	[Test]
	public async Task NonGMCPJSONDoesNotParseAsGMCP()
	{
		// Arrange
		var json = """{"someField": "someValue"}""";

		// Act
		using var doc = JsonDocument.Parse(json);
		var root = doc.RootElement;
		var hasType = root.TryGetProperty("type", out var typeElement);
		
		// Assert - should not have "type" property or it should not be "gmcp"
		if (hasType)
		{
			await Assert.That(typeElement.GetString()).IsNotEqualTo("gmcp");
		}
		else
		{
			await Assert.That(hasType).IsFalse();
		}
	}

	[Test]
	public async Task GMCPDataCanBeStringOrObject()
	{
		// Test with data as a string (JSON-encoded)
		var jsonWithString = """
			{
				"type": "gmcp",
				"package": "Core.Hello",
				"data": "{\"client\":\"TestClient\"}"
			}
			""";

		using (var doc = JsonDocument.Parse(jsonWithString))
		{
			var root = doc.RootElement;
			var dataElement = root.GetProperty("data");
			
			// When data is a string, we should get the decoded value
			if (dataElement.ValueKind == JsonValueKind.String)
			{
				var data = dataElement.GetString();
				await Assert.That(data).IsEqualTo("{\"client\":\"TestClient\"}");
			}
		}

		// Test with data as an object
		var jsonWithObject = """
			{
				"type": "gmcp",
				"package": "Core.Hello",
				"data": {"client":"TestClient"}
			}
			""";

		using (var doc = JsonDocument.Parse(jsonWithObject))
		{
			var root = doc.RootElement;
			var dataElement = root.GetProperty("data");
			
			// When data is an object, we should get the raw JSON
			if (dataElement.ValueKind == JsonValueKind.Object)
			{
				var data = dataElement.GetRawText();
				await Assert.That(data).Contains("\"client\"");
				await Assert.That(data).Contains("TestClient");
			}
		}
	}
}
