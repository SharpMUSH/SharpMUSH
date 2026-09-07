using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.Tests.ConnectionServer;

public class LifecycleNoticeTests
{
	[Test]
	public async Task NoticesReachEverySocketWithoutDisconnectingAndIgnoreOldEvents()
	{
		var bus = Substitute.For<IMessageBus>();
		var service = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus);
		var output = new List<string>();
		var disconnected = false;
		await service.RegisterAsync(1, "localhost", "localhost", "telnet",
			_ => throw new IOException("broken socket"), _ => ValueTask.CompletedTask,
			() => Encoding.UTF8, () => disconnected = true);
		await service.RegisterAsync(2, "localhost", "localhost", "websocket",
			data => { output.Add(Encoding.UTF8.GetString(data)); return ValueTask.CompletedTask; },
			_ => ValueTask.CompletedTask, () => Encoding.UTF8, () => disconnected = true);
		var notices = new EngineLifecycleNoticeService(service, NullLogger<EngineLifecycleNoticeService>.Instance);
		var now = DateTimeOffset.UtcNow;
		await notices.NotifyAsync(now, false).WaitAsync(TimeSpan.FromSeconds(5));
		await notices.NotifyAsync(now, false).WaitAsync(TimeSpan.FromSeconds(5));
		await notices.NotifyAsync(now.AddSeconds(1), true).WaitAsync(TimeSpan.FromSeconds(5));
		await notices.NotifyAsync(now, false).WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(output.Count).IsEqualTo(2);
		await Assert.That(output[0]).Contains("restarting");
		await Assert.That(output[1]).Contains("ready");
		await Assert.That(disconnected).IsFalse();
	}

	[Test]
	public async Task DisconnectClosesSocketEvenWhenBusFails()
	{
		var bus = Substitute.For<IMessageBus>();
		var service = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus);
		var disconnected = false;
		await service.RegisterAsync(1, "localhost", "localhost", "telnet",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask,
			() => Encoding.UTF8, () => disconnected = true);
		bus.Publish(Arg.Any<SharpMUSH.Messaging.Messages.ConnectionClosedMessage>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromException(new IOException("bus unavailable")));
		await service.DisconnectAsync(1);
		await Assert.That(disconnected).IsTrue();
		await Assert.That(service.Get(1)).IsNull();
	}
	[Test]
	public async Task InputFailureKeepsSocketAndDoesNotRetryAnUncertainCommand()
	{
		var bus = Substitute.For<IMessageBus>();
		var service = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus);
		var output = new List<string>();
		await service.RegisterAsync(1, "localhost", "localhost", "telnet",
			data => { output.Add(Encoding.UTF8.GetString(data)); return ValueTask.CompletedTask; },
			_ => ValueTask.CompletedTask, () => Encoding.UTF8, () => throw new Exception("Must not disconnect"));
		var message = new SharpMUSH.Messaging.Messages.TelnetInputMessage(1, "look");
		bus.Publish(message, Arg.Any<CancellationToken>()).Returns(Task.FromException(new IOException()));
		await ConnectionInputPublisher.PublishAsync(bus, service, NullLogger.Instance, 1, message, default);
		await bus.Received(1).Publish(message, Arg.Any<CancellationToken>());
		await Assert.That(output.Single()).Contains("could not be confirmed");
		await Assert.That(service.Get(1)).IsNotNull();
	}

	[Test]
	public async Task RefreshContinuesAfterStoreFailureWithoutReplacingBindings()
	{
		var bus = Substitute.For<IMessageBus>();
		var service = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus);
		foreach (var handle in new long[] { 1, 2 })
			await service.RegisterAsync(handle, "localhost", "localhost", "telnet",
				_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8,
				() => throw new Exception("Must not disconnect"));
		var store = Substitute.For<SharpMUSH.Library.Services.Interfaces.IConnectionStateStore>();
		store.UpdateMetadataAsync(1, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromException(new IOException()));
		await new ConnectionStateRefreshService(service, store,
			NullLogger<ConnectionStateRefreshService>.Instance).RefreshAsync(default);
		await store.Received(1).UpdateMetadataAsync(2, "GatewayLastSeen", Arg.Any<string>(), Arg.Any<CancellationToken>());
		await store.DidNotReceive().SetConnectionAsync(Arg.Any<long>(),
			Arg.Any<SharpMUSH.Library.Services.Interfaces.ConnectionStateData>(), Arg.Any<CancellationToken>());
		await Assert.That(service.GetAll().Count()).IsEqualTo(2);
	}

	[Test]
	public async Task TimedOutNoticeDoesNotOverlapTheNextNotice()
	{
		var bus = Substitute.For<IMessageBus>();
		var service = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus);
		var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var writes = 0;
		await service.RegisterAsync(1, "localhost", "localhost", "telnet",
			_ => { writes++; return new ValueTask(pending.Task); }, _ => ValueTask.CompletedTask,
			() => Encoding.UTF8, () => { });
		var notices = new EngineLifecycleNoticeService(service, NullLogger<EngineLifecycleNoticeService>.Instance);
		var now = DateTimeOffset.UtcNow;
		try
		{
			await notices.NotifyAsync(now, false).WaitAsync(TimeSpan.FromSeconds(5));
			await notices.NotifyAsync(now.AddSeconds(1), true).WaitAsync(TimeSpan.FromSeconds(5));
			await Assert.That(writes).IsEqualTo(1);
		}
		finally { pending.TrySetResult(); }
	}

}
