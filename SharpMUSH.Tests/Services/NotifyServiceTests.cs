using MarkupString;
using MarkupString.MarkupImplementation;
using Mediator;
using NSubstitute;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;
using System.Collections.Concurrent;
using System.Text;

namespace SharpMUSH.Tests.Services;

public class NotifyServiceTests
{
	[Test]
	public async Task StringNotifications_PublishSerializedMarkup()
	{
		// NotifyService no longer renders to ANSI/Pueblo/MXP — it keeps output as an MString and
		// publishes serialized markup; the ConnectionServer renders it per the connection's wire
		// format (see MarkupOutputRendererTests). Here we verify the markup is published and that it
		// round-trips to the original text.
		var publisher = Substitute.For<IPublisher>();
		var messageBus = Substitute.For<IMessageBus>();
		var connections = new ConnectionService(publisher);
		var localization = new LocalizationService();
		var notify = new NotifyService(messageBus, connections, localization);

		await connections.Register(
			1,
			"127.0.0.1",
			"localhost",
			"telnet",
			_ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask,
			() => Encoding.UTF8,
			new ConcurrentDictionary<string, string>(new Dictionary<string, string>
			{
				["ConnectionStartTime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
				["LastConnectionSignal"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
				["InternetProtocolAddress"] = "127.0.0.1",
				["HostName"] = "localhost",
				["ConnectionType"] = "telnet"
			}));

		const string raw = "<send href=\"look\">Tom & \"Sue\"</send>";
		await notify.Notify(1, raw, sender: null);

		await messageBus.Received(1).HandlePublish(
			Arg.Is<MarkupOutputMessage>(msg =>
				msg.Handle == 1 &&
				MModule.deserialize(msg.Markup).ToPlainText() == raw));
	}

	[Test]
	public async Task NotifyLocalizedMarkup_PreservesMarkupWhenFormattingLocalizedText()
	{
		var publisher = Substitute.For<IPublisher>();
		var messageBus = Substitute.For<IMessageBus>();
		var connections = new ConnectionService(publisher);
		var localization = new LocalizationService();
		var notify = new NotifyService(messageBus, connections, localization);

		await connections.Register(
			7,
			"127.0.0.1",
			"localhost",
			"telnet",
			_ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask,
			() => Encoding.UTF8,
			new ConcurrentDictionary<string, string>(new Dictionary<string, string>
			{
				["ConnectionStartTime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
				["LastConnectionSignal"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
				["InternetProtocolAddress"] = "127.0.0.1",
				["HostName"] = "localhost",
				["ConnectionType"] = "telnet",
				["OUTPUT_FORMAT"] = "pueblo"
			}));

		var exitMarkup = MModule.MarkupSingle2(
			HtmlMarkup.Create("send", "href=\"North\""),
			MModule.single("North"));

		await notify.NotifyLocalizedMarkup(
			7,
			nameof(ErrorMessages.Notifications.ExitNameToDestFormat),
			sender: null,
			exitMarkup,
			MModule.single("Room Zero"));

		await messageBus.Received(1).HandlePublish(
			Arg.Is<MarkupOutputMessage>(msg =>
				msg.Handle == 7 &&
				MModule.deserialize(msg.Markup).Render("pueblo").Contains("<send href=\"North\">North</send> to Room Zero")));
	}
}
