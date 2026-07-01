namespace SharpMUSH.Client.Services;

/// <summary>
/// Transport-agnostic client channel to the ConnectionServer terminal endpoint. Implemented by the
/// WebSocket and WebTransport clients so the rest of the app is unaware of which transport is active.
/// Kept to the members both transports share (the WebSocket client already satisfies these), so the
/// existing WebSocket-specific events remain available to current consumers without conflict.
/// </summary>
public interface ITransportClient : IAsyncDisposable
{
	/// <summary>"websocket" | "webtransport".</summary>
	string Kind { get; }

	bool IsConnected { get; }

	/// <summary>Raised for each decoded text frame received from the server.</summary>
	event EventHandler<string>? MessageReceived;

	Task ConnectAsync(string uri);

	Task SendAsync(string message);

	Task DisconnectAsync();

	/// <summary>Discards any queued outbound messages (e.g. before an OTT login).</summary>
	void ClearSendBuffer();
}
