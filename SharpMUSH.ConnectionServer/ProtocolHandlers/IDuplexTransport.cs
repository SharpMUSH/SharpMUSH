namespace SharpMUSH.ConnectionServer.ProtocolHandlers;

/// <summary>
/// Transport-agnostic duplex byte pipe for a single terminal-play connection.
/// Implemented by WebSocket and WebTransport adapters so <see cref="ConnectionPump"/>
/// is unaware of the underlying protocol. A migrated QUIC connection is transparent
/// here: <see cref="ReceiveTextAsync"/> simply keeps returning frames.
/// </summary>
public interface IDuplexTransport
{
	/// <summary>Transport identifier used as the connection type ("websocket" | "webtransport").</summary>
	string Kind { get; }

	string RemoteIp { get; }

	string Hostname { get; }

	/// <summary>Sends one UTF-8 frame to the client.</summary>
	Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct);

	/// <summary>Returns one complete decoded UTF-8 frame, or null when the peer closed.</summary>
	Task<string?> ReceiveTextAsync(CancellationToken ct);

	Task CloseAsync();
}
