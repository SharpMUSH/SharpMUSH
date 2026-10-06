using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// <c>GET api/wiki/categories</c> lists every category with its name and page count, and filing a page in a
/// category nobody has named yet gives it a category page titled as typed, so its capitals are not lost.
/// </summary>
public class WikiCategoriesControllerTests
{
	private static async Task<List<WikiCategorySummaryDto>> CategoriesAsync(WikiEndpoints wiki)
	{
		var ok = (OkObjectResult)await wiki.Browse.GetCategories();
		return ((IEnumerable<WikiCategorySummaryDto>)ok.Value!).ToList();
	}

	[Test]
	public async Task Categories_carry_their_name_count_and_whether_they_have_a_page()
	{
		var storage = InMemoryWikiStore.CreateService();
		await storage.CreateAsync("Dragons", "x", "#1", categories: ["Lore", "Beasts"]);
		await storage.CreateAsync("Wyverns", "x", "#1", categories: ["Beasts"]);
		await storage.CreateAsync("Lore of the Deep", "Stories.", "#1", WikiNamespace.Category);
		var secret = (await storage.CreateAsync("Plot", "x", "#1", categories: ["Secrets"])).Expect<WikiPage>();
		await storage.SetMetadataAsync(secret.Id, secret.Categories, published: false);

		var categories = await CategoriesAsync(WikiControllerTestHarness.Build(storage, authenticated: false, "#42").Wiki);

		await Assert.That(categories).IsEquivalentTo(new[]
		{
			new WikiCategorySummaryDto("beasts", "Beasts", 2, HasPage: false),
			new WikiCategorySummaryDto("lore", "Lore", 1, HasPage: false),
			new WikiCategorySummaryDto("lore_of_the_deep", "Lore of the Deep", 0, HasPage: true),
		}).Because("a draft the caller may not see is not counted, so its category is not listed");
	}

	[Test]
	public async Task Filing_a_page_in_a_new_category_titles_its_page_as_typed()
	{
		var (wiki, storage) = WikiControllerTestHarness.BuildWithClaims(PortalPermission.WikiEdit, PortalPermission.WikiCreate);
		var page = (await storage.CreateAsync("Harbour", "x", "#42", categories: ["lore"])).Expect<WikiPage>();

		var result = await wiki.Admin.SetMetadata(page.Slug, new SetMetadataRequest(["lore", "Magic Items", "places"], true));

		await Assert.That(result).IsTypeOf<OkObjectResult>();
		await Assert.That((await storage.GetBySlugAsync("magic_items", WikiNamespace.Category)).Expect<WikiPage>().Title)
			.IsEqualTo("Magic Items");
		await Assert.That(await storage.GetBySlugAsync("places", WikiNamespace.Category) is NotFound).IsTrue()
			.Because("a name typed as its own key adds nothing its key does not already say");
		await Assert.That(await storage.GetBySlugAsync("lore", WikiNamespace.Category) is NotFound).IsTrue()
			.Because("the page already held lore");
	}

	[Test]
	public async Task A_caller_who_may_not_create_pages_files_the_page_without_naming_the_category()
	{
		var (wiki, storage) = WikiControllerTestHarness.BuildWithClaims(PortalPermission.WikiEdit);
		var page = (await storage.CreateAsync("Harbour", "x", "#42")).Expect<WikiPage>();

		await wiki.Admin.SetMetadata(page.Slug, new SetMetadataRequest(["Magic Items"], true));

		await Assert.That((await storage.GetBySlugAsync("harbour")).Expect<WikiPage>().Categories).IsEquivalentTo(new[] { "magic_items" });
		await Assert.That(await storage.GetBySlugAsync("magic_items", WikiNamespace.Category) is NotFound).IsTrue();
	}

	[Test]
	public async Task A_draft_category_page_the_caller_may_see_names_its_category()
	{
		var storage = InMemoryWikiStore.CreateService();
		await storage.CreateAsync("Harbour", "x", "#1", categories: ["Magic Items"]);
		var draft = (await storage.CreateAsync("Magic Items", "x", "#1", WikiNamespace.Category)).Expect<WikiPage>();
		await storage.SetMetadataAsync(draft.Id, draft.Categories, published: false);

		var categories = await CategoriesAsync(WikiControllerTestHarness.Build(storage, authenticated: true, "#42", PortalPermission.WikiDrafts).Wiki);

		await Assert.That(categories).IsEquivalentTo(new[] { new WikiCategorySummaryDto("magic_items", "Magic Items", 1, HasPage: true) })
			.Because("a caller who sees drafts sees the draft category page, so the category is not offered as new");
	}

	[Test]
	public async Task A_session_acting_as_no_character_files_the_page_without_naming_the_category()
	{
		var storage = InMemoryWikiStore.CreateService();
		var page = (await storage.CreateAsync("Harbour", "x", "#1")).Expect<WikiPage>();
		var wiki = WikiControllerTestHarness.Build(storage, authenticated: true, "", PortalPermission.WikiEdit, PortalPermission.WikiCreate).Wiki;

		await wiki.Admin.SetMetadata(page.Slug, new SetMetadataRequest(["Magic Items"], true));

		await Assert.That((await storage.GetBySlugAsync("harbour")).Expect<WikiPage>().Categories).IsEquivalentTo(new[] { "magic_items" });
		await Assert.That(await storage.GetBySlugAsync("magic_items", WikiNamespace.Category) is NotFound).IsTrue()
			.Because("a category page needs an author");
	}

	[Test]
	public async Task Creating_a_page_in_a_new_category_titles_its_page_as_typed()
	{
		var (wiki, storage) = WikiControllerTestHarness.BuildWithClaims(PortalPermission.WikiCreate);

		await wiki.Pages.CreatePage(new CreatePageRequest("Tide Tables", "x", "main", ["Sea Lore"]));

		await Assert.That((await storage.GetBySlugAsync("sea_lore", WikiNamespace.Category)).Expect<WikiPage>().Title)
			.IsEqualTo("Sea Lore");
	}
}
