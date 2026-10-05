using SharpMUSH.Tests.Infrastructure;
using System.Net;
using System.Net.Http.Json;

namespace SharpMUSH.Tests.Integration.Wiki;

/// <summary>
/// HTTP-level integration tests for the wiki admin endpoints on <c>WikiController</c>:
/// the paginated /pages listing (with X-Total-Count), page metadata (categories and the published
/// flag), category listings, and the batch protect/delete operations.
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
	private record WikiPageDto(
		string Id,
		string Slug,
		string Title,
		string Namespace,
		string MarkdownSource,
		bool IsRestricted,
		int RevisionNumber,
		List<string>? Categories,
		bool Published);

	private record CreatePageRequest(string Title, string Markdown, string? Namespace, string[]? Categories = null);
	private record SetMetadataRequest(string[]? Categories, bool Published);
	private record UpdatePageRequest(string Markdown, string? EditSummary);
	private record BatchProtectRequest(string[] Refs, bool IsProtected);
	private record BatchDeleteRequest(string[] Refs);
	private record BatchResult(List<string> Succeeded, List<string> Failed);

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

	private async Task<WikiPageDto> CreatePageAsync(
		HttpClient http, string titlePrefix, string markdown = "# admin api test page", string[]? categories = null)
	{
		var title = $"{titlePrefix} {Guid.NewGuid():N}";
		var response = await http.PostAsJsonAsync(
			"api/wiki",
			new CreatePageRequest(title, markdown, null, categories));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
		return (await response.Content.ReadFromJsonAsync<WikiPageDto>())!;
	}

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

	[Test]
	public async Task SetMetadata_RoundTripsCategoriesAndPublished()
	{
		var http = CreateClient();
		var created = await CreatePageAsync(http, "AdminPub");

		var put = await http.PutAsJsonAsync(
			$"api/wiki/{Uri.EscapeDataString(created.Slug)}/metadata",
			new SetMetadataRequest(["Lore", "Places of Note", "lore"], false));

		await Assert.That(put.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var updated = await put.Content.ReadFromJsonAsync<WikiPageDto>();
		await Assert.That(updated).IsNotNull();
		await Assert.That(updated!.Published).IsFalse();
		await Assert.That(updated.Categories!).IsEquivalentTo(["lore", "places_of_note"]);
		await Assert.That(updated.RevisionNumber).IsEqualTo(created.RevisionNumber);

		var fetched = await http.GetFromJsonAsync<WikiPageDto>(
			$"api/wiki/ns/main/{Uri.EscapeDataString(created.Slug)}");
		await Assert.That(fetched!.Published).IsFalse();
		await Assert.That(fetched.Categories!).IsEquivalentTo(["lore", "places_of_note"]);
	}

	[Test]
	public async Task SetMetadata_UnknownSlug_Returns404()
	{
		var http = CreateClient();

		var put = await http.PutAsJsonAsync(
			"api/wiki/does-not-exist-xyzzy/metadata",
			new SetMetadataRequest(null, true));

		await Assert.That(put.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task CreatePage_StoresTheCategoriesItIsGiven()
	{
		var http = CreateClient();
		var created = await CreatePageAsync(http, "AdminCats",
			"Dragons live here. See [[Category:Myth]].", ["Lore", "Dragons", "lore"]);

		await Assert.That(created.Categories).IsNotNull();
		await Assert.That(created.Categories!).IsEquivalentTo(["dragons", "lore"]);

		var fetched = await http.GetFromJsonAsync<WikiPageDto>(
			$"api/wiki/ns/main/{Uri.EscapeDataString(created.Slug)}");
		await Assert.That(fetched!.Categories!).IsEquivalentTo(["dragons", "lore"]);
	}

	[Test]
	public async Task ListCategoryPages_ReturnsPagesInCategory()
	{
		var http = CreateClient();
		var category = $"cat{Guid.NewGuid():N}"[..12];
		var created = await CreatePageAsync(http, "AdminCat", "In a category.", [category]);

		var pages = await http.GetFromJsonAsync<List<WikiPageDto>>(
			$"api/wiki/category/{Uri.EscapeDataString(category)}");

		await Assert.That(pages).IsNotNull();
		await Assert.That(pages!.Any(p => p.Slug == created.Slug)).IsTrue();
	}

	[Test]
	public async Task CategoriesSurviveABodyEditAndLeaveOnlyThroughMetadata()
	{
		var http = CreateClient();
		var category = $"cat{Guid.NewGuid():N}"[..12];
		var created = await CreatePageAsync(http, "AdminUncat", "In a category.", [category]);
		var listing = $"api/wiki/category/{Uri.EscapeDataString(category)}";

		var put = await http.PutAsJsonAsync(
			$"api/wiki/{Uri.EscapeDataString(created.Slug)}",
			new UpdatePageRequest("New text.", null));
		await Assert.That(put.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That((await http.GetFromJsonAsync<List<WikiPageDto>>(listing))!.Any(p => p.Slug == created.Slug)).IsTrue();

		var cleared = await http.PutAsJsonAsync(
			$"api/wiki/{Uri.EscapeDataString(created.Slug)}/metadata",
			new SetMetadataRequest([], true));
		await Assert.That(cleared.StatusCode).IsEqualTo(HttpStatusCode.OK);
		await Assert.That((await http.GetFromJsonAsync<List<WikiPageDto>>(listing))!.Any(p => p.Slug == created.Slug)).IsFalse();
	}

	[Test]
	public async Task BatchProtect_ProtectsAllRequestedPages()
	{
		var http = CreateClient();
		var first = await CreatePageAsync(http, "AdminProt1");
		var second = await CreatePageAsync(http, "AdminProt2");

		var response = await http.PostAsJsonAsync(
			"api/wiki/batch/protect",
			new BatchProtectRequest(
				[$"main/{first.Slug}", $"main/{second.Slug}", "main/does-not-exist-xyzzy"], true));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var result = await response.Content.ReadFromJsonAsync<BatchResult>();
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Succeeded.Count).IsEqualTo(2);
		await Assert.That(result.Failed).Contains("main/does-not-exist-xyzzy");

		var fetched = await http.GetFromJsonAsync<WikiPageDto>(
			$"api/wiki/ns/main/{Uri.EscapeDataString(first.Slug)}");
		await Assert.That(fetched!.IsRestricted).IsTrue();
	}

	[Test]
	public async Task BatchDelete_DeletesAllRequestedPages()
	{
		var http = CreateClient();
		var first = await CreatePageAsync(http, "AdminDel1");
		var second = await CreatePageAsync(http, "AdminDel2");

		var response = await http.PostAsJsonAsync(
			"api/wiki/batch/delete",
			new BatchDeleteRequest(
				[$"main/{first.Slug}", $"main/{second.Slug}", "main/does-not-exist-xyzzy"]));

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var result = await response.Content.ReadFromJsonAsync<BatchResult>();
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Succeeded.Count).IsEqualTo(2);
		await Assert.That(result.Failed.Count).IsEqualTo(1);

		var gone = await http.GetAsync($"api/wiki/ns/main/{Uri.EscapeDataString(first.Slug)}");
		await Assert.That(gone.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}
}
