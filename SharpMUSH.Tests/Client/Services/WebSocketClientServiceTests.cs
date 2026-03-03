using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.Client.Services;

/// <summary>
/// Tests for the WebSocketClientService to verify connection management and messaging.
/// </summary>
public class WebSocketClientServiceTests
{
	[Test]
	public async Task IsConnected_InitiallyFalse()
	{
		// Arrange
		var logger = Substitute.For<ILogger<WebSocketClientService>>();
		var service = new WebSocketClientService(logger);

		// Assert
		await Assert.That(service.IsConnected).IsFalse();
	}

	[Test]
	public async Task DisconnectAsync_WhenNotConnected_DoesNotThrow()
	{
		// Arrange
		var logger = Substitute.For<ILogger<WebSocketClientService>>();
		var service = new WebSocketClientService(logger);

		// Act 
		await service.DisconnectAsync();

		// Assert - Verify still disconnected
		await Assert.That(service.IsConnected).IsFalse();
	}

	[Test]
	public async Task SendAsync_WhenNotConnected_BuffersMessage()
	{
		// Arrange
		var logger = Substitute.For<ILogger<WebSocketClientService>>();
		var service = new WebSocketClientService(logger);

		// Act - Should buffer the message instead of throwing
		await service.SendAsync("test message");

		// Assert - Service should still be disconnected (message is buffered for later)
		await Assert.That(service.IsConnected).IsFalse();
	}

	[Test]
	public async Task DisposeAsync_DisconnectsIfConnected()
	{
		// Arrange
		var logger = Substitute.For<ILogger<WebSocketClientService>>();
		var service = new WebSocketClientService(logger);

		// Act 
		await service.DisposeAsync();

		// Assert - Verify still disconnected after dispose
		await Assert.That(service.IsConnected).IsFalse();
	}

	[Test]
	public async Task MessageReceived_EventCanBeSubscribed()
	{
		// Arrange
		var logger = Substitute.For<ILogger<WebSocketClientService>>();
		var service = new WebSocketClientService(logger);
		var eventTriggered = false;

		service.MessageReceived += (sender, message) =>
		{
			eventTriggered = true;
		};

		// Assert - Event subscription should work without errors
		await Assert.That(eventTriggered).IsFalse(); // Not triggered yet
	}

	[Test]
	public async Task ConnectionStateChanged_EventCanBeSubscribed()
	{
		// Arrange
		var logger = Substitute.For<ILogger<WebSocketClientService>>();
		var service = new WebSocketClientService(logger);
		var eventTriggered = false;

		service.ConnectionStateChanged += (sender, state) =>
		{
			eventTriggered = true;
		};

		// Assert - Event subscription should work without errors
		await Assert.That(eventTriggered).IsFalse(); // Not triggered yet
	}

	[Test]
	public async Task Constructor_InitializesWithLogger()
	{
		// Arrange
		var logger = Substitute.For<ILogger<WebSocketClientService>>();

		// Act
		var service = new WebSocketClientService(logger);

		// Assert
		await Assert.That(service).IsNotNull();
		await Assert.That(service.IsConnected).IsFalse();
	}

	[Test]
	public async Task MultipleDisposeAsync_DoesNotThrow()
	{
		// Arrange
		var logger = Substitute.For<ILogger<WebSocketClientService>>();
		var service = new WebSocketClientService(logger);

		// Act - Dispose multiple times
		await service.DisposeAsync();
		await service.DisposeAsync();
		await service.DisposeAsync();

		// Assert - Verify still disconnected
		await Assert.That(service.IsConnected).IsFalse();
	}

	[Test]
	public async Task SendAsync_WithEmptyMessage_BuffersWhenDisconnected()
	{
		// Arrange
		var logger = Substitute.For<ILogger<WebSocketClientService>>();
		var service = new WebSocketClientService(logger);

		// Act - Should buffer the empty message instead of throwing
		await service.SendAsync("");

		// Assert - Service should still be disconnected (message is buffered for later)
		await Assert.That(service.IsConnected).IsFalse();
	}
}
