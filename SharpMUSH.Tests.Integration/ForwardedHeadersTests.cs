using SharpMUSH.Tests.Infrastructure;
using System.Net;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// Verifies the forwarded-headers pipeline (Task 14): behind a proxy, the client IP captured on
/// sessions (Task 1) and matched by sitelock (Tasks 13/15) must be the real client IP, not the
/// proxy's. This is only trustworthy when the proxy hop is explicitly known — an untrusted remote
/// must never be able to spoof its own IP via <c>X-Forwarded-For</c>.
///
/// The dev-only <c>GET api/debug/client-ip</c> endpoint (mirrors <c>AuthController.GetDebugOtt</c>'s
/// <c>IsDevelopment()</c> gate) echoes <see cref="Microsoft.AspNetCore.Http.HttpContext"/>'s resolved
/// <c>Connection.RemoteIpAddress</c> so the test can observe what the middleware pipeline produced.
/// The shared test host's default config has an empty <c>ForwardedHeaders:KnownProxies</c> list, so
/// this exercises the spoof-resistant (no trusted proxies configured) path.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class ForwardedHeadersTests(ServerWebAppFactory factory)
{
	private HttpClient CreateClient()
	{
		var http = factory.CreateHttpClient();
		http.BaseAddress = new Uri("https://localhost/");
		return http;
	}

	private static async Task<string?> GetClientIpAsync(HttpClient http, string? forwardedFor = null)
	{
		var request = new HttpRequestMessage(HttpMethod.Get, "api/debug/client-ip");
		if (forwardedFor is not null)
		{
			request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
		}

		var response = await http.SendAsync(request);
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		return await response.Content.ReadAsStringAsync();
	}

	/// <summary>
	/// Spoof-resistance (the required assertion): the test host trusts no proxies
	/// (<c>ForwardedHeaders:KnownProxies</c> is empty by default), so an <c>X-Forwarded-For</c>
	/// header from the untrusted TestServer remote must be ignored entirely — the observed client
	/// IP must be the real (loopback) connection IP, never the attacker-supplied
	/// <c>203.0.113.99</c>.
	/// </summary>
	[Test]
	public async Task ClientIp_XForwardedForFromUntrustedRemote_IsIgnored()
	{
		var http = CreateClient();

		var baseline = await GetClientIpAsync(http);
		var spoofed = await GetClientIpAsync(http, "203.0.113.99");

		await Assert.That(spoofed).IsNotNull();
		await Assert.That(spoofed).IsNotEqualTo("203.0.113.99");
		await Assert.That(spoofed).IsEqualTo(baseline);
	}
}
