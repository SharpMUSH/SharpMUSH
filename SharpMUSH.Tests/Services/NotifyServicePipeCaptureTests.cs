using NSubstitute;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The real <see cref="NotifyService"/>'s handling of a piped command's output (<see cref="IPipeOutputCapture"/>):
/// output to the executor is taken for the next command's <c>%|</c>, markup kept, and reaches no connection.
/// </summary>
public class NotifyServicePipeCaptureTests
{
	private static (NotifyService Service, IConnectionService Connections) BuildService()
	{
		var connections = Substitute.For<IConnectionService>();
		connections.Get(Arg.Any<DBRef>()).Returns(AsyncEnumerable.Empty<IConnectionService.ConnectionData>());
		var localization = Substitute.For<ILocalizationService>();
		localization.Format(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<object[]>()).Returns("localized line");
		var service = new NotifyService(Substitute.For<IMessageBus>(), connections, localization, DisabledRealityPolicy.Instance,
			pipeOutputCapture: new PipeOutputCapture());
		return (service, connections);
	}

	[Test]
	public async Task OutputToThePipedExecutor_IsTaken_AndNotDelivered()
	{
		var (service, connections) = BuildService();
		var buffer = new PipeBuffer(1000);
		var executor = new DBRef(42, null);
		var red = MarkupText.Wrap(AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, false)), MarkupText.Plain("red"));

		using (new PipeOutputCapture().BeginCapture(42, buffer))
		{
			await service.Notify(executor, "first", sender: null);
			await service.Notify(executor, red, sender: null);
			await service.NotifyLocalized(executor, "AnyKey", sender: null);
		}

		await Assert.That(buffer.Text.ToPlainText()).IsEqualTo("first\nred\nlocalized line");
		await Assert.That(buffer.Text.Render(MarkupFormat.Ansi)).IsEqualTo(MarkupText.Concat([MarkupText.Plain("first\n"), red,
			MarkupText.Plain("\nlocalized line")]).Render(MarkupFormat.Ansi));
		connections.DidNotReceive().Get(executor);
	}

	[Test]
	public async Task OutputToSomeoneElse_OrAfterThePipe_IsDelivered()
	{
		var (service, connections) = BuildService();
		var buffer = new PipeBuffer(1000);

		using (new PipeOutputCapture().BeginCapture(42, buffer))
		{
			await service.Notify(new DBRef(43, null), "to someone else", sender: null);
		}
		await service.Notify(new DBRef(42, null), "after the pipe", sender: null);

		await Assert.That(buffer.Text.Length).IsEqualTo(0);
		connections.Received(1).Get(new DBRef(43, null));
		connections.Received(1).Get(new DBRef(42, null));
	}

	[Test]
	public async Task ABufferPastItsLimit_KeepsWholeLines()
	{
		var buffer = new PipeBuffer(10);

		buffer.Append(MarkupText.Plain("12345"));
		buffer.Append(MarkupText.Plain("1234"));
		buffer.Append(MarkupText.Plain("1"));

		await Assert.That(buffer.Text.ToPlainText()).IsEqualTo("12345\n1234");
		await Assert.That(buffer.Truncated).IsTrue();
	}
}
