using Microsoft.AspNetCore.SignalR.Client;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Tests.Shared;

/// <summary>
/// An in-memory <see cref="IGameHubConnection"/>: start and stop flip <see cref="State"/> and are
/// counted, every invocation is recorded, and handler registrations are accepted and ignored, except
/// payload-free ones, which <see cref="Raise"/> calls, and <see cref="Reconnected"/>, which
/// <see cref="RaiseReconnectedAsync"/> calls.
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
	public IDisposable On(string methodName, Action<CharacterMovedMessage> handler) => NoopDisposable.Instance;
	public IDisposable On(string methodName, Action handler)
	{
		_handlers.Add((methodName, handler));
		return NoopDisposable.Instance;
	}

	/// <summary>Calls every payload-free handler registered for <paramref name="methodName"/>, as the server would.</summary>
	public void Raise(string methodName)
	{
		foreach (var (name, handler) in _handlers.ToArray())
			if (name == methodName) handler();
	}

	/// <summary>Raises <see cref="Reconnected"/>, as a connection that came back would.</summary>
	public async Task RaiseReconnectedAsync()
	{
		if (_reconnected is { } reconnected) await reconnected(null);
	}

	private readonly List<(string Name, Action Handler)> _handlers = [];
	private Func<string?, Task>? _reconnected;

	public event Func<Exception?, Task>? Closed { add { } remove { } }
	public event Func<Exception?, Task>? Reconnecting { add { } remove { } }
	public event Func<string?, Task>? Reconnected { add => _reconnected += value; remove => _reconnected -= value; }
	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
