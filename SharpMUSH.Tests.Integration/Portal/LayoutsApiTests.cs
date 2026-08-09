using System.Net;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Integration.Portal;

/// <summary>
/// The layout read path on a clean install, over real HTTP and unauthenticated (reads are anonymous —
/// a guest browsing the front page must not be asked to log in).
///
/// Every page view asks for its scope's layout, so an uncustomized game must not answer with an
/// error: a 4xx here put one failed request on the wire for every route in the portal and made the
/// browser console permanently noisy, which is what hid genuine failures. 204 is the contract —
/// request succeeded, no stored override, use the code default.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class LayoutsApiTests(ServerWebAppFactory factory)
{
	[Test]
	[Arguments("global")]
	[Arguments("home")]
	[Arguments("wiki-index")]
	[Arguments("profile")]
	[Arguments("a-scope-nobody-has-ever-heard-of")]
	public async Task Get_UncustomizedScope_Returns204AndNoBody(string scope)
	{
		var http = factory.CreateHttpClient();
		var response = await http.GetAsync($"api/layouts/{scope}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
		await Assert.That((await response.Content.ReadAsStringAsync()).Length).IsEqualTo(0);
	}
}
