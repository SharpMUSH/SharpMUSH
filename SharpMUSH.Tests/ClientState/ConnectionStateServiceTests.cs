using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.ClientState;

public class ConnectionStateServiceTests
{
	private static (ConnectionStateService svc, IGameHubConnectionFactory factory, IGameHubConnection hub)
		MakeService()
	{
		var hub = Substitute.For<IGameHubConnection>();
		var disposable = Substitute.For<IDisposable>();
		hub.On(Arg.Any<string>(), Arg.Any<Action<GameOutputMessage>>()).Returns(disposable);
		hub.On(Arg.Any<string>(), Arg.Any<Action<RoomEventMessage>>()).Returns(disposable);
		hub.On(Arg.Any<string>(), Arg.Any<Action<SceneEventMessage>>()).Returns(disposable);

		var factory = Substitute.For<IGameHubConnectionFactory>();
		factory.Create().Returns(hub);

		var svc = new ConnectionStateService(factory, NullLogger<ConnectionStateService>.Instance);
		return (svc, factory, hub);
	}

	[Test]
	public async Task InitialState_IsDisconnected_NotConnected()
	{
		var (svc, _, _) = MakeService();
		await Assert.That(svc.IsConnected).IsFalse();
		await Assert.That(svc.ConnectionState).IsEqualTo(HubConnectionState.Disconnected);
	}

	[Test]
	public async Task ConnectAsync_CallsFactoryCreate_AndStartAsync()
	{
		var (svc, factory, hub) = MakeService();
		await svc.ConnectAsync();
		factory.Received(1).Create();
		await hub.Received(1).StartAsync(Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task ConnectAsync_OnSuccess_IsConnectedTrue()
	{
		var (svc, _, _) = MakeService();
		await svc.ConnectAsync();
		await Assert.That(svc.IsConnected).IsTrue();
		await Assert.That(svc.ConnectionState).IsEqualTo(HubConnectionState.Connected);
	}

	[Test]
	public async Task ConnectAsync_FiresOnConnectionStateChanged()
	{
		var (svc, _, _) = MakeService();
		var events = new List<HubConnectionState>();
		svc.OnConnectionStateChanged += () => events.Add(svc.ConnectionState);

		await svc.ConnectAsync();

		await Assert.That(events.Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(events[^1]).IsEqualTo(HubConnectionState.Connected);
	}

	[Test]
	public async Task ConnectAsync_WhenAlreadyConnected_DoesNotCallFactoryAgain()
	{
		var (svc, factory, _) = MakeService();
		await svc.ConnectAsync();
		await svc.ConnectAsync();
		factory.Received(1).Create();
	}

	[Test]
	public async Task ConnectAsync_RegistersOutputAndRoomEventHandlers()
	{
		var (svc, _, hub) = MakeService();
		await svc.ConnectAsync();
		hub.Received(1).On("ReceiveOutput", Arg.Any<Action<GameOutputMessage>>());
		hub.Received(1).On("ReceiveRoomEvent", Arg.Any<Action<RoomEventMessage>>());
	}

	[Test]
	public async Task ConnectAsync_WhenStartAsyncThrows_SetsDisconnectedState()
	{
		var hub = Substitute.For<IGameHubConnection>();
		var disposable = Substitute.For<IDisposable>();
		hub.On(Arg.Any<string>(), Arg.Any<Action<GameOutputMessage>>()).Returns(disposable);
		hub.On(Arg.Any<string>(), Arg.Any<Action<RoomEventMessage>>()).Returns(disposable);
		hub.On(Arg.Any<string>(), Arg.Any<Action<SceneEventMessage>>()).Returns(disposable);
		hub.StartAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new Exception("network error"));

		var factory = Substitute.For<IGameHubConnectionFactory>();
		factory.Create().Returns(hub);

		var svc = new ConnectionStateService(factory, NullLogger<ConnectionStateService>.Instance);
		await svc.ConnectAsync();

		await Assert.That(svc.IsConnected).IsFalse();
		await Assert.That(svc.ConnectionState).IsEqualTo(HubConnectionState.Disconnected);
	}

	[Test]
	public async Task DisconnectAsync_WhenNotConnected_DoesNotThrow()
	{
		var (svc, _, _) = MakeService();
		await svc.DisconnectAsync();
		await Assert.That(svc.IsConnected).IsFalse();
	}

	[Test]
	public async Task DisconnectAsync_AfterConnect_CallsStopAsync()
	{
		var (svc, _, hub) = MakeService();
		await svc.ConnectAsync();
		await svc.DisconnectAsync();
		await hub.Received(1).StopAsync(Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task DisconnectAsync_SetsDisconnectedState()
	{
		var (svc, _, _) = MakeService();
		await svc.ConnectAsync();
		await svc.DisconnectAsync();
		await Assert.That(svc.IsConnected).IsFalse();
		await Assert.That(svc.ConnectionState).IsEqualTo(HubConnectionState.Disconnected);
	}

	[Test]
	public async Task SendCommandAsync_WhenNotConnected_ThrowsInvalidOperationException()
	{
		var (svc, _, _) = MakeService();
		await Assert.ThrowsAsync<InvalidOperationException>(
			async () => await svc.SendCommandAsync("look"));
	}

	[Test]
	public async Task SendCommandAsync_WhenConnected_InvokesHubMethod()
	{
		var (svc, _, hub) = MakeService();
		await svc.ConnectAsync();
		await svc.SendCommandAsync("look");
		await hub.Received(1).InvokeAsync("SendCommand", "look", Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task DisposeAsync_WhenConnected_DisposesHub()
	{
		var (svc, _, hub) = MakeService();
		await svc.ConnectAsync();
		await svc.DisposeAsync();
		await hub.Received(1).DisposeAsync();
	}

	[Test]
	public async Task DisposeAsync_WhenNotConnected_DoesNotThrow()
	{
		var (svc, _, _) = MakeService();
		await svc.DisposeAsync();
		await Assert.That(svc.IsConnected).IsFalse();
	}

	private static IGameHubConnection StubHub(Func<Task> start)
	{
		var hub = Substitute.For<IGameHubConnection>();
		var disposable = Substitute.For<IDisposable>();
		hub.On(Arg.Any<string>(), Arg.Any<Action<GameOutputMessage>>()).Returns(disposable);
		hub.On(Arg.Any<string>(), Arg.Any<Action<RoomEventMessage>>()).Returns(disposable);
		hub.On(Arg.Any<string>(), Arg.Any<Action<SceneEventMessage>>()).Returns(disposable);
		hub.StartAsync(Arg.Any<CancellationToken>()).Returns(_ => start());
		return hub;
	}

	/// <summary>
	/// A game that is restarting refuses the first start. The service tries again on its own, so the portal
	/// has its hub back once the game does, whichever page is open.
	/// </summary>
	[Test]
	public async Task AStartThatFails_IsTriedAgain_UntilItConnects()
	{
		// The game stays down until the test says it is back, so no retry can connect early.
		var gameUp = false;
		var factory = Substitute.For<IGameHubConnectionFactory>();
		factory.Create().Returns(_ => gameUp
			? StubHub(() => Task.CompletedTask)
			: StubHub(() => Task.FromException(new HttpRequestException("connection refused"))));
		var svc = new ConnectionStateService(factory, NullLogger<ConnectionStateService>.Instance)
		{
			RetryDelays = [TimeSpan.FromMilliseconds(10)]
		};

		await svc.ConnectAsync();
		await Task.Delay(50);
		await Assert.That(svc.IsConnected).IsFalse();

		gameUp = true;
		for (var i = 0; i < 200 && !svc.IsConnected; i++) await Task.Delay(10);
		await Assert.That(svc.IsConnected).IsTrue();
	}

	/// <summary>Disconnecting on purpose (sign-out, a character switch) ends the retrying.</summary>
	[Test]
	public async Task DisconnectAsync_StopsTheRetrying()
	{
		var down = StubHub(() => Task.FromException(new HttpRequestException("connection refused")));
		var factory = Substitute.For<IGameHubConnectionFactory>();
		factory.Create().Returns(down);
		var svc = new ConnectionStateService(factory, NullLogger<ConnectionStateService>.Instance)
		{
			RetryDelays = [TimeSpan.FromMilliseconds(20)]
		};

		await svc.ConnectAsync();
		await svc.DisconnectAsync();
		var created = factory.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IGameHubConnectionFactory.Create));
		await Task.Delay(150);

		await Assert.That(factory.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IGameHubConnectionFactory.Create)))
			.IsEqualTo(created).Because("no hub is started after a disconnect");
	}

	/// <summary>
	/// While SignalR is reconnecting on its own, asking for the scene feed must not stop that connection:
	/// a stopped reconnect is over for good.
	/// </summary>
	[Test]
	public async Task EnsureSceneLive_LeavesAReconnectingHubAlone()
	{
		var hub = StubHub(() => Task.CompletedTask);
		var factory = Substitute.For<IGameHubConnectionFactory>();
		factory.Create().Returns(hub);
		var svc = new ConnectionStateService(factory, NullLogger<ConnectionStateService>.Instance);
		await svc.ConnectAsync();

		hub.State.Returns(Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Reconnecting);
		hub.Reconnecting += Raise.Event<Func<Exception?, Task>>(new Exception("lost"));
		await svc.EnsureSceneLiveAsync();

		await hub.DidNotReceive().StopAsync(Arg.Any<CancellationToken>());
		factory.Received(1).Create();
	}
}
