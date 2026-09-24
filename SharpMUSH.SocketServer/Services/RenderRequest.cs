using SharpMUSH.ConnectionServer.Models;

namespace SharpMUSH.ConnectionServer.Services;

public sealed record RenderContext(
	string ConnectionType,
	ProtocolCapabilities Capabilities,
	PlayerOutputPreferences? Preferences);

/// <param name="Prompt">Whether the markup is a prompt, which is not given a line ending.</param>
public sealed record RenderRequest(string? Markup, byte[]? Data, RenderContext Context, bool Prompt = false);
