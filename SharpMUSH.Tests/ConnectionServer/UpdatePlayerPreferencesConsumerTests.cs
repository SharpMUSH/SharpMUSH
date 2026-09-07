using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.ConnectionServer.Consumers;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Tests.ConnectionServer;

public class UpdatePlayerPreferencesConsumerTests
{
	[Test]
	public async Task HandleAsync_UpdatesAllPlayerColorFlagsForActiveConnection()
	{
		var bus = Substitute.For<IMessageBus>();
		var connectionService = new ConnectionServerService(
			NullLogger<ConnectionServerService>.Instance, bus);
		await connectionService.RegisterAsync(
			42,
			"127.0.0.1",
			"localhost",
			"telnet",
			_ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask,
			() => Encoding.UTF8,
			() => { });
		var consumer = new UpdatePlayerPreferencesConsumer(
			connectionService, NullLogger<UpdatePlayerPreferencesConsumer>.Instance);

		await consumer.HandleAsync(new UpdatePlayerPreferencesMessage(
			42,
			AnsiEnabled: true,
			ColorEnabled: true,
			Xterm256Enabled: true,
			TruecolorEnabled: true));

		var preferences = connectionService.Get(42)!.Preferences;
		await Assert.That(preferences).IsNotNull();
		await Assert.That(preferences!.AnsiEnabled).IsTrue();
		await Assert.That(preferences.ColorEnabled).IsTrue();
		await Assert.That(preferences.Xterm256Enabled).IsTrue();
		await Assert.That(preferences.TruecolorEnabled).IsTrue();
	}
}
