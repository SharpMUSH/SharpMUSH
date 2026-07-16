using SharpMUSH.Library.Models.Portal;

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

	/// <summary>
	/// Raised when the server signals that its loaded-plugin set changed (a plugin DLL was unloaded or
	/// reloaded). The portal reacts by forcing a hard browser refresh, which fully tears down and rebuilds
	/// the WASM runtime — the only way to reclaim a compiled component assembly that was loaded in-browser.
	/// </summary>
	event Action? OnPluginsChanged;

	/// <summary>
	/// Opens the hub connection, authenticated with the caller's current account-session token
	/// (passed as the <c>access_token</c> query parameter so the hub can authenticate the Blazor
	/// WASM client without cookies). The token is resolved live on every connect and automatic-reconnect
	/// attempt, not captured once up front, so a session established/renewed after this call was first
	/// made is still picked up. No-ops if already connected.
	/// </summary>
	Task ConnectAsync();

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
