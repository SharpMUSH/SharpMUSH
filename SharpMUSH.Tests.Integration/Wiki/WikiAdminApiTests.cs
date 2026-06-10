using SharpMUSH.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.Integration.Wiki;

/// <summary>
/// HTTP-level integration tests for the wiki admin endpoints on <c>WikiController</c>:
/// the paginated /pages listing (with X-Total-Count), metadata round-trip,
/// category/tag listings, and the batch protect/delete operations.
///
/// DebugAuthenticationHandler auto-authenticates all requests as the bootstrap admin,
/// so <c>[Authorize]</c> endpoints work out-of-the-box in the Development environment.
///
/// NOTE: Do NOT implement IAsyncInitializer here — TUnit's ClassDataSource calls
/// ServerWebAppFactory.InitializeAsync() exactly once for the session.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class WikiAdminApiTests(ServerWebAppFactory factory)
{
	// DTO mirrors WikiController.WikiPageDto — only the fields the tests care about.
	private record WikiPageDto(
		string Id,
		string Slug,
		string Title,
		string Namespace,
		string MarkdownSource,
		bool IsProtected,
		int RevisionNumber,
		string? Category,
		List<string>? Tags,
		bool Published);

	private record CreatePageRequest(string Title, string Markdown, string? Namespace);
	private record SetMetadataRequest(string? Category, string[] Tags, bool Published);
	private record BatchProtectRequest(string[] Slugs, string? Ns, bool IsProtected);
	private record BatchDeleteRequest(string[] Slugs, string? Ns);
	private record BatchResult(List<string> Succeeded, List<string> Failed);

	// ── Helpers ───────────────────────────────────────────────────────────────

	/// <summary>
	/// Test client pinned to the https base address. The server uses UseHttpsRedirection;
	/// following the 307 from http→https makes HttpClient drop the Authorization header,
	/// which breaks bearer-authenticated endpoints.
	/// </summary>
	private HttpClient CreateClient()
	{
		var http = factory.CreateHttpClient();
		http.BaseAddress = new Uri("https://localhost/");
		return http;
	}

	private async Task<WikiPageDto> CreatePageAsync(HttpClient http, string titlePrefix)
	{
		var title = $"{titlePrefix} {Guid.NewGuid():N}";
		var response = await http.PostAsJsonAsync(
			"api/wiki",
			new CreatePageRequest(title, "# admin api test page", null));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
		return (await response.Content.ReadFromJsonAsync<WikiPageDto>())!;
	}

	// ── /pages listing ────────────────────────────────────────────────────────

	[Test]
	public async Task ListAllPages_ReturnsPagesAndTotalCountHeader()
	{
		var http = CreateClient();
		var created = await CreatePageAsync(http, "AdminList");

		var response = await http.GetAsync("api/wiki/pages?skip=0&take=500");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That(response.Headers.Contains("X-Total-Count")).IsTrue();

		var total = int.Parse(response.Headers.GetValues("X-Total-Count").First());
		await Assert.That(total).IsGreaterThanOrEqualTo(1);

		var pages = await response.Content.ReadFromJsonAsync<List<WikiPageDto>>();
		await Assert.That(pages).IsNotNull();
		await Assert.That(pages!.Any(p => p.Slug == created.Slug)).IsTrue();
	}

	[Test]
	public async Task ListAllPages_PaginationRespectsTake()
	{
		var http = CreateClient();
		await CreatePageAsync(http, "AdminPage1");
		await CreatePageAsync(http, "AdminPage2");

		var response = await http.GetAsync("api/wiki/pages?skip=0&take=1");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var pages = await response.Content.ReadFromJsonAsync<List<WikiPageDto>>();
		await Assert.That(pages!.Count).IsEqualTo(1);

		var total = int.Parse(response.Headers.GetValues("X-Total-Count").First());
		await Assert.That(total).IsGreaterThanOrEqualTo(2);
	}

	// ── Metadata round-trip ───────────────────────────────────────────────────

	[Test]
	public async Task SetMetadata_RoundTripsCategoryTagsAndPublished()
	{
		var http = CreateClient();
		var created = await CreatePageAsync(http, "AdminMeta");

		var put = await http.PutAsJsonAsync(
			$"api/wiki/{Uri.EscapeDataString(created.Slug)}/metadata",
			new SetMetadataRequest("Lore", ["Dragons", "magic", "dragons"], false));

		await Assert.That(put.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var updated = await put.Content.ReadFromJsonAsync<WikiPageDto>();
		await Assert.That(updated).IsNotNull();
		// Normalised to lower-case; tags de-duplicated.
		await Assert.That(updated!.Category).IsEqualTo("lore");
		await Assert.That(updated.Tags!.Count).IsEqualTo(2);
		await Assert.That(updated.Tags!.Contains("dragons")).IsTrue();
		await Assert.That(updated.Tags!.Contains("magic")).IsTrue();
		await Assert.That(updated.Published).IsFalse();

		// GET reflects the change (authenticated callers see unpublished pages).
		var fetched = await http.GetFromJsonAsync<WikiPageDto>(
			$"api/wiki/{Uri.EscapeDataString(created.Slug)}");
		await Assert.That(fetched!.Category).IsEqualTo("lore");
		await Assert.That(fetched.Published).IsFalse();
	}

	[Test]
	public async Task SetMetadata_UnknownSlug_Returns404()
	{
		var http = CreateClient();

		var put = await http.PutAsJsonAsync(
			"api/wiki/does-not-exist-xyzzy/metadata",
			new SetMetadataRequest("lore", [], true));

		await Assert.That(put.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	// ── Category / tag listings ───────────────────────────────────────────────

	[Test]
	public async Task ListCategoryPages_ReturnsPagesInCategory()
	{
		var http = CreateClient();
		var created = await CreatePageAsync(http, "AdminCat");
		var category = $"cat{Guid.NewGuid():N}"[..12];

		await http.PutAsJsonAsync(
			$"api/wiki/{Uri.EscapeDataString(created.Slug)}/metadata",
			new SetMetadataRequest(category, [], true));

		var pages = await http.GetFromJsonAsync<List<WikiPageDto>>(
			$"api/wiki/category/{Uri.EscapeDataString(category)}");

		await Assert.That(pages).IsNotNull();
		await Assert.That(pages!.Any(p => p.Slug == created.Slug)).IsTrue();
	}

	[Test]
	public async Task ListTagPages_ReturnsPagesWithTag()
	{
		var http = CreateClient();
		var created = await CreatePageAsync(http, "AdminTag");
		var tag = $"tag{Guid.NewGuid():N}"[..12];

		await http.PutAsJsonAsync(
			$"api/wiki/{Uri.EscapeDataString(created.Slug)}/metadata",
			new SetMetadataRequest(null, [tag], true));

		var pages = await http.GetFromJsonAsync<List<WikiPageDto>>(
			$"api/wiki/tag/{Uri.EscapeDataString(tag)}");

		await Assert.That(pages).IsNotNull();
		await Assert.That(pages!.Any(p => p.Slug == created.Slug)).IsTrue();
	}

	// ── Batch operations ──────────────────────────────────────────────────────

	[Test]
	public async Task BatchProtect_ProtectsAllRequestedPages()
	{
		var http = CreateClient();
		var first = await CreatePageAsync(http, "AdminProt1");
		var second = await CreatePageAsync(http, "AdminProt2");

		var response = await http.PostAsJsonAsync(
			"api/wiki/batch/protect",
			new BatchProtectRequest([first.Slug, second.Slug, "does-not-exist-xyzzy"], null, true));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var result = await response.Content.ReadFromJsonAsync<BatchResult>();
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Succeeded.Count).IsEqualTo(2);
		await Assert.That(result.Failed).Contains("does-not-exist-xyzzy");

		var fetched = await http.GetFromJsonAsync<WikiPageDto>(
			$"api/wiki/{Uri.EscapeDataString(first.Slug)}");
		await Assert.That(fetched!.IsProtected).IsTrue();
	}

	[Test]
	public async Task BatchDelete_DeletesAllRequestedPages()
	{
		var http = CreateClient();
		var first = await CreatePageAsync(http, "AdminDel1");
		var second = await CreatePageAsync(http, "AdminDel2");

		var response = await http.PostAsJsonAsync(
			"api/wiki/batch/delete",
			new BatchDeleteRequest([first.Slug, second.Slug, "does-not-exist-xyzzy"], null));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var result = await response.Content.ReadFromJsonAsync<BatchResult>();
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Succeeded.Count).IsEqualTo(2);
		await Assert.That(result.Failed.Count).IsEqualTo(1);

		var gone = await http.GetAsync($"api/wiki/{Uri.EscapeDataString(first.Slug)}");
		await Assert.That(gone.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}
}
