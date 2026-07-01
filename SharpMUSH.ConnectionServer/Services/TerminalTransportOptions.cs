namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// Terminal transport behavior toggles. <see cref="SequencedOutput"/> turns on per-handle output
/// sequence envelopes + resume/replay; it is enabled together with the WebTransport feature so the
/// default (WebSocket-only) path is unchanged.
/// </summary>
public sealed record TerminalTransportOptions(bool SequencedOutput);
