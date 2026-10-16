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
	[Arguments(false)]
	[Arguments(true)]
	public async Task QueuedPrivateOutputDoesNotSurviveLogoutAndRebind(bool sameActor)
	{
		var connections = await Connections();
		await connections.Bind(1, new DBRef(2, 2));
		var original = connections.Get(1)!;
		var bus = Substitute.For<IMessageBus>();
		var notify = new NotifyService(bus, connections, new LocalizationService());
		var earlier = notify.Lane.Reserve(1, "original");
		var pending = notify.NotifyToBinding(original, "original", "private draft").AsTask();
		await Assert.That(pending.IsCompleted).IsFalse();
		await connections.Unbind(1);
		await connections.Bind(1, sameActor ? new DBRef(2, 2) : new DBRef(3, 3));
		earlier.Dispose();
		await pending;
		await bus.DidNotReceive().HandlePublish(Arg.Any<MarkupOutputMessage>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task BindingRetirementWaitsForAdmittedPrivatePublication()
	{
		var connections = await Connections();
		await connections.Bind(1, new DBRef(2, 2));
		var original = connections.Get(1)!;
		var started = new TaskCompletionSource();
		var release = new TaskCompletionSource();
		var bus = Substitute.For<IMessageBus>();
		bus.HandlePublish(Arg.Any<MarkupOutputMessage>(), Arg.Any<CancellationToken>()).Returns(async _ =>
		{
			started.TrySetResult();
			await release.Task;
		});
		var notify = new NotifyService(bus, connections, new LocalizationService());
		var pending = notify.NotifyToBinding(original, "original", "private draft").AsTask();
		await started.Task;
		var retiring = connections.Unbind(1).AsTask();
		await Assert.That(retiring.IsCompleted).IsFalse();
		release.SetResult();
		await pending;
		await retiring;
		await connections.Bind(1, new DBRef(2, 2));
		await notify.NotifyToBinding(original, "original", "old binding");
		await bus.Received(1).HandlePublish(Arg.Any<MarkupOutputMessage>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task DisconnectAfterQueuedBindClosesOnlyTheOriginalTransport(bool replaceTransport)
	{
		var connections = await Connections();
		await connections.Bind(1, new DBRef(2, 2));
		var original = connections.Get(1)!;
		var started = new TaskCompletionSource();
		var release = new TaskCompletionSource();
		var bus = Substitute.For<IMessageBus>();
		bus.HandlePublish(Arg.Any<MarkupOutputMessage>(), Arg.Any<CancellationToken>()).Returns(async _ =>
		{
			started.TrySetResult();
			await release.Task;
		});
		var notify = new NotifyService(bus, connections, new LocalizationService());
		var publishing = notify.NotifyToBinding(original, "original", "private draft").AsTask();
		await started.Task;
		var binding = connections.Bind(1, new DBRef(3, 3)).AsTask();
		var closing = connections.Disconnect(1, "original").AsTask();
		try
		{
			await Assert.That(binding.IsCompleted).IsFalse();
			await Assert.That(closing.IsCompleted).IsFalse();
			if (replaceTransport)
				await connections.Register(1, "127.0.0.1", "localhost", "test", _ => ValueTask.CompletedTask,
					_ => ValueTask.CompletedTask, () => Encoding.UTF8,
					new ConcurrentDictionary<string, string>(new Dictionary<string, string> { ["SessionId"] = "replacement", ["ConnectionStartTime"] = "200" }));
		}
		finally { release.TrySetResult(); }
		await Task.WhenAll(publishing, binding, closing);
		if (replaceTransport)
			await Assert.That(connections.Get(1)!.Metadata["SessionId"]).IsEqualTo("replacement");
		else await Assert.That(connections.Get(1) is null).IsTrue().Because("a close must remove the same transport even when its character binding changed while waiting");
		await Assert.That(await connections.Get(new DBRef(3, 3)).CountAsync()).IsEqualTo(0);
		await notify.NotifyToBinding(original, "original", "retired private draft");
		await bus.Received(1).HandlePublish(Arg.Any<MarkupOutputMessage>(), Arg.Any<CancellationToken>());
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
