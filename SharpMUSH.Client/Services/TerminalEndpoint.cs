namespace SharpMUSH.Client.Services;

/// <summary>
/// Derives the terminal WebSocket endpoint from the portal's own origin, so it survives a reverse
/// proxy. Loopback maps to the dev connection server on :4202.
/// </summary>
public static class TerminalEndpoint
{
	public static string Resolve(string portalBaseUri)
	{
		var baseUri = new Uri(portalBaseUri);
		if (baseUri.IsLoopback)
			return "ws://localhost:4202/ws";
		var scheme = baseUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
		return $"{scheme}://{baseUri.Authority}/ws";
	}
}
