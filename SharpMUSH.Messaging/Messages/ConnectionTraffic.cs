namespace SharpMUSH.Messaging.Messages;

/// <summary>
/// The bytes one connection has moved since it opened, as the connection server counts them: PennMUSH's
/// <c>input_chars</c>, <c>output_chars</c> and <c>output_size</c>, which <c>SESSION</c> shows.
/// </summary>
/// <param name="Handle">The connection's descriptor.</param>
/// <param name="Received">Bytes the client has sent.</param>
/// <param name="Sent">Bytes written to the client.</param>
/// <param name="Pending">Bytes handed to the socket that it has not yet taken.</param>
public sealed record ConnectionTraffic(long Handle, long Received, long Sent, long Pending);
