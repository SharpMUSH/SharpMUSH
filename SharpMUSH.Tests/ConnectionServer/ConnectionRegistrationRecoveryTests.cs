using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Tests.ConnectionServer;

public class ConnectionRegistrationRecoveryTests
{
	[Test]
	public async Task TransientPersistenceFailureRecoversBeforePublishingEstablished()
	{
		var store = Substitute.For<IConnectionStateStore>();
		var bus = Substitute.For<IMessageBus>();
		var writes = new List<ConnectionStateData>();
		var persisted = false;
		var publishedAfterPersistence = false;
		var disconnected = false;
		store.SetConnectionAsync(42, Arg.Any<ConnectionStateData>(), Arg.Any<CancellationToken>()).Returns(call =>
		{
			writes.Add(call.ArgAt<ConnectionStateData>(1));
			if (writes.Count == 1) return Task.FromException(new IOException("temporary KV failure"));
			persisted = true;
			return Task.CompletedTask;
		});
		bus.Publish(Arg.Any<ConnectionEstablishedMessage>(), Arg.Any<CancellationToken>()).Returns(_ =>
		{
			publishedAfterPersistence = persisted;
			return Task.CompletedTask;
		});
		var service = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus, store);

		await Register(service, () => disconnected = true);

		await Assert.That(writes.Count).IsEqualTo(2);
		await Assert.That(writes[0].ConnectedAt).IsEqualTo(writes[1].ConnectedAt);
		await Assert.That(writes[0].Metadata["SessionId"]).IsEqualTo(writes[1].Metadata["SessionId"]);
		await Assert.That(publishedAfterPersistence).IsTrue();
		await Assert.That(service.Get(42)).IsNotNull();
		await Assert.That(disconnected).IsFalse();
		await bus.Received(1).Publish(Arg.Any<ConnectionEstablishedMessage>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task ExhaustedPersistenceRetriesCloseIncompleteConnectionWithoutPublishing()
	{
		var store = Substitute.For<IConnectionStateStore>();
		var bus = Substitute.For<IMessageBus>();
		var disconnected = false;
		store.SetConnectionAsync(42, Arg.Any<ConnectionStateData>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromException(new IOException("KV unavailable")));
		var service = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus, store);

		await Assert.That(async () => await Register(service, () => disconnected = true)).Throws<IOException>();

		await Assert.That(disconnected).IsTrue();
		await Assert.That(service.Get(42)).IsNull();
		await bus.DidNotReceive().Publish(Arg.Any<ConnectionEstablishedMessage>(), Arg.Any<CancellationToken>());
		await store.Received(1).RemoveConnectionAsync(42, Arg.Any<CancellationToken>());
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task RegistrationObservesCallerAndHostCancellation(bool cancelHost)
	{
		var store = Substitute.For<IConnectionStateStore>();
		var bus = Substitute.For<IMessageBus>();
		var lifetime = Substitute.For<IHostApplicationLifetime>();
		using var cancellation = new CancellationTokenSource();
		lifetime.ApplicationStopping.Returns(cancelHost ? cancellation.Token : CancellationToken.None);
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		store.SetConnectionAsync(42, Arg.Any<ConnectionStateData>(), Arg.Any<CancellationToken>()).Returns(call =>
		{
			entered.SetResult();
			return Task.Delay(Timeout.Infinite, call.ArgAt<CancellationToken>(2));
		});
		var disconnected = false;
		var service = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus, store, lifetime);
		var registration = Register(service, () => disconnected = true, cancelHost ? default : cancellation.Token);
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		cancellation.Cancel();

		await Assert.That(async () => await registration.WaitAsync(TimeSpan.FromSeconds(5))).Throws<OperationCanceledException>();

		await Assert.That(disconnected).IsTrue();
		await Assert.That(service.Get(42)).IsNull();
		await bus.DidNotReceive().Publish(Arg.Any<ConnectionEstablishedMessage>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task PublicationRetryNeverOverwritesAnAlreadyBoundPlayer()
	{
		var store = Substitute.For<IConnectionStateStore>();
		var bus = Substitute.For<IMessageBus>();
		ConnectionStateData? persisted = null;
		var publications = new List<ConnectionEstablishedMessage>();
		store.SetConnectionAsync(42, Arg.Any<ConnectionStateData>(), Arg.Any<CancellationToken>()).Returns(call =>
		{
			persisted = call.ArgAt<ConnectionStateData>(1);
			return Task.CompletedTask;
		});
		bus.Publish(Arg.Any<ConnectionEstablishedMessage>(), Arg.Any<CancellationToken>()).Returns(call =>
		{
			publications.Add(call.ArgAt<ConnectionEstablishedMessage>(0));
			if (publications.Count != 1) return Task.CompletedTask;
			// The engine accepted the event and logged the player in; only its acknowledgement was lost.
			persisted!.PlayerObjid = "#7:1000";
			persisted.State = "LoggedIn";
			return Task.FromException(new IOException("lost publication acknowledgement"));
		});
		var service = new ConnectionServerService(NullLogger<ConnectionServerService>.Instance, bus, store);

		await Register(service, () => { });

		await store.Received(1).SetConnectionAsync(42, Arg.Any<ConnectionStateData>(), Arg.Any<CancellationToken>());
		await Assert.That(publications.Count).IsEqualTo(2);
		await Assert.That(publications[0]).IsEqualTo(publications[1]);
		await Assert.That(persisted!.PlayerObjid).IsEqualTo("#7:1000");
		await Assert.That(persisted.State).IsEqualTo("LoggedIn");
	}

	private static Task Register(ConnectionServerService service, Action disconnect, CancellationToken ct = default) =>
		service.RegisterAsync(42, "127.0.0.1", "localhost", "telnet", _ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask, () => Encoding.UTF8, disconnect, cancellationToken: ct);
}
