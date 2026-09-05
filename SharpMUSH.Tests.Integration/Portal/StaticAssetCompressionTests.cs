using SharpMUSH.Tests.Infrastructure;
using System.Net.Http.Headers;

namespace SharpMUSH.Tests.Integration.Portal;

/// <summary>
/// The server compresses what it sends.
///
/// <para>It did not. <c>UseBlazorFrameworkFiles</c> serves the pre-brotlied <c>_framework</c> files,
/// so the .NET runtime arrived compressed and everything else did not — and everything else is the
/// bulk of a first visit. A cold load of the anonymous home page pulled <b>15.0 MB over 149
/// requests</b>, of which Monaco's <c>editor.api</c> (3.67 MB), Mermaid (2.57 MB), MudBlazor's CSS
/// (0.62 MB), EasyMDE (0.33 MB) and <c>mush-defs.json</c> (0.76 MB) were served as raw bytes with no
/// <c>Content-Encoding</c> at all. Publishing emits <c>.br</c>/<c>.gz</c> beside the client's own
/// <c>js/*</c>, but nothing served them, and the <c>_content/**</c> assets — the largest ones — have
/// no precompressed variants to serve.</para>
///
/// <para>Asserted on a JSON API response rather than a file because the test host has no client
/// <c>wwwroot</c> to serve; it is the same middleware either way, and <c>application/json</c> is the
/// MIME type <c>mush-defs.json</c> ships under. The real files are checked against a published
/// server.</para>
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class StaticAssetCompressionTests(ServerWebAppFactory factory)
{
	private HttpClient CreateClient()
	{
		var http = factory.CreateHttpClient();
		http.BaseAddress = new Uri("https://localhost/");
		return http;
	}

	[Test]
	public async Task Responses_AreBrotliCompressed_WhenTheClientAsksForIt()
	{
		var http = CreateClient();
		using var request = new HttpRequestMessage(HttpMethod.Get, "api/health");
		request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));

		using var response = await http.SendAsync(request);

		await Assert.That(response.Content.Headers.ContentEncoding).Contains("br");
	}

	/// <summary>
	/// A client that only speaks gzip still gets a compressed body — brotli is not universal, and a
	/// server that offered nothing else would hand those clients the full uncompressed payload.
	/// </summary>
	[Test]
	public async Task Responses_FallBackToGzip_ForAClientThatCannotDoBrotli()
	{
		var http = CreateClient();
		using var request = new HttpRequestMessage(HttpMethod.Get, "api/health");
		request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));

		using var response = await http.SendAsync(request);

		await Assert.That(response.Content.Headers.ContentEncoding).Contains("gzip");
	}

	/// <summary>
	/// Asking for nothing gets nothing: a client that sends no Accept-Encoding must not be handed a
	/// body it cannot read.
	/// </summary>
	[Test]
	public async Task Responses_AreUncompressed_WhenTheClientAsksForNothing()
	{
		var http = CreateClient();
		using var request = new HttpRequestMessage(HttpMethod.Get, "api/health");

		using var response = await http.SendAsync(request);

		await Assert.That(response.Content.Headers.ContentEncoding).IsEmpty();
	}
}
