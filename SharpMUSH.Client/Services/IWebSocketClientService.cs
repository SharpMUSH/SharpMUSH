using System.Net.WebSockets;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Interface for managing WebSocket connections to SharpMUSH ConnectionServer
/// </summary>
public interface IWebSocketClientService : IAsyncDisposable
{
	/// <summary>
	/// Event raised when a message is received from the server
	/// </summary>
	event EventHandler<string>? MessageReceived;

	/// <summary>
	/// Event raised when the connection state changes
	/// </summary>
	event EventHandler<WebSocketState>? ConnectionStateChanged;

	/// <summary>
	/// Gets whether the WebSocket is currently connected
	/// </summary>
	bool IsConnected { get; }

	/// <summary>
	/// Connect to the WebSocket server
	/// </summary>
	/// <param name="serverUri">WebSocket server URI (e.g., ws://localhost:4202/ws)</param>
	Task ConnectAsync(string serverUri);

	/// <summary>
	/// Send a message to the server
	/// </summary>
	/// <param name="message">Message to send</param>
	Task SendAsync(string message);

	/// <summary>
	/// Disconnect from the server
	/// </summary>
	Task DisconnectAsync();
}
