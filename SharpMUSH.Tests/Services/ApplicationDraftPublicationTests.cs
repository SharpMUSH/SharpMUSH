using MarkupString;
using System.Collections.Concurrent;
using System.Text;
using Mediator;
using NSubstitute;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Tests.Services;

public class ApplicationDraftPublicationTests
{
	private static async Task<ConnectionService> Connections()
	{
		var connections = new ConnectionService(Substitute.For<IPublisher>());
		await connections.Register(1, "127.0.0.1", "localhost", "test", _ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask, () => Encoding.UTF8,
			new ConcurrentDictionary<string, string>(new Dictionary<string, string> { ["SessionId"] = "original" }));
		return connections;
	}

	[Test]
	public async Task DraftReplyRetiresOnLogoutEvenWhenTheSameCharacterRebinds()
	{
		var connections = await Connections();
		var actor = new DBRef(2, 2);
		await connections.Bind(1, actor);
		var service = new ApplicationDraftService(connections: connections);
		var original = connections.Get(1)!;
		var instance = service.TerminalInstance(original);
		await Assert.That(service.CanReply(original, instance)).IsTrue();
		await connections.Unbind(1);
		await connections.Bind(1, actor);
		await Assert.That(service.CanReply(original, instance)).IsFalse();
	}

	[Test]
	public async Task NormalOutputCarriesTheCapturedIncarnationAndSkipsRetiredOrMissingTransports()
	{
		var connections = await Connections();
		var bus = Substitute.For<IMessageBus>();
		var notify = new NotifyService(bus, connections, new LocalizationService());
		await notify.NotifyToSession(1, "original", "private");
		connections.Update(1, "SessionId", "replacement");
		await notify.NotifyToSession(1, "original", "stale private");
		await notify.NotifyToSession(9, "", "missing private");
		await bus.Received(1).HandlePublish(Arg.Is<MarkupOutputMessage>(message => message.Handle == 1
			&& message.SessionId == "original" && MarkupTextSerializer.Deserialize(message.Markup).ToPlainText() == "private"), Arg.Any<CancellationToken>());
		await bus.DidNotReceive().HandlePublish(Arg.Is<MarkupOutputMessage>(message => message.Handle == 9), Arg.Any<CancellationToken>());
	}
}
