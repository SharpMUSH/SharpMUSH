using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.Utilities;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.SocketServer.Consumers;
using SharpMUSH.SocketServer.Models;
using SharpMUSH.SocketServer.ProtocolHandlers;
using SharpMUSH.SocketServer.Services;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>
/// The player flags that change what a connection is sent (#2102): NOACCENTS writes it in ASCII, and KEEPALIVE
/// sends a quiet telnet connection a NOP after <c>keepalive_timeout</c>.
/// </summary>
public class PlayerFlagCapabilityTests
{
	private static async Task<ConnectionServerService> ConnectionAsync(long handle)
	{
		var connections = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, Substitute.For<IMessageBus>());
		await connections.RegisterAsync(handle, "127.0.0.1", "localhost", "telnet",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8, () => { },
			capabilities: new ProtocolCapabilities(Charset: TerminalCharsets.Utf8));
		return connections;
	}

	[Test]
	public async Task TheFlagsSetTheCapabilitiesAndAClearDropsThem()
	{
		var connections = await ConnectionAsync(7);
		var consumer = new UpdatePlayerPreferencesConsumer(connections, NullLogger<UpdatePlayerPreferencesConsumer>.Instance);

		await consumer.HandleAsync(new UpdatePlayerPreferencesMessage(7, true, true, false, NoAccents: true, KeepAlive: true));
		var flagged = connections.Get(7)!.Capabilities;

		await consumer.HandleAsync(new ClearPlayerOutputPreferencesMessage(7));
		var cleared = connections.Get(7)!.Capabilities;

		await Assert.That(flagged.NoAccents).IsTrue();
		await Assert.That(flagged.KeepAlive).IsTrue();
		await Assert.That(flagged.Utf8).IsFalse();
		await Assert.That(cleared.NoAccents).IsFalse();
		await Assert.That(cleared.KeepAlive).IsFalse();
		await Assert.That(cleared.Utf8).IsTrue();
	}

	[Test]
	public async Task NoAccents_KeepsTheOtherPins()
	{
		var capabilities = new ProtocolCapabilities(Charset: TerminalCharsets.Utf8, NoAccents: true,
			Pins: TerminalPins.None with { ScreenReader = true });

		await Assert.That(capabilities.Utf8).IsFalse();
		await Assert.That(capabilities.ReadsAloud).IsTrue();
	}

	[Test]
	[Arguments(true, true, 300, true)]
	[Arguments(true, true, 299, false)]
	[Arguments(false, true, 900, false)]
	[Arguments(true, false, 900, false)]
	public async Task KeepAliveIsDueOnlyToAQuietKeepAliveTelnetConnection(bool keepAlive, bool telnet, int silentSeconds,
		bool due)
	{
		var capabilities = new ProtocolCapabilities(KeepAlive: keepAlive, Telnet: telnet);

		await Assert.That(TelnetServer.KeepAliveDue(capabilities, TimeSpan.FromSeconds(silentSeconds), TimeSpan.FromSeconds(300)))
			.IsEqualTo(due);
	}

	[Test]
	public async Task KeepAliveIsNotDueToAnUnknownConnection()
		=> await Assert.That(TelnetServer.KeepAliveDue(null, TimeSpan.FromHours(1), TimeSpan.FromSeconds(300))).IsFalse();

	[Test]
	public async Task TheKeepAliveTimeoutComesFromTheMainProcess()
	{
		var settings = new OutputSettingsHolder();
		var before = settings.KeepAlive;

		settings.Replace(new OutputSettingsMessage(new Dictionary<string, string>(), KeepAliveSeconds: 120));
		var sent = settings.KeepAlive;

		settings.Replace(new OutputSettingsMessage(new Dictionary<string, string>()));

		await Assert.That(before).IsEqualTo(OutputSettingsHolder.DefaultKeepAlive);
		await Assert.That(sent).IsEqualTo(TimeSpan.FromSeconds(120));
		await Assert.That(settings.KeepAlive).IsEqualTo(TimeSpan.FromSeconds(120)).Because("an older server sends none");
	}
}
