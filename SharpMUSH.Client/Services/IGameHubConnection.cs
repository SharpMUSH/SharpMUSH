using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Thin abstraction over a SignalR <c>HubConnection</c> so that
/// <see cref="ConnectionStateService"/> can be unit-tested without a live hub.
/// </summary>
public interface IGameHubConnection : IAsyncDisposable
{
	/// <summary>The underlying connection lifecycle state.</summary>
	Microsoft.AspNetCore.SignalR.Client.HubConnectionState State { get; }

	/// <summary>Starts the connection.</summary>
	Task StartAsync(CancellationToken cancellationToken = default);

	/// <summary>Stops the connection.</summary>
	Task StopAsync(CancellationToken cancellationToken = default);

	/// <summary>Invokes a hub server method with one argument.</summary>
	Task InvokeAsync(string methodName, string arg, CancellationToken cancellationToken = default);

	/// <summary>
	/// Registers a handler for a strongly-typed hub client method that carries a
	/// <see cref="GameOutputMessage"/> payload.
	/// </summary>
	IDisposable On(string methodName, Action<GameOutputMessage> handler);

	/// <summary>
	/// Registers a handler for a strongly-typed hub client method that carries a
	/// <see cref="RoomEventMessage"/> payload.
	/// </summary>
	IDisposable On(string methodName, Action<RoomEventMessage> handler);

	/// <summary>
	/// Registers a handler for a strongly-typed hub client method that carries a
	/// <see cref="SceneEventMessage"/> payload.
	/// </summary>
	IDisposable On(string methodName, Action<SceneEventMessage> handler);

	/// <summary>
	/// Raised when the connection drops unexpectedly.
	/// The exception may be null for a graceful close.
	/// </summary>
	event Func<Exception?, Task>? Closed;

	/// <summary>
	/// Raised when the connection is in the process of reconnecting after a drop.
	/// </summary>
	event Func<Exception?, Task>? Reconnecting;

	/// <summary>
	/// Raised when the connection has successfully reconnected.
	/// </summary>
	event Func<string?, Task>? Reconnected;
}
