using Microsoft.AspNetCore.SignalR.Client;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Tests.Shared;

/// <summary>
/// An in-memory <see cref="IGameHubConnection"/>: start and stop flip <see cref="State"/> and are
/// counted, every invocation is recorded, and handler registrations are accepted and ignored.
/// </summary>
/// <param name="onInvoke">When set, every <see cref="InvokeAsync"/> faults with it after being recorded.</param>
public sealed class FakeGameHubConnection(Exception? onInvoke = null) : IGameHubConnection
{
	public int StartCount { get; private set; }
	public int StopCount { get; private set; }
	public List<string> Invoked { get; } = [];
	public HubConnectionState State { get; private set; } = HubConnectionState.Disconnected;

	public Task StartAsync(CancellationToken cancellationToken = default)
	{
		StartCount++;
		State = HubConnectionState.Connected;
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken = default)
	{
		StopCount++;
		State = HubConnectionState.Disconnected;
		return Task.CompletedTask;
	}

	public Task InvokeAsync(string methodName, string arg, CancellationToken cancellationToken = default)
	{
		Invoked.Add($"{methodName}:{arg}");
		return onInvoke is null ? Task.CompletedTask : Task.FromException(onInvoke);
	}

	public IDisposable On(string methodName, Action<GameOutputMessage> handler) => NoopDisposable.Instance;
	public IDisposable On(string methodName, Action<RoomEventMessage> handler) => NoopDisposable.Instance;
	public IDisposable On(string methodName, Action<SceneEventMessage> handler) => NoopDisposable.Instance;
	public IDisposable On(string methodName, Action handler) => NoopDisposable.Instance;
	public event Func<Exception?, Task>? Closed { add { } remove { } }
	public event Func<Exception?, Task>? Reconnecting { add { } remove { } }
	public event Func<string?, Task>? Reconnected { add { } remove { } }
	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
