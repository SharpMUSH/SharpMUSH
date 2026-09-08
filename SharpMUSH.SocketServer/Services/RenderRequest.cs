using SharpMUSH.ConnectionServer.Models;

namespace SharpMUSH.ConnectionServer.Services;

public sealed record RenderContext(
	string ConnectionType,
	ProtocolCapabilities Capabilities,
	PlayerOutputPreferences? Preferences);

public sealed record RenderRequest(string? Markup, byte[]? Data, RenderContext Context);
