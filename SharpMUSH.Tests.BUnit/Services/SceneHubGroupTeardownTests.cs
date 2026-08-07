using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>A scene connection that reports itself Connected and throws whatever a test hands it.</summary>
file sealed class ThrowingSceneConnection(Exception? onInvoke) : IGameHubConnection
{
	public List<string> Invoked { get; } = [];
	public HubConnectionState State { get; private set; } = HubConnectionState.Disconnected;

	public Task StartAsync(CancellationToken cancellationToken = default)
	{
		State = HubConnectionState.Connected;
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken = default)
	{
		State = HubConnectionState.Disconnected;
		return Task.CompletedTask;
	}

	public Task InvokeAsync(string methodName, string arg, CancellationToken cancellationToken = default)
	{
		Invoked.Add($"{methodName}:{arg}");
		return onInvoke is null ? Task.CompletedTask : Task.FromException(onInvoke);
	}

	public IDisposable On(string methodName, Action<GameOutputMessage> handler) => new Noop();
	public IDisposable On(string methodName, Action<RoomEventMessage> handler) => new Noop();
	public IDisposable On(string methodName, Action<SceneEventMessage> handler) => new Noop();
	public IDisposable On(string methodName, Action handler) => new Noop();
	public event Func<Exception?, Task>? Closed;
	public event Func<Exception?, Task>? Reconnecting;
	public event Func<string?, Task>? Reconnected;
	public ValueTask DisposeAsync() => ValueTask.CompletedTask;

	private sealed class Noop : IDisposable { public void Dispose() { } }
}

file sealed class SceneHubFactory(Exception? onInvoke) : IGameHubConnectionFactory
{
	public ThrowingSceneConnection Game { get; } = new(null);
	public ThrowingSceneConnection Scene { get; } = new(onInvoke);

	public IGameHubConnection Create() => Game;
	public IGameHubConnection? CreateScene() => Scene;
}

/// <summary>
/// Joining and leaving a scene group must survive the connection going away underneath the call.
/// <para>Both methods check <c>State == Connected</c> and then invoke, and the connection can close in
/// between — SignalR answers a closed or disposed connection with a transport fault, not the no-op the
/// check was reaching for. <c>SceneLive</c> leaves its group from <c>DisposeAsync</c>, where a throw has
/// nowhere to surface, and that is precisely when the connection is being torn down.</para>
/// <para>A <c>HubException</c> is the opposite case and must still reach the caller: it is the hub's
/// authorization answer, and the page renders it.</para>
/// </summary>
public class SceneHubGroupTeardownTests
{
	/// <summary>What SignalR answers a connection that closed, disposed, or cancelled underneath the call.</summary>
	public static IEnumerable<Exception> TransportFaults() =>
	[
		new InvalidOperationException("The 'InvokeCoreAsync' method cannot be called if the connection is not active."),
		new ObjectDisposedException(nameof(HubConnection)),
		new TaskCanceledException("The underlying connection was closed."),
		new OperationCanceledException("Connection closed."),
	];

	private static async Task<ISceneHubControl> ConnectedServiceAsync(IGameHubConnectionFactory factory)
	{
		var service = new ConnectionStateService(factory, NullLogger<ConnectionStateService>.Instance);
		await service.ConnectAsync();
		return service;
	}

	[Test]
	[MethodDataSource(nameof(TransportFaults))]
	public async Task LeaveSceneAsync_WhenTheConnectionDiesMidCall_DoesNotThrow(Exception fault)
	{
		var factory = new SceneHubFactory(fault);
		var service = await ConnectedServiceAsync(factory);

		await service.LeaveSceneAsync("scene-1");

		await Assert.That(factory.Scene.Invoked).Contains("LeaveScene:scene-1");
	}

	[Test]
	[MethodDataSource(nameof(TransportFaults))]
	public async Task JoinSceneAsync_WhenTheConnectionDiesMidCall_DoesNotThrow(Exception fault)
	{
		var factory = new SceneHubFactory(fault);
		var service = await ConnectedServiceAsync(factory);

		await service.JoinSceneAsync("scene-1");

		await Assert.That(factory.Scene.Invoked).Contains("JoinScene:scene-1");
	}

	[Test]
	public async Task JoinSceneAsync_StillSurfacesTheHubsRefusal()
	{
		var factory = new SceneHubFactory(new HubException("That scene is not available."));
		var service = await ConnectedServiceAsync(factory);

		await Assert.That(async () => await service.JoinSceneAsync("scene-1"))
			.Throws<HubException>()
			.Because("a refusal is the hub's answer about the caller, not a transport fault — SceneLive renders it");
	}

	[Test]
	public async Task LeaveSceneAsync_StillSurfacesTheHubsRefusal()
	{
		var factory = new SceneHubFactory(new HubException("Joining a scene requires a character."));
		var service = await ConnectedServiceAsync(factory);

		await Assert.That(async () => await service.LeaveSceneAsync("scene-1"))
			.Throws<HubException>();
	}
}
