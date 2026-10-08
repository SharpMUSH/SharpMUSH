using SharpMUSH.SocketServer.Models;

namespace SharpMUSH.SocketServer.Services;

/// <param name="Handle">The connection, so the renderer can tell which pictures its terminal already holds.</param>
/// <param name="SessionId">The connection's session, which tells a reused handle from the one before it.</param>
/// <param name="Website">
/// The game's web address (<c>mud_url</c>): an MXP or Pueblo client is sent the game's own pictures at it, since it
/// cannot fetch an address relative to the portal. Null while it is unset.
/// </param>
public sealed record RenderContext(
	string ConnectionType,
	ProtocolCapabilities Capabilities,
	PlayerOutputPreferences? Preferences,
	long Handle = 0,
	string? SessionId = null,
	string? Website = null);

/// <param name="Prompt">Whether the markup is a prompt, which is not given a line ending.</param>
public sealed record RenderRequest(string? Markup, byte[]? Data, RenderContext Context, bool Prompt = false);
