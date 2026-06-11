using SharpMUSH.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.Integration.Wiki;

/// <summary>
/// HTTP integration tests for revision rollback (POST /api/wiki/{slug}/rollback)
/// and the batch page-existence check (POST /api/wiki/exists) that powers
/// client-side redlink rendering.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class WikiRollbackAndRedlinkApiTests(ServerWebAppFactory factory)
{
	private record WikiPageDto(string Id, string Slug, string Title, string MarkdownSource, int RevisionNumber);
	private record WikiRevisionDto(int RevisionNumber, string EditorDbref, DateTimeOffset Timestamp, string? EditSummary, string MarkdownSource);
	private record CreatePageRequest(string Title, string Markdown, string? Namespace);
	private record UpdatePageRequest(string Markdown, string? EditSummary);
	private record RollbackRequest(int RevisionNumber);
	private record ExistsRequest(string[] Refs);

	/// <summary>https base address — UseHttpsRedirection drops auth headers on the
	/// http→https redirect (see AuthHttpControllerTests).</summary>
	private HttpClient CreateClient()
	{
		var http = factory.CreateHttpClient();
		http.BaseAddress = new Uri("https://localhost/");
		return http;
	}

	private async Task<WikiPageDto> CreatePageAsync(HttpClient http, string title, string markdown)
	{
		var response = await http.PostAsJsonAsync("api/wiki", new CreatePageRequest(title, markdown, null));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
		return (await response.Content.ReadFromJsonAsync<WikiPageDto>())!;
	}

	// ── Rollback ──────────────────────────────────────────────────────────────

	[Test]
	public async Task Rollback_RestoresOldContent_AsNewRevision()
	{
		var http = CreateClient();
		var page = await CreatePageAsync(http, $"Rollback {Guid.NewGuid():N}", "# original");

		await http.PutAsJsonAsync($"api/wiki/{page.Slug}", new UpdatePageRequest("# changed", null));

		var rollback = await http.PostAsJsonAsync(
			$"api/wiki/{page.Slug}/rollback", new RollbackRequest(1));
		await Assert.That(rollback.StatusCode).IsEqualTo(HttpStatusCode.OK);

		var restored = await rollback.Content.ReadFromJsonAsync<WikiPageDto>();
		await Assert.That(restored!.MarkdownSource).IsEqualTo("# original");
		// History is preserved: the rollback is revision 3, not a rewrite.
		await Assert.That(restored.RevisionNumber).IsEqualTo(3);

		var revisions = await http.GetFromJsonAsync<List<WikiRevisionDto>>(
			$"api/wiki/{page.Slug}/revisions");
		await Assert.That(revisions!.Count).IsEqualTo(3);
		await Assert.That(revisions[0].EditSummary).IsEqualTo("rollback to r1");
	}

	[Test]
	public async Task Rollback_UnknownRevision_Returns404()
	{
		var http = CreateClient();
		var page = await CreatePageAsync(http, $"RollbackMissing {Guid.NewGuid():N}", "# body");

		var response = await http.PostAsJsonAsync(
			$"api/wiki/{page.Slug}/rollback", new RollbackRequest(999));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task Rollback_UnknownPage_Returns404()
	{
		var http = CreateClient();

		var response = await http.PostAsJsonAsync(
			"api/wiki/does-not-exist-xyzzy/rollback", new RollbackRequest(1));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	// ── Existence check (redlinks) ────────────────────────────────────────────

	[Test]
	public async Task Exists_MixedRefs_ReportsEachCorrectly()
	{
		var http = CreateClient();
		var page = await CreatePageAsync(http, $"Exists {Guid.NewGuid():N}", "# here");

		var response = await http.PostAsJsonAsync("api/wiki/exists",
			new ExistsRequest([page.Slug, "definitely_missing_page", "help/markdown_guide", "help/missing_help"]));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

		var map = await response.Content.ReadFromJsonAsync<Dictionary<string, bool>>();
		await Assert.That(map![page.Slug]).IsTrue();
		await Assert.That(map["definitely_missing_page"]).IsFalse();
		await Assert.That(map["help/markdown_guide"]).IsTrue(); // seeded guide
		await Assert.That(map["help/missing_help"]).IsFalse();
	}

	[Test]
	public async Task Exists_EmptyRefs_ReturnsEmptyMap()
	{
		var http = CreateClient();

		var response = await http.PostAsJsonAsync("api/wiki/exists", new ExistsRequest([]));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var map = await response.Content.ReadFromJsonAsync<Dictionary<string, bool>>();
		await Assert.That(map!.Count).IsEqualTo(0);
	}
}
