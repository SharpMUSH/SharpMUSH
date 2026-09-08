using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.ConnectionServer.Consumers;
using SharpMUSH.ConnectionServer.Models;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Library.Utilities;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Tests.ConnectionServer;

public class UpdatePlayerPreferencesConsumerTests
{
	[Test]
	public async Task PlayerOutputPreferences_PreservesExistingPositionalLocaleParameter()
	{
		var preferences = new PlayerOutputPreferences(true, true, true, "fr");

		await Assert.That(preferences.Locale).IsEqualTo("fr");
		await Assert.That(preferences.TruecolorEnabled).IsFalse();
	}

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

	[Test]
	public async Task ClearMessage_RemovesPlayerPreferencesFromActiveConnection()
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
		connectionService.UpdatePreferences(42, new PlayerOutputPreferences(
			AnsiEnabled: true,
			ColorEnabled: true,
			Xterm256Enabled: true,
			TruecolorEnabled: true));
		var consumer = new UpdatePlayerPreferencesConsumer(
			connectionService, NullLogger<UpdatePlayerPreferencesConsumer>.Instance);

		await consumer.HandleAsync(new ClearPlayerOutputPreferencesMessage(42));

		await Assert.That(connectionService.Get(42)!.Preferences).IsNull();
	}

	/// <summary>
	/// <c>SOCKSET colorstyle</c> used to write engine-side metadata and nothing else, so it changed
	/// what <c>terminfo()</c> and <c>SOCKSET</c> reported and not one byte of what was actually sent.
	/// The pin has to reach the process that renders.
	/// </summary>
	[Test]
	public async Task ColorStyleMessage_PinsAndUnpinsTheStyleOnTheConnection()
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

		await consumer.HandleAsync(new UpdateColorStyleMessage(42, ColorStyles.Plain));

		await Assert.That(connectionService.Get(42)!.Capabilities.ColorStylePin).IsEqualTo(ColorStyles.Plain);

		await consumer.HandleAsync(new UpdateColorStyleMessage(42, null));

		await Assert.That(connectionService.Get(42)!.Capabilities.ColorStylePin).IsNull();
	}

	/// <summary>The pin rides on the capabilities record, so a terminal-type report must not drop it.</summary>
	[Test]
	public async Task ColorStylePin_SurvivesALaterTerminalCapabilityUpdate()
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
		connectionService.UpdateColorStyle(42, ColorStyles.Hilite);

		connectionService.UpdateCapabilities(42, current => current with { SupportsTruecolor = true });

		var capabilities = connectionService.Get(42)!.Capabilities;
		await Assert.That(capabilities.SupportsTruecolor).IsTrue();
		await Assert.That(capabilities.ColorStylePin).IsEqualTo(ColorStyles.Hilite);
	}

	/// <summary>
	/// The capabilities record has several independent writers — terminal-type negotiation, the Pueblo
	/// and MXP format switches, and a colorstyle pin arriving from the engine — and each used to read
	/// it, build a whole replacement, and write that back. A writer that had to retry re-applied a
	/// snapshot taken before the other writer existed, so a pin acknowledged to the player reverted to
	/// automatic rendering because the client finished negotiating in the same instant. The transform
	/// runs against whatever is current on every attempt, so the two changes compose.
	/// </summary>
	[Test]
	public async Task ConcurrentCapabilityWriters_Compose_RatherThanOverwritingEachOther()
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

		var start = new ManualResetEventSlim();
		var writers = new[]
		{
			Task.Run(() => { start.Wait(); connectionService.UpdateColorStyle(42, ColorStyles.Plain); }),
			Task.Run(() =>
			{
				start.Wait();
				connectionService.UpdateCapabilities(42, current => current with { SupportsTruecolor = true });
			}),
			Task.Run(() =>
			{
				start.Wait();
				connectionService.UpdateCapabilities(42, current => current with { Format = OutputFormat.Mxp });
			})
		};
		start.Set();
		await Task.WhenAll(writers);

		var capabilities = connectionService.Get(42)!.Capabilities;
		await Assert.That(capabilities.ColorStylePin).IsEqualTo(ColorStyles.Plain);
		await Assert.That(capabilities.SupportsTruecolor).IsTrue();
		await Assert.That(capabilities.Format).IsEqualTo(OutputFormat.Mxp);
	}

	/// <summary>A transform that changes nothing reports no change, which is what the logging keys on.</summary>
	[Test]
	public async Task UpdateCapabilities_ReportsFalse_ForANoOpTransform()
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

		await Assert.That(connectionService.UpdateCapabilities(42, current => current)).IsFalse();
		await Assert.That(connectionService.UpdateCapabilities(99, current => current)).IsFalse()
			.Because("an unknown handle is not a change either");
	}
}
