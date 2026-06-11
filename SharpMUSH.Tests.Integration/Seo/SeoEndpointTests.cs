using SharpMUSH.Tests.Infrastructure;
using System.Net;

namespace SharpMUSH.Tests.Integration.Seo;

/// <summary>
/// HTTP-level integration tests for <c>SeoController</c>.
/// Uses the in-process <see cref="ServerWebAppFactory"/> test server so every request travels
/// through the full ASP.NET Core middleware pipeline without touching a real network socket.
///
/// NOTE: Do NOT implement IAsyncInitializer here — TUnit's ClassDataSource calls
/// ServerWebAppFactory.InitializeAsync() exactly once for the session. Calling it
/// again from a test class would double-init the host and crash with a duplicate-key
/// exception in Functions..ctor (static function library built at first startup).
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class SeoEndpointTests(ServerWebAppFactory factory)
{
	[Test]
	public async Task GetSitemap_Returns200XmlWithHomePage()
	{
		var http = factory.CreateHttpClient();

		var response = await http.GetAsync("sitemap.xml");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var contentType = response.Content.Headers.ContentType?.ToString() ?? string.Empty;
		await Assert.That(contentType.StartsWith("application/xml")).IsTrue();

		var body = await response.Content.ReadAsStringAsync();
		// Wiki URLs are category-qualified: /wiki/{ns}/{category}/{slug}. The seeded home page lives at
		// main/general/home.
		await Assert.That(body).Contains("/wiki/main/general/home");
	}

	[Test]
	public async Task GetRobotsTxt_Returns200WithSitemapDirective()
	{
		var http = factory.CreateHttpClient();

		var response = await http.GetAsync("robots.txt");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var body = await response.Content.ReadAsStringAsync();
		await Assert.That(body).Contains("Sitemap:");
	}
}
