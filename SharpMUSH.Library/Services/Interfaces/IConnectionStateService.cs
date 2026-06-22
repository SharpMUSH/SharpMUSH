using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Plugins.Scene.Contracts;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Manages the client-side SignalR connection to the game hub at /hubs/game.
/// Creates the connection on demand, handles auto-reconnect with exponential
/// back-off, and surfaces incoming hub messages as events.
/// </summary>
public interface IConnectionStateService
{
	/// <summary>True when the hub connection is in the Connected state.</summary>
	bool IsConnected { get; }

	/// <summary>Current lifecycle state of the hub connection.</summary>
	HubConnectionState ConnectionState { get; }

	/// <summary>Raised whenever <see cref="ConnectionState"/> changes.</summary>
	event Action? OnConnectionStateChanged;

	/// <summary>Raised when the hub pushes a <see cref="GameOutputMessage"/> to this client.</summary>
	event Action<GameOutputMessage>? OnOutputReceived;

	/// <summary>Raised when the hub pushes a <see cref="RoomEventMessage"/> to this client.</summary>
	event Action<RoomEventMessage>? OnRoomEventReceived;

	/// <summary>Raised when the hub pushes a <see cref="SceneEventMessage"/> to this client.</summary>
	event Action<SceneEventMessage>? OnSceneEventReceived;

	/// <summary>
	/// Opens the hub connection authenticated with <paramref name="accessToken"/>.
	/// The token is passed as the <c>access_token</c> query parameter so the hub
	/// can authenticate the Blazor WASM client without cookies.
	/// No-ops if already connected.
	/// </summary>
	Task ConnectAsync(string accessToken);

	/// <summary>
	/// Closes the hub connection gracefully.
	/// No-ops when already disconnected.
	/// </summary>
	Task DisconnectAsync();

	/// <summary>
	/// Sends a raw command string to the game engine via the hub.
	/// Throws <see cref="InvalidOperationException"/> when not connected.
	/// </summary>
	Task SendCommandAsync(string command);
}
