using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>
/// The presence-class plumbing: every connection declares "play" (a real interactive session) or
/// "portal" (a background query connection). The default is "play" everywhere, so telnet and older
/// publishers keep counting as normal sessions.
/// </summary>
public class PresenceClassPlumbingTests
{
	[Test]
	public async Task ConnectionEstablishedMessage_defaults_presence_class_to_play()
	{
		var message = new ConnectionEstablishedMessage(1, "1.2.3.4", "host", "telnet", DateTimeOffset.UtcNow);
		await Assert.That(message.PresenceClass).IsEqualTo("play");
	}

	[Test]
	public async Task ConnectionEstablishedMessage_carries_an_explicit_presence_class()
	{
		var message = new ConnectionEstablishedMessage(1, "1.2.3.4", "host", "websocket", DateTimeOffset.UtcNow, "portal");
		await Assert.That(message.PresenceClass).IsEqualTo("portal");
	}

	[Test]
	public async Task ConnectionData_presence_class_defaults_to_play_when_metadata_is_absent()
	{
		var metadata = new ConcurrentDictionary<string, string>
		{
			["ConnectionType"] = "telnet"
		};
		var data = new IConnectionService.ConnectionData(
			1, null, IConnectionService.ConnectionState.Connected,
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8, metadata);

		await Assert.That(data.PresenceClass).IsEqualTo("play");
	}

	[Test]
	public async Task ConnectionData_presence_class_reflects_metadata_when_present()
	{
		var metadata = new ConcurrentDictionary<string, string>
		{
			["ConnectionType"] = "websocket",
			["PresenceClass"] = "portal"
		};
		var data = new IConnectionService.ConnectionData(
			1, null, IConnectionService.ConnectionState.Connected,
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8, metadata);

		await Assert.That(data.PresenceClass).IsEqualTo("portal");
	}

	[Test]
	public async Task RegisterAsync_publishes_the_presence_class_on_the_established_message()
	{
		var bus = Substitute.For<IMessageBus>();
		var service = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus);

		await service.RegisterAsync(
			42, "1.2.3.4", "host", "websocket",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8, () => { },
			presenceClass: "portal");

		await bus.Received(1).Publish(
			Arg.Is<ConnectionEstablishedMessage>(m => m.Handle == 42 && m.PresenceClass == "portal"),
			Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task RegisterAsync_defaults_the_published_presence_class_to_play()
	{
		var bus = Substitute.For<IMessageBus>();
		var service = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus);

		await service.RegisterAsync(
			43, "1.2.3.4", "host", "telnet",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8, () => { });

		await bus.Received(1).Publish(
			Arg.Is<ConnectionEstablishedMessage>(m => m.Handle == 43 && m.PresenceClass == "play"),
			Arg.Any<CancellationToken>());
	}
}
