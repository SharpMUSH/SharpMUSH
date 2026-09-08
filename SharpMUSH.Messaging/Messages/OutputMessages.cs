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
public record MarkupOutputMessage(long Handle, string Markup) : IHandleMessage;

/// <summary>
/// Like <see cref="MarkupOutputMessage"/> but for prompt output (no trailing newline).
/// </summary>
public record MarkupPromptMessage(long Handle, string Markup) : IHandleMessage;

/// <summary>
/// Message sent from MainProcess to ConnectionServer to send GMCP data to a connection
/// </summary>
public record GMCPOutputMessage(long Handle, string Module, string Message) : IHandleMessage;

/// <summary>
/// Message sent from MainProcess to ConnectionServer to send MSDP variable updates to a connection
/// </summary>
public record MSDPOutputMessage(long Handle, Dictionary<string, string> Variables) : IHandleMessage;

/// <summary>
/// Message sent from MainProcess to ConnectionServer to send MSSP configuration to a connection
/// </summary>
public record MSSPOutputMessage(long Handle, Dictionary<string, string> Configuration) : IHandleMessage;

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
