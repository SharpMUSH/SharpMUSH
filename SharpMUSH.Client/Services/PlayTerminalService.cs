namespace SharpMUSH.Client.Services;

// Two independent, persistent (singleton) MUSH connections exist side by side:
//
//   • ITerminalService      — the "command" terminal (docked drawer). Used by the softcode editor,
//                             MushQueryService, object browser, etc. for tooling/admin commands.
//   • IPlayTerminalService  — the "play" terminal (the /play page). A separate connection aimed at
//                             the player's in-character interactions.
//
// They are distinct WebSocket connections so command/tooling traffic never interleaves with the
// player's roleplay session. Both are singletons, so each connection survives page navigation.

/// <summary>Marker interface for the dedicated play-session WebSocket client.</summary>
public interface IPlayWebSocketClientService : IWebSocketClientService;

/// <summary>A second WebSocket client instance, independent of the command terminal's.</summary>
public sealed class PlayWebSocketClientService(ILogger<WebSocketClientService> logger)
	: WebSocketClientService(logger), IPlayWebSocketClientService;

/// <summary>Marker interface for the play-session terminal (the /play page connection).</summary>
public interface IPlayTerminalService : ITerminalService;

/// <summary>
/// The play-session terminal. Identical behaviour to <see cref="TerminalService"/> but backed by its
/// own <see cref="IPlayWebSocketClientService"/>, giving the player a connection separate from the
/// command/softcode terminal.
/// </summary>
public sealed class PlayTerminalService(IPlayWebSocketClientService wsService, ILogger<TerminalService> logger)
	: TerminalService(wsService, logger), IPlayTerminalService;
