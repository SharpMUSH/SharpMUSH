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
	[Arguments(IPermissionService.InteractType.Hear)]
	[Arguments(IPermissionService.InteractType.Hear | IPermissionService.InteractType.Page)]
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
	public async Task VisualInteractionHasADistinctNonzeroBit()
	{
		var visual = Enum.Parse<IPermissionService.InteractType>("See");
		await Assert.That((int)visual).IsNotEqualTo(0);
		await Assert.That(IPermissionService.InteractType.Hear.HasFlag(IPermissionService.InteractType.See)).IsFalse();
	}

	[Test]
	[Arguments(typeof(PermissionService))]
	[Arguments(typeof(NotifyService))]
	[Arguments(typeof(MoveService))]
	[Arguments(typeof(ListenerRoutingService))]
	public async Task PerceptionPolicyIsARequiredDependency(Type service)
	{
		var parameter = service.GetConstructors().Single().GetParameters().Single(p => p.ParameterType == typeof(IRealityPolicy));
		await Assert.That(parameter.HasDefaultValue).IsFalse();
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

}
