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
		factory.Create(Arg.Any<string>()).Returns(hub);

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
		await svc.ConnectAsync("token");
		factory.Received(1).Create("token");
		await hub.Received(1).StartAsync(Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task ConnectAsync_OnSuccess_IsConnectedTrue()
	{
		var (svc, _, _) = MakeService();
		await svc.ConnectAsync("token");
		await Assert.That(svc.IsConnected).IsTrue();
		await Assert.That(svc.ConnectionState).IsEqualTo(HubConnectionState.Connected);
	}

	[Test]
	public async Task ConnectAsync_FiresOnConnectionStateChanged()
	{
		var (svc, _, _) = MakeService();
		var events = new List<HubConnectionState>();
		svc.OnConnectionStateChanged += () => events.Add(svc.ConnectionState);

		await svc.ConnectAsync("token");

		await Assert.That(events.Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(events[^1]).IsEqualTo(HubConnectionState.Connected);
	}

	[Test]
	public async Task ConnectAsync_WhenAlreadyConnected_DoesNotCallFactoryAgain()
	{
		var (svc, factory, _) = MakeService();
		await svc.ConnectAsync("token");
		await svc.ConnectAsync("token2");
		factory.Received(1).Create(Arg.Any<string>());
	}

	[Test]
	public async Task ConnectAsync_RegistersOutputAndRoomEventHandlers()
	{
		var (svc, _, hub) = MakeService();
		await svc.ConnectAsync("token");
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
		factory.Create(Arg.Any<string>()).Returns(hub);

		var svc = new ConnectionStateService(factory, NullLogger<ConnectionStateService>.Instance);
		await svc.ConnectAsync("token");

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
		await svc.ConnectAsync("token");
		await svc.DisconnectAsync();
		await hub.Received(1).StopAsync(Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task DisconnectAsync_SetsDisconnectedState()
	{
		var (svc, _, _) = MakeService();
		await svc.ConnectAsync("token");
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
		await svc.ConnectAsync("token");
		await svc.SendCommandAsync("look");
		await hub.Received(1).InvokeAsync("SendCommand", "look", Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task DisposeAsync_WhenConnected_DisposesHub()
	{
		var (svc, _, hub) = MakeService();
		await svc.ConnectAsync("token");
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
}
