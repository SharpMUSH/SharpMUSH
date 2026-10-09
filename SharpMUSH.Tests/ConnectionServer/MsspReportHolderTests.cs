using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.SocketServer.Services;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>The connection server answers the MSSP telnet option with the report the main process sent.</summary>
public class MsspReportHolderTests
{
	[Test]
	public async Task TheTelnetOptionReportsWhatTheMainProcessSent()
	{
		var holder = new MsspReportHolder();
		await new MSSPReportConsumer(holder).HandleAsync(new MSSPReportMessage(
		[
			new MSSPVariable("NAME", ["Test Game"]),
			new MSSPVariable("CHARSET", ["ISO-8859-1", "UTF-8"])
		]));

		var variables = holder.Current.Variables;
		await Assert.That(variables["NAME"]).IsEquivalentTo(new[] { "Test Game" });
		await Assert.That(variables["CHARSET"]).IsEquivalentTo(new[] { "ISO-8859-1", "UTF-8" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task UntilAReportArrivesTheNameIsStillReported()
	{
		var holder = new MsspReportHolder();

		await Assert.That(holder.Current.Name).IsEqualTo("SharpMUSH");

		holder.Replace([]);
		await Assert.That(holder.Current.Name).IsEqualTo("SharpMUSH");
	}

	/// <summary>
	/// The first request can be answered before this server's consumer exists, and an unchanged report is
	/// not sent again, so the request is repeated until a report arrives, and then no more.
	/// </summary>
	[Test]
	public async Task TheReportIsAskedForAgainUntilOneArrives()
	{
		var holder = new MsspReportHolder();
		var settings = new OutputSettingsHolder();
		var bus = Substitute.For<IMessageBus>();
		var asked = 0;
		bus.Publish(Arg.Any<MSSPReportRequestMessage>(), Arg.Any<CancellationToken>())
			.Returns(_ =>
			{
				if (Interlocked.Increment(ref asked) == 2)
				{
					holder.Replace([new MSSPVariable("NAME", ["Test Game"])]);
					settings.Replace(new OutputSettingsMessage("·=-"));
				}

				return Task.CompletedTask;
			});
		using var service = new MsspReportRequestService(bus, holder, settings, NullLogger<MsspReportRequestService>.Instance);

		await service.StartAsync(CancellationToken.None);
		await holder.Received.WaitAsync(TimeSpan.FromSeconds(10));
		await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
		await service.StopAsync(CancellationToken.None);

		await Assert.That(asked).IsEqualTo(2);
		await Assert.That(holder.Current.Variables["NAME"]).IsEquivalentTo(new[] { "Test Game" });
	}

	/// <summary>
	/// The settings and the report reach this server through consumers of their own, so the report arriving
	/// first does not stop the asking while the settings are still missed.
	/// </summary>
	[Test]
	public async Task TheSettingsAreAskedForUntilTheyArriveToo()
	{
		var holder = new MsspReportHolder();
		var settings = new OutputSettingsHolder();
		var bus = Substitute.For<IMessageBus>();
		var asked = 0;
		bus.Publish(Arg.Any<MSSPReportRequestMessage>(), Arg.Any<CancellationToken>())
			.Returns(_ =>
			{
				var count = Interlocked.Increment(ref asked);
				if (count == 1) holder.Replace([new MSSPVariable("NAME", ["Test Game"])]);
				if (count == 2) settings.Replace(new OutputSettingsMessage("·=-"));
				return Task.CompletedTask;
			});
		using var service = new MsspReportRequestService(bus, holder, settings, NullLogger<MsspReportRequestService>.Instance);

		await service.StartAsync(CancellationToken.None);
		await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
		await service.StopAsync(CancellationToken.None);

		await Assert.That(asked).IsEqualTo(2);
		await Assert.That(settings.AsciiTranslations).IsEqualTo("·=-");
	}
}
