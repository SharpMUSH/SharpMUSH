using SharpMUSH.Tests.Wiki;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.DiscriminatedUnions;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SharpMUSH.Tests.Shared;

namespace SharpMUSH.Tests.Client.Services;

/// <summary>
/// Unit tests for <see cref="WikiService"/>.
///
/// These tests use a capturing <see cref="HttpMessageHandler"/> to assert the exact
/// outgoing request shape (URL, verb, Content-Type, JSON body) and a canned-response
/// handler to verify deserialization and error-path behaviour — all without a server.
///
/// They complement the HTTP controller integration tests
/// (WikiHttpControllerTests) which verify the server side of the same contract.
/// </summary>
public class WikiServiceTests : TrackingTestContext
{
	/// <summary>Builds a minimal valid WikiPageDto JSON string.</summary>
	private static string PageDtoJson(
		string id = "node_wiki_pages/1",
		string slug = "home",
		string title = "Home",
		string ns = "Main",
		string markdown = "# Home",
		string html = "<h1>Home</h1>",
		int revision = 2,
		string? lastEditedBy = null,
		string? image = null) =>
		$$"""
		{
		  "id": "{{id}}",
		  "slug": "{{slug}}",
		  "title": "{{title}}",
		  "namespace": "{{ns}}",
		  "markdownSource": "{{markdown}}",
		  "renderedHtml": "{{html}}",
		  "plainText": "",
		  "createdAt": "2025-01-01T00:00:00Z",
		  "updatedAt": "2025-01-02T00:00:00Z",
		  "isProtected": false,
		  "revisionNumber": {{revision}},
		  "lastEditedBy": {{(lastEditedBy is null ? "null" : $"\"{lastEditedBy}\"")}},
		  "image": {{(image is null ? "null" : $"\"{image}\"")}}
		}
		""";

	private WikiService BuildService(HttpMessageHandler handler, out CapturingHttpHandler? capturing)
	{
		capturing = handler as CapturingHttpHandler;
		var http = Track(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(http);
		var logger = Substitute.For<ILogger<WikiService>>();
		return new WikiService(factory, logger);
	}

	private WikiService BuildService(HttpStatusCode code, string body, out CapturingHttpHandler capturing)
	{
		var handler = CapturingHttpHandler.WithJson(code, body);
		capturing = handler;
		return BuildService(handler, out _);
	}

	[Test]
	public async Task UpdatePageAsync_200_ReturnsSuccessWithCorrectSlug()
	{
		var service = BuildService(HttpStatusCode.OK, PageDtoJson(), out _);

		var result = await service.UpdatePageAsync("home", "# Home", null);

		var article = result.Expect<WikiArticle>();
		await Assert.That(article.Slug).IsEqualTo("home");
	}

	[Test]
	public async Task UpdatePageAsync_200_MapsMarkdownSourceToContent()
	{
		var service = BuildService(HttpStatusCode.OK, PageDtoJson(markdown: "## Updated"), out _);

		var result = await service.UpdatePageAsync("home", "## Updated", null);

		var article = result.Expect<WikiArticle>();
		await Assert.That(article.Content).IsEqualTo("## Updated");
	}

	[Test]
	public async Task UpdatePageAsync_200_MapsLastEditedByImageAndUpdatedAt()
	{
		// D1 README §6.2: the banner shows the first image and "Last edited by X · when".
		var service = BuildService(HttpStatusCode.OK, PageDtoJson(lastEditedBy: "Ilsa Varn", image: "/api/wiki-assets/a/quay.jpg"), out _);

		var article = (await service.UpdatePageAsync("home", "# Home", null)).Expect<WikiArticle>();

		await Assert.That(article.LastEditedBy).IsEqualTo("Ilsa Varn");
		await Assert.That(article.Image).IsEqualTo("/api/wiki-assets/a/quay.jpg");
		await Assert.That(article.UpdatedAt).IsEqualTo(DateTimeOffset.Parse("2025-01-02T00:00:00Z"));
	}

	[Test]
	public async Task GetRecentChangesAsync_MapsImageAndLastEditedByOntoSummaries()
	{
		var service = BuildService(HttpStatusCode.OK, "[" + PageDtoJson(slug: "quay", lastEditedBy: "Wren", image: "/q.jpg") + "]", out _);

		var summaries = await service.GetRecentChangesAsync(10);

		await Assert.That(summaries.Count).IsEqualTo(1);
		await Assert.That(summaries[0].LastEditedBy).IsEqualTo("Wren");
		await Assert.That(summaries[0].Image).IsEqualTo("/q.jpg");
	}

	[Test]
	public async Task UpdatePageAsync_200_MapsRenderedHtml()
	{
		var service = BuildService(HttpStatusCode.OK, PageDtoJson(html: "<h2>Updated</h2>"), out _);

		var result = await service.UpdatePageAsync("home", "## Updated", null);

		var article = result.Expect<WikiArticle>();
		await Assert.That(article.RenderedHtml).IsEqualTo("<h2>Updated</h2>");
	}

	[Test]
	public async Task UpdatePageAsync_404_ReturnsNotFoundFailure()
	{
		var service = BuildService(HttpStatusCode.NotFound, "Not Found", out _);

		var result = await service.UpdatePageAsync("does-not-exist", "# X", null);

		var failure = result.Expect<ApiFailure>();
		await Assert.That(failure.Kind).IsEqualTo(ApiFailureKind.NotFound);
		await Assert.That(failure.Status).IsEqualTo(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task UpdatePageAsync_500_ReturnsTheServersSentence()
	{
		var service = BuildService(HttpStatusCode.InternalServerError, "oops", out _);

		var result = await service.UpdatePageAsync("home", "# X", null);

		var failure = result.Expect<ApiFailure>();
		await Assert.That(failure.Status).IsEqualTo(HttpStatusCode.InternalServerError);
		await Assert.That(failure.Message).IsEqualTo("oops");
	}

	[Test]
	public async Task UpdatePageAsync_SendsPutToCorrectUrl()
	{
		var service = BuildService(HttpStatusCode.OK, PageDtoJson(), out var handler);

		await service.UpdatePageAsync("home", "# Home", null);

		await Assert.That(handler!.LastRequest).IsNotNull();
		await Assert.That(handler.LastRequest!.Method).IsEqualTo(HttpMethod.Put);
		await Assert.That(handler.LastRequest.RequestUri!.ToString())
			.Contains("api/wiki/home");
	}

	[Test]
	public async Task UpdatePageAsync_SlugsWithSpecialChars_ArePercentEncoded()
	{
		var service = BuildService(HttpStatusCode.OK, PageDtoJson(slug: "hello world"), out var handler);

		await service.UpdatePageAsync("hello world", "# X", null);

		// Use AbsoluteUri (percent-encoded form) — Uri.ToString() returns the unescaped form.
		var uri = handler!.LastRequest!.RequestUri!.AbsoluteUri;
		await Assert.That(uri).Contains("hello%20world");
		await Assert.That(uri).DoesNotContain("hello world");
	}

	[Test]
	public async Task UpdatePageAsync_SendsCorrectJsonBody()
	{
		var service = BuildService(HttpStatusCode.OK, PageDtoJson(), out var handler);

		await service.UpdatePageAsync("home", "# Hello", "my summary");

		await Assert.That(handler!.LastBody).IsNotNull();

		using var doc = JsonDocument.Parse(handler.LastBody!);
		var root = doc.RootElement;
		await Assert.That(root.GetProperty("markdown").GetString()).IsEqualTo("# Hello");
		await Assert.That(root.GetProperty("editSummary").GetString()).IsEqualTo("my summary");
	}

	[Test]
	public async Task UpdatePageAsync_SendsApplicationJsonContentType()
	{
		var service = BuildService(HttpStatusCode.OK, PageDtoJson(), out var handler);

		await service.UpdatePageAsync("home", "# Home", null);

		var contentType = handler!.LastRequest!.Content!.Headers.ContentType!.MediaType;
		await Assert.That(contentType).IsEqualTo("application/json");
	}

	[Test]
	public async Task GetWikiArticle_200_ReturnsMappedArticle()
	{
		var service = BuildService(HttpStatusCode.OK, PageDtoJson(slug: "home", title: "Home"), out _);

		var result = await service.GetWikiArticle("home");

		var article = result.Expect<WikiArticle>();
		await Assert.That(article.Title).IsEqualTo("Home");
		await Assert.That(article.Slug).IsEqualTo("home");
	}

	[Test]
	public async Task GetWikiArticle_404_ReturnsNotFound()
	{
		var service = BuildService(HttpStatusCode.NotFound, "", out _);

		var result = await service.GetWikiArticle("missing");

		result.Expect<NotFound>();
	}

	/// <summary>A server that does not answer is not a page that does not exist: the view must not offer to create it.</summary>
	[Test]
	[Arguments(HttpStatusCode.ServiceUnavailable)]
	[Arguments(HttpStatusCode.InternalServerError)]
	public async Task GetWikiArticle_AServerFailure_IsAnError_NotNotFound(HttpStatusCode code)
	{
		var service = BuildService(code, "", out _);

		var result = await service.GetWikiArticle("home");

		await Assert.That(result.Expect<Error<string>>().Value).IsNotEmpty();
	}

	[Test]
	public async Task GetWikiArticle_SendsGetToCorrectUrl()
	{
		var service = BuildService(HttpStatusCode.OK, PageDtoJson(), out var handler);

		await service.GetWikiArticle("home");

		await Assert.That(handler!.LastRequest!.Method).IsEqualTo(HttpMethod.Get);
		await Assert.That(handler.LastRequest.RequestUri!.ToString())
			.Contains("api/wiki/ns/main/home");
	}

	[Test]
	public async Task CreatePageAsync_201_ReturnsMappedArticle()
	{
		var service = BuildService(HttpStatusCode.Created, PageDtoJson(slug: "my-page", title: "My Page"), out _);

		var result = await service.CreatePageAsync("My Page", "# My Page");

		var article = result.Expect<WikiArticle>();
		await Assert.That(article.Title).IsEqualTo("My Page");
		await Assert.That(article.Slug).IsEqualTo("my-page");
	}

	[Test]
	public async Task CreatePageAsync_409_ReturnsTheServersReason()
	{
		var service = BuildService(HttpStatusCode.Conflict, """{"error":"slug already exists"}""", out _);

		var result = await service.CreatePageAsync("Duplicate", "# Dup");

		var failure = result.Expect<ApiFailure>();
		await Assert.That(failure.Status).IsEqualTo(HttpStatusCode.Conflict);
		await Assert.That(failure.Message).IsEqualTo("slug already exists");
	}

	[Test]
	public async Task CreatePageAsync_SendsPostToCorrectUrl()
	{
		var service = BuildService(HttpStatusCode.Created, PageDtoJson(), out var handler);

		await service.CreatePageAsync("Test", "# Test");

		await Assert.That(handler!.LastRequest!.Method).IsEqualTo(HttpMethod.Post);
		await Assert.That(handler.LastRequest.RequestUri!.ToString())
			.EndsWith("api/wiki");
	}

	[Test]
	public async Task CreatePageAsync_SendsCorrectJsonBody()
	{
		var service = BuildService(HttpStatusCode.Created, PageDtoJson(), out var handler);

		await service.CreatePageAsync("My Title", "# Content", "Character");

		await Assert.That(handler!.LastBody).IsNotNull();

		using var doc = JsonDocument.Parse(handler.LastBody!);
		var root = doc.RootElement;
		await Assert.That(root.GetProperty("title").GetString()).IsEqualTo("My Title");
		await Assert.That(root.GetProperty("markdown").GetString()).IsEqualTo("# Content");
		await Assert.That(root.GetProperty("namespace").GetString()).IsEqualTo("Character");
	}

	/// <summary>A body that records whether the response carrying it was disposed.</summary>
	private sealed class DisposalTrackingContent() : StringContent("refused", Encoding.UTF8, "text/plain")
	{
		public bool Disposed { get; private set; }

		protected override void Dispose(bool disposing)
		{
			Disposed = true;
			base.Dispose(disposing);
		}
	}

	/// <summary>Answers every request with a fresh 500 and keeps the body it sent.</summary>
	private sealed class RefusingTrackingHandler : HttpMessageHandler
	{
		public List<DisposalTrackingContent> Sent { get; } = [];

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var content = new DisposalTrackingContent();
			Sent.Add(content);
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = content });
		}
	}

	/// <summary>
	/// The handler hands each response to the service, which owns it from then on: every call that
	/// reads a raw <see cref="HttpResponseMessage"/> disposes it, on the failure path as well.
	/// </summary>
	[Test]
	public async Task EveryRawResponseIsDisposed()
	{
		var handler = new RefusingTrackingHandler();
		var service = BuildService(handler, out _);
		string[] refs = ["main/home"];

		await service.GetAllPagesAsync();
		await service.UpsertTranslationAsync("home", "fr", "Accueil", "# Accueil", true, 1);
		await service.DeleteTranslationAsync("home", "fr");
		await service.CreatePageAsync("Home", "# Home");
		await service.UpdatePageAsync("home", "# Home");
		await service.SetMetadataAsync("home", [], true);
		await service.RollbackAsync("home", 1);
		await service.CheckExistsAsync(refs);
		await service.BatchProtectAsync(refs, true);
		await service.BatchDeleteAsync(refs);
		await service.DeletePageAsync("home");

		await Assert.That(handler.Sent.Count).IsEqualTo(11);
		await Assert.That(handler.Sent.Count(c => !c.Disposed)).IsEqualTo(0);
	}

	/// <summary>
	/// The home page's stats tile and recent-activity widget both read the last ten changes on the
	/// same render; while one read is in flight the other joins it.
	/// </summary>
	[Test]
	public async Task GetRecentChangesAsync_ConcurrentCalls_FetchOnce()
	{
		var handler = new GatedHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent($"[{PageDtoJson()}]", Encoding.UTF8, "application/json")
		});
		var service = BuildService(handler, out _);

		var first = service.GetRecentChangesAsync(10).AsTask();
		var second = service.GetRecentChangesAsync(10).AsTask();
		handler.Release();

		await Assert.That((await first).Count).IsEqualTo(1);
		await Assert.That((await second).Count).IsEqualTo(1);
		await Assert.That(handler.CallsTo("/api/wiki/recent?count=10")).IsEqualTo(1);

		await service.GetRecentChangesAsync(10);
		await Assert.That(handler.CallsTo("/api/wiki/recent?count=10")).IsEqualTo(2);
	}

	/// <summary>
	/// The server pages over the rows this caller may see (#1478), so a batch shorter than the one
	/// asked for is the last: the listing does not spend a request to learn that the next is empty.
	/// </summary>
	[Test]
	public async Task GetAllByCategoryAsync_AShortBatchIsTheLast()
	{
		var calls = 0;
		var handler = new CapturingHttpHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(
				calls++ == 0 ? $"[{PageDtoJson(slug: "a")},{PageDtoJson(slug: "b")}]" : "[]",
				Encoding.UTF8, "application/json")
		});
		var service = BuildService(handler, out _);

		var pages = await service.GetAllByCategoryAsync("Lore");

		await Assert.That(pages.Select(p => p.Slug)).IsEquivalentTo(["a", "b"]);
		await Assert.That(handler.Requests.Count).IsEqualTo(1);
	}
}
