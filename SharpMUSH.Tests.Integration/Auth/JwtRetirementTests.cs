using SharpMUSH.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.Integration.Auth;

/// <summary>
/// Proves the JWT/refresh-token hybrid (Task 8) is fully retired: the <c>jwt-*</c> endpoints
/// no longer exist, leaving <c>AccountSession</c> as the single web credential scheme.
///
/// A removed route under <c>api/</c> lands on the API fallback (<c>PortalStaticFiles.MapPortal</c>), which
/// answers <c>404</c> for every method rather than the SPA shell. The important assertion is that these calls
/// no longer succeed (200) or reach JWT-specific logic (401/501): the route is simply gone.
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
		await Assert.That(r1.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

		var r2 = await http.PostAsJsonAsync("api/auth/jwt-refresh", new { RefreshToken = "z" });
		await Assert.That(r2.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

		var r3 = await http.PostAsJsonAsync("api/auth/jwt-switch-character",
			new { AccountSessionToken = "z", CharacterKey = 1, CharacterCreationTime = 0L });
		await Assert.That(r3.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}
}
