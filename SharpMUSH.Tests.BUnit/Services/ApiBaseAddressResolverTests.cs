using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// Coverage for how the "api" HttpClient's BaseAddress is derived from the WASM host's base
/// address (see <see cref="ApiBaseAddressResolver"/>).
///
/// Regression coverage for the "Game is starting up" hang: the client used to rewrite the host
/// address to a hardcoded https://…:8081. That is only correct for the dev launch profile
/// (SharpMUSH.Server/Properties/launchSettings.json binds https://localhost:8081), and it breaks
/// every reverse-proxied deployment — behind Cloudflare the site is served on 443 and :8081 is
/// not routable at all, so ServerStartupGate's /api/health probe could never succeed and the app
/// sat behind the startup screen forever.
///
/// The rule: same-origin by default (correct behind any proxy), with an explicit opt-in override
/// for dev, supplied via appsettings.Development.json rather than baked into the code.
/// </summary>
public class ApiBaseAddressResolverTests
{
	[TUnit.Core.Test]
	public async Task NoOverride_UsesHostBaseAddressUnchanged()
	{
		// The deployed case: served from https://mush.sharpmush.com/, the API is the same origin.
		var resolved = ApiBaseAddressResolver.Resolve("https://mush.sharpmush.com/", configuredOverride: null);

		await Assert.That(resolved.ToString()).IsEqualTo("https://mush.sharpmush.com/");
	}

	[TUnit.Core.Test]
	public async Task NoOverride_DoesNotRewriteSchemeOrPort()
	{
		// A non-default port that the host is genuinely served on must survive untouched — the
		// resolver's job is not to invent an endpoint.
		var resolved = ApiBaseAddressResolver.Resolve("http://localhost:5000/", configuredOverride: null);

		await Assert.That(resolved.ToString()).IsEqualTo("http://localhost:5000/");
	}

	[TUnit.Core.Test]
	public async Task Override_TakesPrecedenceOverHostBaseAddress()
	{
		// The dev case, expressed as config instead of a hardcode: client served over http:8080,
		// API reached over https:8081.
		var resolved = ApiBaseAddressResolver.Resolve("http://localhost:8080/", configuredOverride: "https://localhost:8081/");

		await Assert.That(resolved.ToString()).IsEqualTo("https://localhost:8081/");
	}

	[TUnit.Core.Test]
	public async Task BlankOverride_IsIgnored()
	{
		// An empty/whitespace key in appsettings must not produce a broken relative Uri.
		var resolved = ApiBaseAddressResolver.Resolve("https://mush.sharpmush.com/", configuredOverride: "   ");

		await Assert.That(resolved.ToString()).IsEqualTo("https://mush.sharpmush.com/");
	}

	[TUnit.Core.Test]
	public async Task Override_WithoutTrailingSlash_StillResolvesRelativePathsUnderIt()
	{
		// HttpClient drops the last path segment of a BaseAddress that lacks a trailing slash, so
		// "api/health" against ".../base" would silently become ".../api/health" at the root.
		var resolved = ApiBaseAddressResolver.Resolve("https://example.test/", configuredOverride: "https://api.example.test/base");

		var probe = new Uri(resolved, "api/health");
		await Assert.That(probe.ToString()).IsEqualTo("https://api.example.test/base/api/health");
	}
}
