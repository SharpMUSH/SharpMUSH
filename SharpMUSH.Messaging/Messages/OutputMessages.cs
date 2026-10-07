namespace SharpMUSH.Messaging.Messages;

/// <summary>
/// Message sent from MainProcess to ConnectionServer to output text to a specific connection
/// </summary>
public record TelnetOutputMessage(long Handle, byte[] Data) : IHandleMessage;

/// <summary>
/// Message sent from MainProcess to ConnectionServer to output a prompt to a specific connection
/// </summary>
public record TelnetPromptMessage(long Handle, byte[] Data) : IHandleMessage;

/// <summary>
/// Message sent from MainProcess to ConnectionServer to broadcast to all connections
/// </summary>
public record BroadcastMessage(byte[] Data);

/// <summary>
/// Message sent from MainProcess to ConnectionServer to disconnect a connection
/// </summary>
public record DisconnectConnectionMessage(long Handle, string? Reason) : IHandleMessage;

/// <summary>
/// Message sent from MainProcess to ConnectionServer to output text to a WebSocket connection
/// </summary>
public record WebSocketOutputMessage(long Handle, string Data) : IHandleMessage;

/// <summary>
/// Message sent from MainProcess to ConnectionServer to output a prompt to a WebSocket connection
/// </summary>
public record WebSocketPromptMessage(long Handle, string Data) : IHandleMessage;

/// <summary>
/// Message sent from MainProcess to ConnectionServer carrying serialized markup (an MString as JSON)
/// for a specific connection. The ConnectionServer owns the wire format: it renders the markup to
/// ANSI/Pueblo/MXP for terminal connections, or forwards it as a markup envelope for WebSocket
/// (portal) connections so the browser can render it natively. <see cref="Markup"/> is the output of
/// <c>MarkupTextSerializer.Serialize</c>.
/// </summary>
public record MarkupOutputMessage(long Handle, string Markup) : IHandleMessage
{
	/// <summary>When present, delivery requires this exact transport incarnation.</summary>
	public string? SessionId { get; init; }

	/// <summary>
	/// Written to the connection's prompt channel (no trailing newline). Carrying prompts on this
	/// subject keeps them in order with ordinary output; the engine sends them this way only to a
	/// socket owner that advertised <see cref="ConnectionEstablishedMessage.OrderedPrompts"/>.
	/// </summary>
	public bool Prompt { get; init; }
}

/// <summary>
/// Like <see cref="MarkupOutputMessage"/> but for prompt output (no trailing newline). Consumed
/// on its own subject, so it has no order relative to ordinary output; kept for socket owners that
/// do not advertise <see cref="ConnectionEstablishedMessage.OrderedPrompts"/>.
/// </summary>
public record MarkupPromptMessage(long Handle, string Markup) : IHandleMessage
{
	/// <summary>When present, delivery requires this exact transport incarnation.</summary>
	public string? SessionId { get; init; }
}

/// <summary>
/// Message sent from MainProcess to ConnectionServer to send GMCP data to a connection
/// </summary>
public record GMCPOutputMessage(long Handle, string Module, string Message) : IHandleMessage;

/// <summary>
/// Message sent from MainProcess to ConnectionServer to send MSDP variable updates to a connection
/// </summary>
public record MSDPOutputMessage(long Handle, Dictionary<string, string> Variables) : IHandleMessage;

/// <summary>One MSSP variable and every value it carries, the default last.</summary>
public record MSSPVariable(string Name, string[] Values);

/// <summary>
/// Message sent from MainProcess to ConnectionServer with the MSSP report, which the connection server
/// answers <c>IAC DO MSSP</c> with. Published when the report changes, and when a connection server asks
/// (<see cref="MSSPReportRequestMessage"/>), because the telnet option has to be answered at once and
/// the connection server cannot build the report itself.
/// </summary>
public record MSSPReportMessage(MSSPVariable[] Variables);

/// <summary>
/// Message sent from MainProcess to ConnectionServer to update player output preferences for a connection
/// </summary>
public record UpdatePlayerPreferencesMessage(
	long Handle,
	bool AnsiEnabled,
	bool ColorEnabled,
	bool Xterm256Enabled,
	bool TruecolorEnabled = false
) : IHandleMessage;

/// <summary>
/// Clears character-specific output preferences when a socket returns to an unauthenticated state.
/// Terminal negotiation remains attached to the connection and becomes authoritative again.
/// </summary>
public record ClearPlayerOutputPreferencesMessage(long Handle) : IHandleMessage;

/// <summary>
/// Carries a <c>SOCKSET colorstyle</c> pin to the socket owner, which is where output is actually
/// rendered. Player flags and terminal negotiation can only ever <i>add</i> depth, so this is the
/// one setting that renders below what they claim — including refusing colour outright.
/// <paramref name="Style"/> is one of the <see cref="SharpMUSH.Library.Utilities.ColorStyles"/>
/// values, or null for "auto", which hands the decision back to the flags and the terminal.
/// </summary>
public record UpdateColorStyleMessage(long Handle, string? Style) : IHandleMessage;

/// <summary>
/// Carries a player's <c>@theme</c> to the socket owner, whose renderer lays layouts out again under it.
/// <paramref name="Theme"/> is a theme as the <c>"theme"</c> layout option takes it, or null for none,
/// which leaves the game's own look.
/// </summary>
public record UpdateThemeMessage(long Handle, string? Theme) : IHandleMessage;
