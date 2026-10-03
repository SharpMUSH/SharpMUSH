using NSubstitute;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The real <see cref="NotifyService"/>'s copy of a portal command's output (<see cref="ICommandOutputCapture"/>).
/// Unlike the HTTP capture it takes nothing away: the character the command ran as is a player who may be
/// connected elsewhere, and still hears every line.
/// </summary>
public class NotifyServiceCommandCaptureTests
{
	private static (NotifyService Service, IConnectionService Connections) BuildService()
	{
		var connections = Substitute.For<IConnectionService>();
		connections.Get(Arg.Any<DBRef>()).Returns(AsyncEnumerable.Empty<IConnectionService.ConnectionData>());
		var localization = Substitute.For<ILocalizationService>();
		localization.Format(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<object[]>()).Returns("localized line");
		var service = new NotifyService(Substitute.For<IMessageBus>(), connections, localization, DisabledRealityPolicy.Instance,
			listenerRoutingService: null, mediator: null, httpOutputCapture: null, configuration: null,
			commandOutputCapture: new CommandOutputCapture());
		return (service, connections);
	}

	[Test]
	public async Task OutputToTheCapturedCharacter_IsCopied_AndStillDelivered()
	{
		var (service, connections) = BuildService();
		var transcript = new CommandTranscript(1000);
		var character = new DBRef(42, null);

		using (new CommandOutputCapture().BeginCapture(42, transcript))
		{
			await service.Notify(character, "first", sender: null);
			await service.NotifyLocalized(character, "AnyKey", sender: null);
		}

		await Assert.That(transcript.Lines).IsEquivalentTo(["first", "localized line"]);
		connections.Received(2).Get(character);
	}

	[Test]
	public async Task OutputToSomeoneElse_OrAfterTheCapture_IsNotCopied()
	{
		var (service, _) = BuildService();
		var transcript = new CommandTranscript(1000);

		using (new CommandOutputCapture().BeginCapture(42, transcript))
		{
			await service.Notify(new DBRef(43, null), "to someone else", sender: null);
		}
		await service.Notify(new DBRef(42, null), "after the command", sender: null);

		await Assert.That(transcript.Lines).IsEmpty();
	}

	[Test]
	public async Task ATranscriptPastItsLimit_KeepsWhatFit_AndSaysItWasTruncated()
	{
		var transcript = new CommandTranscript(10);

		transcript.Append("12345");
		transcript.Append("1234567");
		transcript.Append("1");

		await Assert.That(transcript.Lines).IsEquivalentTo(["12345"]);
		await Assert.That(transcript.Truncated).IsTrue();
	}
}
