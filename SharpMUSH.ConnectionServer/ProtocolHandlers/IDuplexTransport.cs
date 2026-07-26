namespace SharpMUSH.ConnectionServer.ProtocolHandlers;

/// <summary>
/// Transport-agnostic duplex byte pipe for a single terminal-play connection. Implemented today by
/// the WebSocket adapter; the abstraction keeps <see cref="ConnectionPump"/> unaware of the
/// underlying protocol so another transport could be slotted in without touching it.
/// </summary>
public interface IDuplexTransport
{
	/// <summary>Transport identifier used as the connection type (currently "websocket").</summary>
	string Kind { get; }

	string RemoteIp { get; }

	string Hostname { get; }

	/// <summary>Sends one UTF-8 frame to the client.</summary>
	Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct);

	/// <summary>Returns one complete decoded UTF-8 frame, or null when the peer closed.</summary>
	Task<string?> ReceiveTextAsync(CancellationToken ct);

	Task CloseAsync();
}
