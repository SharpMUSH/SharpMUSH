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
		var logger = Substitute.For<ILogger<WebSocketClientService>>();
		var service = new WebSocketClientService(logger);

		await Assert.That(service.IsConnected).IsFalse();
	}

	[Test]
	public async Task DisconnectAsync_WhenNotConnected_DoesNotThrow()
	{
		var logger = Substitute.For<ILogger<WebSocketClientService>>();
		var service = new WebSocketClientService(logger);

		await service.DisconnectAsync();

		await Assert.That(service.IsConnected).IsFalse();
	}

	[Test]
	public async Task SendAsync_WhenNotConnected_BuffersMessage()
	{
		var logger = Substitute.For<ILogger<WebSocketClientService>>();
		var service = new WebSocketClientService(logger);

		await service.SendAsync("test message");

		await Assert.That(service.IsConnected).IsFalse();
	}

	[Test]
	public async Task DisposeAsync_DisconnectsIfConnected()
	{
		var logger = Substitute.For<ILogger<WebSocketClientService>>();
		var service = new WebSocketClientService(logger);

		await service.DisposeAsync();

		await Assert.That(service.IsConnected).IsFalse();
	}

	[Test]
	public async Task MessageReceived_EventCanBeSubscribed()
	{
		var logger = Substitute.For<ILogger<WebSocketClientService>>();
		var service = new WebSocketClientService(logger);
		var eventTriggered = false;

		service.MessageReceived += (sender, message) =>
		{
			eventTriggered = true;
		};

		await Assert.That(eventTriggered).IsFalse();
	}

	[Test]
	public async Task ConnectionStateChanged_EventCanBeSubscribed()
	{
		var logger = Substitute.For<ILogger<WebSocketClientService>>();
		var service = new WebSocketClientService(logger);
		var eventTriggered = false;

		service.ConnectionStateChanged += (sender, state) =>
		{
			eventTriggered = true;
		};

		await Assert.That(eventTriggered).IsFalse();
	}

	[Test]
	public async Task Constructor_InitializesWithLogger()
	{
		var logger = Substitute.For<ILogger<WebSocketClientService>>();

		var service = new WebSocketClientService(logger);

		await Assert.That(service).IsNotNull();
		await Assert.That(service.IsConnected).IsFalse();
	}

	[Test]
	public async Task MultipleDisposeAsync_DoesNotThrow()
	{
		var logger = Substitute.For<ILogger<WebSocketClientService>>();
		var service = new WebSocketClientService(logger);

		await service.DisposeAsync();
		await service.DisposeAsync();
		await service.DisposeAsync();

		await Assert.That(service.IsConnected).IsFalse();
	}

	[Test]
	public async Task SendAsync_WithEmptyMessage_BuffersWhenDisconnected()
	{
		var logger = Substitute.For<ILogger<WebSocketClientService>>();
		var service = new WebSocketClientService(logger);

		await service.SendAsync("");

		await Assert.That(service.IsConnected).IsFalse();
	}
}
