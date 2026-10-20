using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Models.Wiki;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>Captures actual HTTP-backed client reads; these URI tests make no authorization claim.</summary>
public class WikiRequestRoutingTests
{
	private sealed class CaptureHandler : HttpMessageHandler
	{
		public List<Uri> Requests { get; } = [];

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Requests.Add(request.RequestUri ?? throw new InvalidOperationException("The wiki request has no URI."));
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(new WikiPageDto(
					"wiki_page/42", "guide", "Guide", "help", "Body", "<p>Body</p>", "Body",
					DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, false, 1, [], true))
			});
		}
	}

	private sealed class ApiFactory(HttpClient client) : IHttpClientFactory
	{
		public HttpClient CreateClient(string name) => name == "api" ? client
			: throw new InvalidOperationException("Unexpected HTTP client: " + name);
	}

	[Test]
	[Arguments("wiki_page/42", null)]
	[Arguments("wiki_page/42", "fr-CA")]
	[Arguments("wiki_page/42?label=a&b +%2F#門", "ja-JP")]
	[Arguments("", "en")]
	public async Task ConstrainedArticleUsesCanonicalRouteAndEncodesExpectedIdentityExactlyOnce(string expectedId, string? locale)
	{
		using var handler = new CaptureHandler();
		using var http = new HttpClient(handler) { BaseAddress = new Uri("https://client.example/") };
		var wiki = new WikiService(new ApiFactory(http), NullLogger<WikiService>.Instance);

		var article = (await wiki.GetWikiArticle("guide", "help", locale, expectedId)).Expect<WikiArticle>();

		await Assert.That(article.Id).IsEqualTo("wiki_page/42");
		var request = handler.Requests.Single();
		await Assert.That(request.AbsolutePath).IsEqualTo("/api/wiki/ns/help/guide");
		var query = QueryHelpers.ParseQuery(request.Query);
		// An independent HTTP query parser decodes once. A twice-escaped ID would retain escapes and fail this equality.
		await Assert.That(query["expectedPageId"].Single()).IsEqualTo(expectedId);
		await Assert.That(query.TryGetValue("lang", out var language) ? language.Single() : null).IsEqualTo(locale);
		await Assert.That(query.Count).IsEqualTo(locale is null ? 1 : 2);
	}

	[Test]
	[Arguments(null)]
	[Arguments("fr-CA")]
	public async Task ExistingThreeArgumentArticleReadKeepsItsUnconstrainedRequest(string? locale)
	{
		using var handler = new CaptureHandler();
		using var http = new HttpClient(handler) { BaseAddress = new Uri("https://client.example/") };
		var wiki = new WikiService(new ApiFactory(http), NullLogger<WikiService>.Instance);

		var article = (await wiki.GetWikiArticle("guide", "help", locale)).Expect<WikiArticle>();

		await Assert.That(article.Title).IsEqualTo("Guide");
		var request = handler.Requests.Single();
		await Assert.That(request.AbsolutePath).IsEqualTo("/api/wiki/ns/help/guide");
		var query = QueryHelpers.ParseQuery(request.Query);
		await Assert.That(query.ContainsKey("expectedPageId")).IsFalse();
		await Assert.That(query.TryGetValue("lang", out var language) ? language.Single() : null).IsEqualTo(locale);
		await Assert.That(query.Count).IsEqualTo(locale is null ? 0 : 1);
	}

	[Test]
	public async Task LandingUriRoundTripsToTheActualCanonicalClientRead()
	{
		var landing = new WikiLandingPageDto("wiki_page/42", "help", "guide", "Guide", "en", true);
		var state = new WikiBrowseState("package:rules", "Règle & C++", "actions", 100, "fr-CA");
		var href = WikiJourneyLinks.Landing(landing, state);
		var address = new Uri(new Uri("https://client.example/"), href);
		var journey = QueryHelpers.ParseQuery(address.Query);
		var route = address.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
		await Assert.That(address.AbsolutePath).IsEqualTo("/wiki/help/guide");
		await Assert.That(journey["browseCollection"].Single()).IsEqualTo("package:rules");
		await Assert.That(journey["browseQ"].Single()).IsEqualTo("Règle & C++");
		await Assert.That(journey["membersCollection"].Single()).IsEqualTo("package:rules");

		using var handler = new CaptureHandler();
		using var http = new HttpClient(handler) { BaseAddress = new Uri("https://client.example/") };
		var wiki = new WikiService(new ApiFactory(http), NullLogger<WikiService>.Instance);
		(await wiki.GetWikiArticle(Uri.UnescapeDataString(route[2]), Uri.UnescapeDataString(route[1]),
			journey["lang"].Single(), journey["expectedPageId"].Single())).Expect<WikiArticle>();

		var request = handler.Requests.Single();
		await Assert.That(request.AbsolutePath).IsEqualTo("/api/wiki/ns/help/guide");
		var apiQuery = QueryHelpers.ParseQuery(request.Query);
		await Assert.That(apiQuery["expectedPageId"].Single()).IsEqualTo(landing.PageId);
		await Assert.That(apiQuery["lang"].Single()).IsEqualTo("fr-CA");
		await Assert.That(apiQuery.Keys.Order().ToArray()).IsEquivalentTo(new[] { "expectedPageId", "lang" });
	}
}
