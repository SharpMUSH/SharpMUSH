namespace SharpMUSH.Client.Services;

/// <summary>
/// Derives the "api" HttpClient's BaseAddress from the WASM host's base address.
///
/// Same-origin by default: the server that serves the client also serves /api, so whatever scheme,
/// host and port the app was loaded from is already the right target — and it is the only answer
/// that survives a reverse proxy (behind Cloudflare the site is served on 443; a rewritten port is
/// not routable). Dev setups that split the two — the launch profile serves the client over
/// http:8080 while the API listens on https:8081 — opt in explicitly via the "ApiBaseAddress" key
/// in appsettings.Development.json instead of the code assuming it.
/// </summary>
public static class ApiBaseAddressResolver
{
	public const string ConfigurationKey = "ApiBaseAddress";

	/// <param name="hostBaseAddress">The WASM host's base address (where the client was loaded from).</param>
	/// <param name="configuredOverride">Optional absolute URI from configuration; blank means "same origin".</param>
	public static Uri Resolve(string hostBaseAddress, string? configuredOverride)
	{
		var target = string.IsNullOrWhiteSpace(configuredOverride)
			? hostBaseAddress
			: configuredOverride;

		// HttpClient resolves a relative request against a BaseAddress by replacing its last path
		// segment, so a base without a trailing slash would drop "/base" from "/base/api/health".
		if (!target.EndsWith('/'))
		{
			target += "/";
		}

		return new Uri(target, UriKind.Absolute);
	}
}
