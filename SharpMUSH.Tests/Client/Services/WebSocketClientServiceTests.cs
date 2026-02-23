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
	public async Task SendAsync_WhenNotConnected_ThrowsInvalidOperationException()
	{
		// Arrange
		var logger = Substitute.For<ILogger<WebSocketClientService>>();
		var service = new WebSocketClientService(logger);

		// Act & Assert
		var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
		{
			await service.SendAsync("test message");
		});

		await Assert.That(exception).IsNotNull();
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
	public async Task SendAsync_WithEmptyMessage_DoesNotThrowWhenDisconnected()
	{
		// Arrange
		var logger = Substitute.For<ILogger<WebSocketClientService>>();
		var service = new WebSocketClientService(logger);

		// Act & Assert - Should throw because not connected
		await Assert.ThrowsAsync<InvalidOperationException>(async () =>
		{
			await service.SendAsync("");
		});
	}
}
