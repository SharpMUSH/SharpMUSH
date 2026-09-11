using Mediator;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;
using System.Collections.Concurrent;
using System.Text;

namespace SharpMUSH.Tests.Services;

public class RealityRoutingTests
{
	[Test]
	[Arguments(0, false)]
	[Arguments(3, false)]
	[Arguments(4, true)]
	public async Task PublishedInteractionConstantsKeepTheirPerceptionDirection(int publishedValue, bool hearing)
	{
		var factory = new TestObjectFactory();
		var actor = factory.CreatePlayer(61, "actor");
		var target = factory.CreatePlayer(62, "target");
		actor.Expect<SharpPlayer>().Id = "actor";
		target.Expect<SharpPlayer>().Id = "target";
		var reality = Substitute.For<IRealityPolicy>();
		reality.CanPerceiveAsync(actor.Object().DBRef, target.Object().DBRef).Returns(true);
		reality.CanPerceiveAsync(target.Object().DBRef, actor.Object().DBRef).Returns(false);
		var locks = Substitute.For<ILockService>();
		locks.Evaluate(LockType.Interact, target, actor).Returns(true);
		var permissions = new PermissionService(locks, Substitute.For<IOptionsMonitor<SharpMUSHOptions>>(), reality);
		await Assert.That(await permissions.CanInteract(actor, target, (IPermissionService.InteractType)publishedValue)).IsEqualTo(!hearing);
	}

	[Test]
	[Arguments("See", 0)]
	[Arguments("Hear", 1)]
	[Arguments("Match", 2)]
	[Arguments("Presence", 3)]
	[Arguments("Page", 4)]
	public async Task InteractionEnumRetainsItsPublishedBinaryValues(string name, int publishedValue)
	{
		await Assert.That((int)Enum.Parse<IPermissionService.InteractType>(name)).IsEqualTo(publishedValue);
	}

	[Test]
	[Arguments("notify")]
	[Arguments("prompt")]
	[Arguments("localized")]
	[Arguments("system")]
	[Arguments("markup")]
	[Arguments("except")]
	public async Task BareRecipientDoesNotFollowAStampedReplacementWithTheSameNumber(string route)
	{
		var sender = new TestObjectFactory().CreatePlayer(10, "sender");
		var receiver = new DBRef(11);
		var reality = Substitute.For<IRealityPolicy>();
		reality.CanPerceiveAsync(Arg.Any<DBRef>(), Arg.Any<DBRef>(), Arg.Any<CancellationToken>()).Returns(true);
		var connections = Substitute.For<IConnectionService>();
		var old = new IConnectionService.ConnectionData(5, receiver, IConnectionService.ConnectionState.LoggedIn,
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8, new ConcurrentDictionary<string, string>());
		connections.Get(receiver).Returns(new[] { old }.ToAsyncEnumerable());
		connections.Get(5).Returns(old with { Ref = new DBRef(receiver.Number, 2) });
		var bus = Substitute.For<IMessageBus>();
		var notify = new NotifyService(bus, connections, new LocalizationService(), reality);
		switch (route)
		{
			case "notify": await notify.Notify(receiver, "private", sender); break;
			case "prompt": await notify.Prompt(receiver, "private", sender); break;
			case "localized": await notify.NotifyLocalized(receiver, "private", sender); break;
			case "system": await notify.NotifyLocalized(receiver, "private"); break;
			case "markup": await notify.NotifyLocalizedMarkup(receiver, "private", sender); break;
			default: await notify.NotifyExcept(receiver, "private", [], sender); break;
		}
		await Assert.That(bus.ReceivedCalls().Any()).IsFalse();
	}

	[Test]
	[Arguments(false, true)]
	[Arguments(true, false)]
	[Arguments(true, true)]
	public async Task EffectiveHearingSourceDoesNotReplaceTheInteractionLockActor(bool visible, bool lockAllowed)
	{
		var factory = new TestObjectFactory();
		var executor = factory.CreatePlayer(40, "executor");
		var source = factory.CreatePlayer(41, "source");
		var receiver = factory.CreatePlayer(42, "receiver");
		executor.Expect<SharpPlayer>().Id = "executor";
		source.Expect<SharpPlayer>().Id = "source";
		receiver.Expect<SharpPlayer>().Id = "receiver";
		var reality = Substitute.For<IRealityPolicy>();
		reality.CanPerceiveAsync(receiver.Object().DBRef, source.Object().DBRef).Returns(visible);
		var locks = Substitute.For<ILockService>();
		locks.Evaluate(LockType.Interact, receiver, executor).Returns(lockAllowed);
		locks.Evaluate(LockType.Interact, receiver, source).Returns(!lockAllowed);
		var permissions = new PermissionService(locks, Substitute.For<IOptionsMonitor<SharpMUSHOptions>>(), reality);
		await Assert.That(await permissions.CanInteract(executor, receiver, IPermissionService.InteractType.Hear, source))
			.IsEqualTo(visible && lockAllowed);
		await locks.DidNotReceive().Evaluate(LockType.Interact, receiver, source);
	}

	[Test]
	[Arguments(IPermissionService.InteractType.Hear)]
	[Arguments(IPermissionService.InteractType.Page)]
	public async Task RoomAndPrivilegedShortcutsCannotBypassDirectionalPolicy(IPermissionService.InteractType interaction)
	{
		var factory = new TestObjectFactory();
		var sender = factory.CreatePlayer(1, "God");
		AnySharpObject receiver = factory.CreateRoom(2, "room");
		var reality = Substitute.For<IRealityPolicy>();
		var permissions = new PermissionService(Substitute.For<ILockService>(), Substitute.For<IOptionsMonitor<SharpMUSHOptions>>(), reality);
		await Assert.That(await permissions.CanSee(sender, receiver)).IsFalse();
		await Assert.That(await permissions.CanSee(sender, receiver.Object())).IsFalse();
		await Assert.That(await permissions.CanFind(sender, receiver)).IsFalse();
		await Assert.That(await permissions.CanInteract(sender, receiver, interaction)).IsFalse();
		await reality.Received(1).CanPerceiveAsync(receiver.Object().DBRef, sender.Object().DBRef);
	}

	[Test]
	[Arguments("notify")]
	[Arguments("handles")]
	[Arguments("prompt")]
	[Arguments("object")]
	[Arguments("localized")]
	[Arguments("markup")]
	public async Task HiddenSenderCannotReachTransportCaptureOrListeners(string route)
	{
		var sender = new TestObjectFactory().CreatePlayer(10, "sender");
		var receiver = new DBRef(11, 0);
		var reality = Substitute.For<IRealityPolicy>();
		reality.IsEnabledAsync().Returns(true);
		var connections = Substitute.For<IConnectionService>();
		connections.Get(5).Returns(new IConnectionService.ConnectionData(5, receiver, IConnectionService.ConnectionState.LoggedIn,
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8, new ConcurrentDictionary<string, string>()));
		var bus = Substitute.For<IMessageBus>();
		var capture = Substitute.For<IHttpOutputCapture>();
		var listeners = Substitute.For<IListenerRoutingService>();
		var notify = new NotifyService(bus, connections, new LocalizationService(), reality, listeners, Substitute.For<IMediator>(), capture);
		switch (route)
		{
			case "notify": await notify.Notify(5L, "hidden", sender); break;
			case "handles": await notify.Notify([5L], "hidden", sender); break;
			case "prompt": await notify.Prompt([5L], "hidden", sender); break;
			case "object": await notify.Notify(receiver, "hidden", sender); break;
			case "localized": await notify.NotifyLocalized(receiver, "hidden", sender); break;
			case "markup": await notify.NotifyLocalizedMarkup(receiver, "hidden", sender); break;
		}
		await bus.DidNotReceive().HandlePublish(Arg.Any<MarkupOutputMessage>());
		await bus.DidNotReceive().HandlePublish(Arg.Any<MarkupPromptMessage>());
		await Assert.That(capture.ReceivedCalls().Any()).IsFalse();
		await Assert.That(listeners.ReceivedCalls().Any()).IsFalse();
	}

	[Test]
	[Arguments(typeof(PermissionService))]
	[Arguments(typeof(NotifyService))]
	[Arguments(typeof(MoveService))]
	[Arguments(typeof(ListenerRoutingService))]
	public async Task RealityAwareConstructorRequiresExplicitPolicy(Type service)
	{
		var parameters = service.GetConstructors()
			.SelectMany(c => c.GetParameters())
			.Where(p => p.ParameterType == typeof(IRealityPolicy))
			.ToArray();
		await Assert.That(parameters).IsNotEmpty();
		await Assert.That(parameters.Any(p => p.HasDefaultValue)).IsFalse();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task MultipleConnectionsReusePerceptionWithoutFollowingReboundHandles(bool prompt)
	{
		var sender = new TestObjectFactory().CreatePlayer(10, "sender");
		var receiver = new DBRef(11, 1);
		var reality = Substitute.For<IRealityPolicy>();
		reality.CanPerceiveAsync(Arg.Any<DBRef>(), Arg.Any<DBRef>(), Arg.Any<CancellationToken>()).Returns(true);
		var connections = Substitute.For<IConnectionService>();
		IConnectionService.ConnectionData Connection(long handle, DBRef reference) => new(handle, reference,
			IConnectionService.ConnectionState.LoggedIn, _ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask,
			() => Encoding.UTF8, new ConcurrentDictionary<string, string>());
		async IAsyncEnumerable<IConnectionService.ConnectionData> Current()
		{
			for (var handle = 1; handle <= 3; handle++)
			{
				var captured = Connection(handle, receiver);
				connections.Get(handle).Returns(handle == 3 ? Connection(handle, new DBRef(12, 1)) : captured);
				yield return captured;
				await Task.Yield();
			}
		}
		connections.Get(receiver).Returns(Current());
		var bus = Substitute.For<IMessageBus>();
		var notify = new NotifyService(bus, connections, new LocalizationService(), reality: reality);
		if (prompt) await notify.Prompt(receiver, "private", sender);
		else await notify.Notify(receiver, "private", sender);
		await reality.Received(1).CanPerceiveAsync(receiver, sender.Object().DBRef, Arg.Any<CancellationToken>());
		await Assert.That(bus.ReceivedCalls().Count()).IsEqualTo(2);
	}

	[Test]
	[Arguments("notify", false)]
	[Arguments("notify", true)]
	[Arguments("handles", false)]
	[Arguments("handles", true)]
	[Arguments("prompt", false)]
	[Arguments("prompt", true)]
	[Arguments("prompts", false)]
	[Arguments("prompts", true)]
	public async Task RawHandleRoutesRejectACharacterReboundDuringPerception(string route, bool sessionOnly)
	{
		var sender = new TestObjectFactory().CreatePlayer(10, "sender");
		var receiver = new DBRef(11, 1);
		var check = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var reality = Substitute.For<IRealityPolicy>();
		reality.CanPerceiveAsync(receiver, sender.Object().DBRef, Arg.Any<CancellationToken>()).Returns(new ValueTask<bool>(check.Task));
		var connections = Substitute.For<IConnectionService>();
		var current = new IConnectionService.ConnectionData(5, receiver, IConnectionService.ConnectionState.LoggedIn,
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8, new ConcurrentDictionary<string, string>());
		connections.Get(5).Returns(_ => current);
		var bus = Substitute.For<IMessageBus>();
		var notify = new NotifyService(bus, connections, new LocalizationService(), reality);
		var pending = (route switch
		{
			"notify" => notify.Notify(5L, "hidden", sender),
			"handles" => notify.Notify([5L], "hidden", sender),
			"prompt" => notify.Prompt(5L, "hidden", sender),
			_ => notify.Prompt([5L], "hidden", sender)
		}).AsTask();
		await Assert.That(pending.IsCompleted).IsFalse();
		current = sessionOnly
			? current with { Metadata = new ConcurrentDictionary<string, string>(new[] { KeyValuePair.Create("SessionId", "replacement") }) }
			: current with { Ref = new DBRef(12, 1) };
		check.SetResult(true);
		await pending;
		await Assert.That(bus.ReceivedCalls().Any()).IsFalse();
	}

	[Test]
	[Arguments("localized")]
	[Arguments("system")]
	[Arguments("markup")]
	[Arguments("except")]
	public async Task ObjectTargetedVariantsDoNotFollowAReboundHandle(string route)
	{
		var sender = new TestObjectFactory().CreatePlayer(10, "sender");
		var receiver = new DBRef(11, 1);
		var reality = Substitute.For<IRealityPolicy>();
		reality.CanPerceiveAsync(Arg.Any<DBRef>(), Arg.Any<DBRef>(), Arg.Any<CancellationToken>()).Returns(true);
		var connections = Substitute.For<IConnectionService>();
		var old = new IConnectionService.ConnectionData(5, receiver, IConnectionService.ConnectionState.LoggedIn,
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8, new ConcurrentDictionary<string, string>());
		connections.Get(receiver).Returns(new[] { old }.ToAsyncEnumerable());
		connections.Get(5).Returns(old with { Ref = new DBRef(12, 1) });
		var bus = Substitute.For<IMessageBus>();
		var notify = new NotifyService(bus, connections, new LocalizationService(), reality);
		switch (route)
		{
			case "localized": await notify.NotifyLocalized(receiver, "private", sender); break;
			case "system": await notify.NotifyLocalized(receiver, "private"); break;
			case "markup": await notify.NotifyLocalizedMarkup(receiver, "private", sender); break;
			default: await notify.NotifyExcept(receiver, "private", [], sender); break;
		}
		await Assert.That(bus.ReceivedCalls().Any()).IsFalse();
	}

}
