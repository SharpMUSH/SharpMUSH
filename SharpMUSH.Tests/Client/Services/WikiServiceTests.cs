using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.Client.Services;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

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
public class WikiServiceTests
{
	// ── Helpers ────────────────────────────────────────────────────────────

	/// <summary>
	/// Returns a canned HTTP response and records the request for later inspection.
	/// </summary>
	private sealed class CapturingHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
	{
		public HttpRequestMessage? CapturedRequest { get; private set; }
		public string? CapturedRequestBody { get; private set; }

		protected override async Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			CapturedRequest = request;
			if (request.Content is not null)
				CapturedRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

			return new HttpResponseMessage(statusCode)
			{
				Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
			};
		}
	}

	/// <summary>Builds a minimal valid WikiPageDto JSON string.</summary>
	private static string PageDtoJson(
		string id = "node_wiki_pages/1",
		string slug = "home",
		string title = "Home",
		string ns = "Main",
		string markdown = "# Home",
		string html = "<h1>Home</h1>",
		int revision = 2) =>
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
		  "revisionNumber": {{revision}}
		}
		""";

	private static WikiService BuildService(HttpMessageHandler handler, out CapturingHandler? capturing)
	{
		capturing = handler as CapturingHandler;
		var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(http);
		var logger = Substitute.For<ILogger<WikiService>>();
		return new WikiService(factory, logger);
	}

	private static WikiService BuildService(HttpStatusCode code, string body, out CapturingHandler capturing)
	{
		var handler = new CapturingHandler(code, body);
		capturing = handler;
		return BuildService(handler, out _);
	}

	// ── UpdatePageAsync ────────────────────────────────────────────────────

	[Test]
	public async Task UpdatePageAsync_200_ReturnsSuccessWithCorrectSlug()
	{
		var service = BuildService(HttpStatusCode.OK, PageDtoJson(), out _);

		var result = await service.UpdatePageAsync("home", "# Home", null);

		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(result.AsT0.Slug).IsEqualTo("home");
	}

	[Test]
	public async Task UpdatePageAsync_200_MapsMarkdownSourceToContent()
	{
		var service = BuildService(HttpStatusCode.OK, PageDtoJson(markdown: "## Updated"), out _);

		var result = await service.UpdatePageAsync("home", "## Updated", null);

		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(result.AsT0.Content).IsEqualTo("## Updated");
	}

	[Test]
	public async Task UpdatePageAsync_200_MapsRenderedHtml()
	{
		var service = BuildService(HttpStatusCode.OK, PageDtoJson(html: "<h2>Updated</h2>"), out _);

		var result = await service.UpdatePageAsync("home", "## Updated", null);

		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(result.AsT0.RenderedHtml).IsEqualTo("<h2>Updated</h2>");
	}

	[Test]
	public async Task UpdatePageAsync_404_ReturnsErrorString()
	{
		var service = BuildService(HttpStatusCode.NotFound, "Not Found", out _);

		var result = await service.UpdatePageAsync("does-not-exist", "# X", null);

		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(result.AsT1).Contains("404");
	}

	[Test]
	public async Task UpdatePageAsync_500_ReturnsErrorString()
	{
		var service = BuildService(HttpStatusCode.InternalServerError, "oops", out _);

		var result = await service.UpdatePageAsync("home", "# X", null);

		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(result.AsT1).Contains("500");
	}

	[Test]
	public async Task UpdatePageAsync_SendsPutToCorrectUrl()
	{
		var service = BuildService(HttpStatusCode.OK, PageDtoJson(), out var handler);

		await service.UpdatePageAsync("home", "# Home", null);

		await Assert.That(handler!.CapturedRequest).IsNotNull();
		await Assert.That(handler.CapturedRequest!.Method).IsEqualTo(HttpMethod.Put);
		await Assert.That(handler.CapturedRequest.RequestUri!.ToString())
			.Contains("api/wiki/home");
	}

	[Test]
	public async Task UpdatePageAsync_SlugsWithSpecialChars_ArePercentEncoded()
	{
		var service = BuildService(HttpStatusCode.OK, PageDtoJson(slug: "hello world"), out var handler);

		await service.UpdatePageAsync("hello world", "# X", null);

		// Use AbsoluteUri (percent-encoded form) — Uri.ToString() returns the unescaped form.
		var uri = handler!.CapturedRequest!.RequestUri!.AbsoluteUri;
		await Assert.That(uri).Contains("hello%20world");
		await Assert.That(uri).DoesNotContain("hello world");
	}

	[Test]
	public async Task UpdatePageAsync_SendsCorrectJsonBody()
	{
		var service = BuildService(HttpStatusCode.OK, PageDtoJson(), out var handler);

		await service.UpdatePageAsync("home", "# Hello", "my summary");

		await Assert.That(handler!.CapturedRequestBody).IsNotNull();

		using var doc = JsonDocument.Parse(handler.CapturedRequestBody!);
		var root = doc.RootElement;
		await Assert.That(root.GetProperty("markdown").GetString()).IsEqualTo("# Hello");
		await Assert.That(root.GetProperty("editSummary").GetString()).IsEqualTo("my summary");
	}

	[Test]
	public async Task UpdatePageAsync_SendsApplicationJsonContentType()
	{
		var service = BuildService(HttpStatusCode.OK, PageDtoJson(), out var handler);

		await service.UpdatePageAsync("home", "# Home", null);

		var contentType = handler!.CapturedRequest!.Content!.Headers.ContentType!.MediaType;
		await Assert.That(contentType).IsEqualTo("application/json");
	}

	// ── GetWikiArticle ─────────────────────────────────────────────────────

	[Test]
	public async Task GetWikiArticle_200_ReturnsMappedArticle()
	{
		var service = BuildService(HttpStatusCode.OK, PageDtoJson(slug: "home", title: "Home"), out _);

		var result = await service.GetWikiArticle("home");

		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(result.AsT0.Title).IsEqualTo("Home");
		await Assert.That(result.AsT0.Slug).IsEqualTo("home");
	}

	[Test]
	public async Task GetWikiArticle_404_ReturnsNone()
	{
		var service = BuildService(HttpStatusCode.NotFound, "", out _);

		var result = await service.GetWikiArticle("missing");

		await Assert.That(result.IsT1).IsTrue();
	}

	[Test]
	public async Task GetWikiArticle_SendsGetToCorrectUrl()
	{
		var service = BuildService(HttpStatusCode.OK, PageDtoJson(), out var handler);

		await service.GetWikiArticle("home");

		await Assert.That(handler!.CapturedRequest!.Method).IsEqualTo(HttpMethod.Get);
		await Assert.That(handler.CapturedRequest.RequestUri!.ToString())
			.Contains("api/wiki/home");
	}

	// ── CreatePageAsync ────────────────────────────────────────────────────

	[Test]
	public async Task CreatePageAsync_201_ReturnsMappedArticle()
	{
		var service = BuildService(HttpStatusCode.Created, PageDtoJson(slug: "my-page", title: "My Page"), out _);

		var result = await service.CreatePageAsync("My Page", "# My Page");

		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(result.AsT0.Title).IsEqualTo("My Page");
		await Assert.That(result.AsT0.Slug).IsEqualTo("my-page");
	}

	[Test]
	public async Task CreatePageAsync_409_ReturnsErrorString()
	{
		var service = BuildService(HttpStatusCode.Conflict, """{"error":"slug already exists"}""", out _);

		var result = await service.CreatePageAsync("Duplicate", "# Dup");

		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(result.AsT1).Contains("409");
	}

	[Test]
	public async Task CreatePageAsync_SendsPostToCorrectUrl()
	{
		var service = BuildService(HttpStatusCode.Created, PageDtoJson(), out var handler);

		await service.CreatePageAsync("Test", "# Test");

		await Assert.That(handler!.CapturedRequest!.Method).IsEqualTo(HttpMethod.Post);
		await Assert.That(handler.CapturedRequest.RequestUri!.ToString())
			.EndsWith("api/wiki");
	}

	[Test]
	public async Task CreatePageAsync_SendsCorrectJsonBody()
	{
		var service = BuildService(HttpStatusCode.Created, PageDtoJson(), out var handler);

		await service.CreatePageAsync("My Title", "# Content", "Character");

		await Assert.That(handler!.CapturedRequestBody).IsNotNull();

		using var doc = JsonDocument.Parse(handler.CapturedRequestBody!);
		var root = doc.RootElement;
		await Assert.That(root.GetProperty("title").GetString()).IsEqualTo("My Title");
		await Assert.That(root.GetProperty("markdown").GetString()).IsEqualTo("# Content");
		await Assert.That(root.GetProperty("namespace").GetString()).IsEqualTo("Character");
	}
}
