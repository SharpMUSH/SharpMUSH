using System.Collections.Concurrent;
using System.Text;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Quartz;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Server.Consumers;
using SharpMUSH.Tests.Server;

namespace SharpMUSH.Tests.Services;

public class ConnectionIncarnationTests
{
	private const long Handle = 1000001;

	private static ValueTask Register(ConnectionService service, string session, long time) =>
		service.Register(Handle, "127.0.0.1", "localhost", "websocket",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8,
			new ConcurrentDictionary<string, string>(new Dictionary<string, string>
			{
				["SessionId"] = session,
				["ConnectionStartTime"] = time.ToString(),
				["ConnectionType"] = "websocket"
			}));

	[Test]
	public async Task NewSocketCannotInheritLiveEngineBindingAfterOwnerRestart()
	{
		var service = new ConnectionService(Substitute.For<IPublisher>());
		await Register(service, "old", 100);
		await service.Bind(Handle, new DBRef(7, 1000));

		await Register(service, "replacement", 200);

		await Assert.That(service.Get(Handle)!.Ref).IsNull();
		await Assert.That(service.Get(Handle)!.State).IsEqualTo(IConnectionService.ConnectionState.Connected);
		await Assert.That(service.Get(Handle)!.Metadata["SessionId"]).IsEqualTo("replacement");
	}

	[Test]
	public async Task RedeliveredCurrentAndOlderRegistrationsPreserveCurrentLogin()
	{
		var service = new ConnectionService(Substitute.For<IPublisher>());
		await Register(service, "current", 200);
		await service.Bind(Handle, new DBRef(7, 1000));

		await Register(service, "current", 200);
		await Register(service, "old", 100);

		await Assert.That(service.Get(Handle)!.Ref).IsEqualTo(new DBRef(7, 1000));
		await Assert.That(service.Get(Handle)!.Metadata["SessionId"]).IsEqualTo("current");
	}

	[Test]
	public async Task ReconciledSameIncarnationKeepsAuthenticatedBinding()
	{
		var store = Substitute.For<IConnectionStateStore>();
		store.GetAllConnectionsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IEnumerable<(long, ConnectionStateData)>>(
		[
			(Handle, new ConnectionStateData
			{
				Handle = Handle, PlayerObjid = "#7:1000", State = "LoggedIn", IpAddress = "127.0.0.1",
				Hostname = "localhost", ConnectionType = "websocket", ConnectedAt = DateTimeOffset.UnixEpoch,
				Metadata = new Dictionary<string, string> { ["SessionId"] = "current", ["ConnectionStartTime"] = "100" }
			})
		]));
		var service = new ConnectionService(Substitute.For<IPublisher>(), store);
		await service.ReconcileFromStateStoreAsync(_ => _ => ValueTask.CompletedTask,
			_ => _ => ValueTask.CompletedTask, () => Encoding.UTF8);

		await Register(service, "current", 100);

		await Assert.That(service.Get(Handle)!.Ref).IsEqualTo(new DBRef(7, 1000));
		await store.DidNotReceive().SetConnectionAsync(Arg.Any<long>(), Arg.Any<ConnectionStateData>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task StaleInputAndCloseCannotAffectReplacement()
	{
		var service = new ConnectionService(Substitute.For<IPublisher>());
		await Register(service, "current", 200);
		var scheduler = Substitute.For<ITaskScheduler>();
		await new TelnetInputConsumer(NullLogger<TelnetInputConsumer>.Instance, scheduler, service)
			.HandleAsync(new TelnetInputMessage(Handle, "look", "old"));
		await new WebSocketInputConsumer(NullLogger<WebSocketInputConsumer>.Instance, scheduler, service)
			.HandleAsync(new WebSocketInputMessage(Handle, "look", "old"));
		await new ConnectionClosedConsumer(NullLogger<ConnectionClosedConsumer>.Instance, service)
			.HandleAsync(new ConnectionClosedMessage(Handle, DateTimeOffset.UtcNow, "old"));

		await Assert.That(scheduler.ReceivedCalls().Any()).IsFalse();
		await Assert.That(service.Get(Handle)).IsNotNull();
	}

	[Test]
	public async Task CurrentInputUsesThePublishedSchedulerEntryPoint()
	{
		var service = new ConnectionService(Substitute.For<IPublisher>());
		await Register(service, "current", 200);
		var scheduler = Substitute.For<ITaskScheduler>();
		await new TelnetInputConsumer(NullLogger<TelnetInputConsumer>.Instance, scheduler, service)
			.HandleAsync(new TelnetInputMessage(Handle, "look", "current"));
		await new WebSocketInputConsumer(NullLogger<WebSocketInputConsumer>.Instance, scheduler, service)
			.HandleAsync(new WebSocketInputMessage(Handle, "look", "current"));

		await scheduler.Received(2).WriteUserCommand(Handle, Arg.Any<MarkupText>(),
			Arg.Is<ParserState>(state => state.ConnectionSessionId == "current"));
		await scheduler.DidNotReceive().AdmitUserCommand(Arg.Any<long>(), Arg.Any<MarkupText>(), Arg.Any<ParserState>());
	}

	[Test]
	public async Task DelayedEstablishedEventCannotReplaceAuthoritativeKvIncarnation()
	{
		var service = Substitute.For<IConnectionService>();
		var store = Substitute.For<IConnectionStateStore>();
		store.GetConnectionAsync(Handle, Arg.Any<CancellationToken>()).Returns(Task.FromResult<ConnectionStateData?>(new()
		{
			Handle = Handle, State = "Connected", IpAddress = "127.0.0.1", Hostname = "localhost",
			ConnectionType = "websocket", ConnectedAt = DateTimeOffset.UtcNow,
			Metadata = new Dictionary<string, string> { ["SessionId"] = "current" }
		}));
		var consumer = new ConnectionEstablishedConsumer(NullLogger<ConnectionEstablishedConsumer>.Instance,
			service, Substitute.For<IMessageBus>(), store);

		await consumer.HandleAsync(new ConnectionEstablishedMessage(Handle, "127.0.0.1", "localhost", "websocket",
			DateTimeOffset.UtcNow.AddHours(1), SessionId: "old"));

		await Assert.That(service.ReceivedCalls().Any()).IsFalse();
	}

	[Test]
	public async Task CloseNotificationCannotRemoveAConcurrentReplacement()
	{
		var store = Substitute.For<IConnectionStateStore>();
		var publisher = Substitute.For<IPublisher>();
		var service = new ConnectionService(publisher, store);
		await Register(service, "old", 100);
		service.ListenState(change =>
		{
			if (change.Item4 == IConnectionService.ConnectionState.Disconnected)
				Register(service, "replacement", 200).GetAwaiter().GetResult();
		});

		await service.Disconnect(Handle, "old");

		await Assert.That(service.Get(Handle)!.Metadata["SessionId"]).IsEqualTo("replacement");
		var closed = publisher.ReceivedCalls().SelectMany(call => call.GetArguments())
			.OfType<SharpMUSH.Library.Notifications.ConnectionStateChangeNotification>()
			.Single(message => message.NewState == IConnectionService.ConnectionState.Disconnected);
		await Assert.That(closed.FormerConnection!.Metadata["SessionId"]).IsEqualTo("old");
		await store.DidNotReceive().RemoveConnectionAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task QueuedInputIsFencedAgainWhenItExecutes()
	{
		var service = new ConnectionService(Substitute.For<IPublisher>());
		await Register(service, "old", 100);
		var parser = Substitute.For<IMUSHCodeParser>();
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(TestSharpMushOptions.Create());
		await using var scheduler = new SharpMUSH.Library.Services.TaskScheduler(parser, service,
			Substitute.For<ISchedulerFactory>(), Substitute.For<IAttributeService>(), Substitute.For<IMediator>(),
			NullLogger<SharpMUSH.Library.Services.TaskScheduler>.Instance, options,
			Substitute.For<INotifyService>());
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		await scheduler.AdmitWork(async () => { entered.SetResult(); await release.Task; return null; }, "block", "test");
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			await scheduler.WriteUserCommand(Handle, MarkupString.MarkupText.Plain("look"),
				ParserState.Empty with { Handle = Handle, ConnectionSessionId = "old" });
			await Register(service, "replacement", 200);
			await scheduler.AdmitWork(() => { drained.SetResult(); return ValueTask.FromResult<CallState?>(null); }, "drain", "test");
		}
		finally { release.TrySetResult(); }
		await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(parser.ReceivedCalls().Any()).IsFalse();
	}

	[Test]
	public async Task LogoutAndPlayerSwitchRevokeResumeBeforeBindingChanges()
	{
		var store = Substitute.For<IConnectionStateStore>();
		var service = new ConnectionService(Substitute.For<IPublisher>(), store);
		await Register(service, "current", 100);
		await service.Bind(Handle, new DBRef(7, 1000));
		await service.Bind(Handle, new DBRef(7, 1000));
		await Assert.That(service.Get(Handle)!.Metadata.ContainsKey("ResumeRevoked")).IsFalse();

		var revocation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		store.TryRevokeResumeAsync(Handle, "current", Arg.Any<CancellationToken>()).Returns(revocation.Task);
		var switching = service.Bind(Handle, new DBRef(8, 1000)).AsTask();
		await Assert.That(service.Get(Handle)!.Ref).IsEqualTo(new DBRef(7, 1000));
		revocation.SetResult(true);
		await switching;
		await Assert.That(service.Get(Handle)!.Metadata["ResumeRevoked"]).IsEqualTo("1");
		await service.Unbind(Handle);
		await Assert.That(service.Get(Handle)!.Ref).IsNull();
	}

	[Test]
	public async Task FailedResumeRevocationPreventsLogout()
	{
		var store = Substitute.For<IConnectionStateStore>();
		var service = new ConnectionService(Substitute.For<IPublisher>(), store);
		await Register(service, "current", 100);
		await service.Bind(Handle, new DBRef(7, 1000));
		store.TryRevokeResumeAsync(Handle, "current", Arg.Any<CancellationToken>())
			.Returns(Task.FromException<bool>(new IOException("NATS unavailable")));

		await Assert.That(async () => await service.Unbind(Handle)).Throws<IOException>();

		await Assert.That(service.Get(Handle)!.Ref).IsEqualTo(new DBRef(7, 1000));
		await Assert.That(service.Get(Handle)!.Metadata.ContainsKey("ResumeRevoked")).IsFalse();
	}

	[Test]
	[Arguments("bind")]
	[Arguments("unbind")]
	[Arguments("account")]
	public async Task StaleAuthenticationChangeCannotRevokeOrModifyReplacement(string operation)
	{
		var store = Substitute.For<IConnectionStateStore>();
		var service = new ConnectionService(Substitute.For<IPublisher>(), store);
		await Register(service, "old", 100);
		await service.Bind(Handle, new DBRef(7, 1000));
		var revoked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		store.TryRevokeResumeAsync(Handle, "old", Arg.Any<CancellationToken>()).Returns(revoked.Task);
		var pending = operation switch
		{
			"bind" => service.Bind(Handle, new DBRef(8, 1000)).AsTask(),
			"unbind" => service.Unbind(Handle).AsTask(),
			_ => service.BindAccount(Handle, "other").AsTask()
		};
		await Register(service, "replacement", 200);
		store.ClearReceivedCalls();
		revoked.SetResult(false);
		await pending;

		await Assert.That(service.Get(Handle)!.Metadata["SessionId"]).IsEqualTo("replacement");
		await Assert.That(service.Get(Handle)!.Metadata.ContainsKey("ResumeRevoked")).IsFalse();
		await Assert.That(service.Get(Handle)!.Ref).IsNull();
		await Assert.That(store.ReceivedCalls().Any()).IsFalse();
	}

	[Test]
	public async Task AccountSwitchRevokesResumeButInitialCharacterSelectionDoesNot()
	{
		var service = new ConnectionService(Substitute.For<IPublisher>());
		await Register(service, "current", 100);
		await service.BindAccount(Handle, "account-one");
		await service.Bind(Handle, new DBRef(7, 1000));
		await Assert.That(service.Get(Handle)!.Metadata.ContainsKey("ResumeRevoked")).IsFalse();

		await service.BindAccount(Handle, "account-two");

		await Assert.That(service.Get(Handle)!.Ref).IsNull();
		await Assert.That(service.Get(Handle)!.Metadata["ResumeRevoked"]).IsEqualTo("1");
		await Assert.That(service.Get(Handle)!.Metadata["AccountId"]).IsEqualTo("account-two");
	}
}
