using SharpMUSH.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.Integration.Wiki;

/// <summary>
/// HTTP-level integration tests for <c>WikiController</c>.
/// Uses the in-process <see cref="ServerWebAppFactory"/> test server so every request travels
/// through the full ASP.NET Core middleware pipeline (auth, routing, model binding,
/// controller logic, DB) without touching a real network socket.
///
/// DebugAuthenticationHandler auto-authenticates all requests as the bootstrap admin,
/// so <c>[Authorize]</c> endpoints work out-of-the-box in the Development environment.
///
/// NOTE: Do NOT implement IAsyncInitializer here — TUnit's ClassDataSource calls
/// ServerWebAppFactory.InitializeAsync() exactly once for the session. Calling it
/// again from a test class would double-init the host and crash with a duplicate-key
/// exception in Functions..ctor (static function library built at first startup).
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class WikiHttpControllerTests(ServerWebAppFactory factory)
{
	// DTO mirrors WikiController.WikiPageDto — only the fields the tests care about.
	private record WikiPageDto(
		string Id,
		string Slug,
		string Title,
		string Namespace,
		string MarkdownSource,
		string RenderedHtml,
		int RevisionNumber);

	private record CreatePageRequest(string Title, string Markdown, string? Namespace);
	private record UpdatePageRequest(string Markdown, string? EditSummary);

	// ── GET ──────────────────────────────────────────────────────────────────

	[Test]
	public async Task GetPage_HomeSlug_Returns200WithCorrectSlug()
	{
		var http = factory.CreateHttpClient();

		var response = await http.GetAsync("api/wiki/home");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var dto = await response.Content.ReadFromJsonAsync<WikiPageDto>();
		await Assert.That(dto).IsNotNull();
		await Assert.That(dto!.Slug).IsEqualTo("home");
	}

	[Test]
	public async Task GetPage_UnknownSlug_Returns404()
	{
		var http = factory.CreateHttpClient();

		var response = await http.GetAsync("api/wiki/does-not-exist-xyzzy");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	// ── PUT ──────────────────────────────────────────────────────────────────

	[Test]
	public async Task PutPage_HomeSlug_Returns200AndUpdatesContent()
	{
		var http = factory.CreateHttpClient();
		const string newMarkdown = "## Updated\n\nThis content was written by the HTTP integration test.";

		var response = await http.PutAsJsonAsync(
			"api/wiki/home",
			new UpdatePageRequest(newMarkdown, "integration-test edit"));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

		var dto = await response.Content.ReadFromJsonAsync<WikiPageDto>();
		await Assert.That(dto).IsNotNull();
		await Assert.That(dto!.MarkdownSource).IsEqualTo(newMarkdown);
		await Assert.That(dto.RevisionNumber).IsGreaterThan(0);
	}

	[Test]
	public async Task PutPage_UnknownSlug_Returns404()
	{
		var http = factory.CreateHttpClient();

		var response = await http.PutAsJsonAsync(
			"api/wiki/does-not-exist-xyzzy",
			new UpdatePageRequest("# Whatever", null));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	// ── POST ──────────────────────────────────────────────────────────────────

	[Test]
	public async Task PostPage_NewPage_Returns201AndLocationHeader()
	{
		var http = factory.CreateHttpClient();
		var title = $"Test Page {Guid.NewGuid():N}";

		var response = await http.PostAsJsonAsync(
			"api/wiki",
			new CreatePageRequest(title, "# Hello\n\nIntegration test page.", null));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
		await Assert.That(response.Headers.Location).IsNotNull();

		var dto = await response.Content.ReadFromJsonAsync<WikiPageDto>();
		await Assert.That(dto).IsNotNull();
		await Assert.That(dto!.Title).IsEqualTo(title);
	}

	[Test]
	public async Task PostPage_DuplicateSlug_Returns409Conflict()
	{
		var http = factory.CreateHttpClient();

		// Create a page first …
		var title = $"Conflict Test {Guid.NewGuid():N}";
		var first = await http.PostAsJsonAsync(
			"api/wiki",
			new CreatePageRequest(title, "first", null));
		await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.Created);

		// … then try to create it again with the same title (same derived slug).
		var second = await http.PostAsJsonAsync(
			"api/wiki",
			new CreatePageRequest(title, "second", null));
		await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
	}

	// ── Round-trip ────────────────────────────────────────────────────────────

	[Test]
	public async Task CreateThenUpdateThenGet_ContentRoundTrips()
	{
		var http = factory.CreateHttpClient();
		var title = $"RoundTrip {Guid.NewGuid():N}";
		const string updatedMarkdown = "## Round-trip\n\nFinal content.";

		// POST
		var created = await http.PostAsJsonAsync(
			"api/wiki",
			new CreatePageRequest(title, "# Initial", null));
		await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Created);
		var createdDto = await created.Content.ReadFromJsonAsync<WikiPageDto>();
		await Assert.That(createdDto).IsNotNull();

		// PUT
		var slug = createdDto!.Slug;
		var updated = await http.PutAsJsonAsync(
			$"api/wiki/{Uri.EscapeDataString(slug)}",
			new UpdatePageRequest(updatedMarkdown, "round-trip test"));
		await Assert.That(updated.StatusCode).IsEqualTo(HttpStatusCode.OK);

		// GET
		var fetched = await http.GetFromJsonAsync<WikiPageDto>($"api/wiki/{Uri.EscapeDataString(slug)}");
		await Assert.That(fetched).IsNotNull();
		await Assert.That(fetched!.MarkdownSource).IsEqualTo(updatedMarkdown);
		await Assert.That(fetched.RevisionNumber).IsGreaterThan(createdDto.RevisionNumber);
	}
}
