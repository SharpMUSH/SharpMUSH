using SharpMUSH.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.Integration.Auth;

/// <summary>
/// Proves the JWT/refresh-token hybrid (Task 8) is fully retired: the <c>jwt-*</c> endpoints
/// no longer exist, leaving <c>AccountSession</c> as the single web credential scheme.
///
/// A removed POST route lands on the SPA fallback (<c>MapFallbackToFile("index.html")</c> in
/// <c>Program.cs</c>), which is registered for GET/HEAD only — so an unmatched POST resolves to
/// <c>405 MethodNotAllowed</c>, not <c>404</c>. This is deterministic, environment-independent
/// ASP.NET Core routing behavior (verified against completely unrelated undefined POST routes
/// too, e.g. <c>api/account/does-not-exist</c>), not an artifact of the jwt-* removal. The
/// important assertion is that these calls no longer succeed (200) or reach JWT-specific logic
/// (401/501) — the route is simply gone.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class JwtRetirementTests(ServerWebAppFactory factory)
{
	private HttpClient CreateClient()
	{
		var http = factory.CreateHttpClient();
		http.BaseAddress = new Uri("https://localhost/");
		return http;
	}

	[Test]
	public async Task JwtEndpoints_AreGone()
	{
		var http = CreateClient();
		var r1 = await http.PostAsJsonAsync("api/auth/jwt-login", new { UsernameOrEmail = "x", Password = "y", CharacterKey = 1, CharacterCreationTime = 0L });
		await Assert.That(r1.StatusCode).IsEqualTo(HttpStatusCode.MethodNotAllowed);

		var r2 = await http.PostAsJsonAsync("api/auth/jwt-refresh", new { RefreshToken = "z" });
		await Assert.That(r2.StatusCode).IsEqualTo(HttpStatusCode.MethodNotAllowed);

		var r3 = await http.PostAsJsonAsync("api/auth/jwt-switch-character",
			new { AccountSessionToken = "z", CharacterKey = 1, CharacterCreationTime = 0L });
		await Assert.That(r3.StatusCode).IsEqualTo(HttpStatusCode.MethodNotAllowed);
	}
}
