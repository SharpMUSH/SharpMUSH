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
	public async Task StringNotifications_AreHtmlEncodedForPuebloAndMxp()
	{
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
				["ConnectionType"] = "telnet",
				["OUTPUT_FORMAT"] = "pueblo"
			}));

		await connections.Register(
			2,
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
				["OUTPUT_FORMAT"] = "mxp"
			}));

		await notify.Notify(1, "<send href=\"look\">Tom & \"Sue\"</send>", sender: null);
		await notify.Notify(2, "<send href=\"look\">Tom & \"Sue\"</send>", sender: null);

		await messageBus.Received(1).HandlePublish(
			Arg.Is<TelnetOutputMessage>(msg =>
				msg.Handle == 1 &&
				Encoding.UTF8.GetString(msg.Data).Contains("&lt;send href=&quot;look&quot;&gt;Tom &amp; &quot;Sue&quot;&lt;/send&gt;")));

		await messageBus.Received(1).HandlePublish(
			Arg.Is<TelnetOutputMessage>(msg =>
				msg.Handle == 2 &&
				Encoding.UTF8.GetString(msg.Data).Contains($"{ErrorMessages.Notifications.MxpLineOpen}&lt;send href=&quot;look&quot;&gt;Tom &amp; &quot;Sue&quot;&lt;/send&gt;")));
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
			Arg.Is<TelnetOutputMessage>(msg =>
				msg.Handle == 7 &&
				Encoding.UTF8.GetString(msg.Data).Contains("<send href=\"North\">North</send> to Room Zero")));
	}
}
