using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// Unit tests for <see cref="WikiStoreService"/>.
/// All tests run against a fresh service instance to ensure isolation.
/// Note: CreateAsync derives the slug from the title via Slugify(title).
/// </summary>
public class WikiStoreServiceTests
{
	private static IWikiService BuildService() =>
		InMemoryWikiStore.CreateService();

	/// <summary>
	/// Creates a page and asserts it succeeded, returning the <see cref="WikiPage"/>.
	/// </summary>
	private static async Task<WikiPage> CreatePageAsync(
		IWikiService svc,
		string title = "Test Page",
		WikiNamespace ns = WikiNamespace.Main,
		string markdown = "Hello **world**.",
		string editor = "#1")
	{
		var result = await svc.CreateAsync(title, markdown, editor, ns);
		var page = result.Expect<WikiPage>();
		return page;
	}

	[Test]
	public async Task CreateAsync_ReturnsPageWithAssignedId()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc);

		await Assert.That(page.Id).IsNotNull();
		await Assert.That(page.Id).IsNotEmpty();
	}

	[Test]
	public async Task CreateAsync_SlugDerivedFromTitle()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, title: "My Cool Page");

		await Assert.That(page.Slug).IsEqualTo("my_cool_page");
	}

	[Test]
	public async Task CreateAsync_SetsTitleAndNamespace()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, title: "Help Intro", ns: WikiNamespace.Help);

		await Assert.That(page.Title).IsEqualTo("Help Intro");
		await Assert.That(page.Namespace).IsEqualTo("help");
	}

	[Test]
	public async Task CreateAsync_StoresMarkdownSourceAndRenderedHtml()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, markdown: "Hello **world**.");

		await Assert.That(page.MarkdownSource).IsEqualTo("Hello **world**.");
		await Assert.That(page.RenderedHtml).IsNotNull();
		await Assert.That(page.RenderedHtml).Contains("<strong>world</strong>");
	}

	[Test]
	public async Task CreateAsync_SetsRevisionNumberToOne()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc);

		await Assert.That(page.RevisionNumber).IsEqualTo(1);
	}

	[Test]
	public async Task CreateAsync_SetsAuthorDbref()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, editor: "#42");

		await Assert.That(page.AuthorDbref).IsEqualTo("#42");
		await Assert.That(page.LastEditorDbref).IsEqualTo("#42");
	}

	[Test]
	public async Task CreateAsync_SameTitleSameNamespace_ReturnsError()
	{
		var svc = BuildService();
		await CreatePageAsync(svc, title: "Duplicate");

		var result = await svc.CreateAsync("Duplicate", "content", "#1", WikiNamespace.Main);

		await Assert.That(result.Value).IsTypeOf<Error<string>>();
	}

	[Test]
	public async Task CreateAsync_SameTitleDifferentNamespace_Succeeds()
	{
		var svc = BuildService();
		var main = await CreatePageAsync(svc, title: "Intro", ns: WikiNamespace.Main);
		var help = await CreatePageAsync(svc, title: "Intro", ns: WikiNamespace.Help);

		await Assert.That(main.Id).IsNotEqualTo(help.Id);
	}

	[Test]
	public async Task CreateAsync_SameSlugDifferentCategories_ReturnsError()
	{
		// Categories are tags in the text, not identity: one namespace holds one page per slug.
		var svc = BuildService();
		await svc.CreateAsync("Dragons", "content", "#1", WikiNamespace.Main, categories: ["Lore"]);

		var result = await svc.CreateAsync("Dragons", "content", "#1", WikiNamespace.Main, categories: ["Rules"]);

		await Assert.That(result.Value).IsTypeOf<Error<string>>();
	}

	[Test]
	public async Task SetMetadata_ChangingCategories_KeepsSlug()
	{
		var svc = BuildService();
		var page = (await svc.CreateAsync("Dragons", "content", "#1", categories: ["Lore"])).Expect<WikiPage>();

		await svc.SetMetadataAsync(page.Id, ["Rules"], published: true);

		var found = (await svc.GetBySlugAsync("dragons", WikiNamespace.Main)).Expect<WikiPage>();
		await Assert.That(found.Id).IsEqualTo(page.Id);
		await Assert.That(found.Categories).IsEquivalentTo(new[] { "rules" });
	}

	[Test]
	public async Task NameCategories_TitlesANewCategoryPageAsTyped()
	{
		var svc = BuildService();

		var created = await svc.NameCategoriesAsync(["  Places of Note ", "places of note", "Magic Items"], [], "#1", "en");

		await Assert.That(created.Select(p => p.Title)).IsEquivalentTo(new[] { "Places of Note", "Magic Items" });
		var page = (await svc.GetBySlugAsync("places_of_note", WikiNamespace.Category)).Expect<WikiPage>();
		await Assert.That(page.Title).IsEqualTo("Places of Note");
		await Assert.That(page.AuthorDbref).IsEqualTo("#1");
	}

	[Test]
	public async Task NameCategories_LeavesKeysFiledCategoriesAndExistingPagesAlone()
	{
		var svc = BuildService();
		await svc.CreateAsync("Lore", "Stories.", "#1", WikiNamespace.Category);

		// A key names nothing new; a category the page already held comes back as its key; Lore has its page.
		var created = await svc.NameCategoriesAsync(["places", "Rules", "LORE"], ["rules"], "#1", "en");

		await Assert.That(created).IsEmpty();
		await Assert.That(await svc.GetBySlugAsync("places", WikiNamespace.Category) is NotFound).IsTrue();
		await Assert.That(await svc.GetBySlugAsync("rules", WikiNamespace.Category) is NotFound).IsTrue();
		await Assert.That((await svc.GetBySlugAsync("lore", WikiNamespace.Category)).Expect<WikiPage>().Title).IsEqualTo("Lore");
	}

	[Test]
	public async Task CountPagesByCategory_CountsEachPageOncePerCategory()
	{
		var svc = BuildService();
		await svc.CreateAsync("Dragons", "x", "#1", categories: ["Lore", "lore", "Beasts"]);
		await svc.CreateAsync("Wyverns", "x", "#1", categories: ["Beasts"]);

		var counts = await svc.CountPagesByCategoryAsync(WikiVisibility.All);

		await Assert.That(counts["beasts"]).IsEqualTo(2);
		await Assert.That(counts["lore"]).IsEqualTo(1);
	}

	[Test]
	public async Task CreateAsync_CategoryPageLivesInCategoryNamespace()
	{
		var svc = BuildService();
		var page = (await svc.CreateAsync("Lore", "Stories of the world.", "#1", WikiNamespace.Category,
			categories: ["Setting"])).Expect<WikiPage>();

		var found = (await svc.GetBySlugAsync("lore", WikiNamespace.Category)).Expect<WikiPage>();
		await Assert.That(found.Id).IsEqualTo(page.Id);
		await Assert.That((await svc.GetByCategoryAsync("setting")).Single().Id).IsEqualTo(page.Id)
			.Because("a category page that names another category is that category's subcategory");
	}

	[Test]
	public async Task GetBySlugAsync_ExistingPage_ReturnsPage()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc, title: "Find Me");

		var result = await svc.GetBySlugAsync("find_me", WikiNamespace.Main);

		var page = result.Expect<WikiPage>();
		await Assert.That(page.Id).IsEqualTo(created.Id);
	}

	[Test]
	[Arguments("Mercutio")]
	[Arguments("MERCUTIO")]
	[Arguments("mercutio")]
	public async Task GetBySlugAsync_NormalizesSlugCase(string lookup)
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc, title: "Mercutio");

		var result = await svc.GetBySlugAsync(lookup, WikiNamespace.Main);

		var page = result.Expect<WikiPage>();
		await Assert.That(page.Id).IsEqualTo(created.Id);
	}

	[Test]
	[Arguments("Mannaz Byron")]
	[Arguments("mannaz_byron")]
	public async Task GetBySlugAsync_NormalizesSpacesToUnderscores(string lookup)
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc, title: "Mannaz Byron");

		var result = await svc.GetBySlugAsync(lookup, WikiNamespace.Main);

		var page = result.Expect<WikiPage>();
		await Assert.That(page.Id).IsEqualTo(created.Id);
	}

	[Test]
	public async Task GetBySlugAsync_MissingSlug_ReturnsNotFound()
	{
		var svc = BuildService();
		var result = await svc.GetBySlugAsync("nonexistent", WikiNamespace.Main);

		await Assert.That(result.Value).IsTypeOf<NotFound>();
	}

	[Test]
	public async Task GetBySlugAsync_WrongNamespace_ReturnsNotFound()
	{
		var svc = BuildService();
		await CreatePageAsync(svc, title: "Ns Test", ns: WikiNamespace.Main);

		var result = await svc.GetBySlugAsync("ns_test", WikiNamespace.Help);

		await Assert.That(result.Value).IsTypeOf<NotFound>();
	}

	[Test]
	public async Task GetByIdAsync_ExistingId_ReturnsPage()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc);

		var result = await svc.GetByIdAsync(created.Id);

		var page = result.Expect<WikiPage>();
		await Assert.That(page.Id).IsEqualTo(created.Id);
	}

	[Test]
	public async Task GetByIdAsync_MissingId_ReturnsNotFound()
	{
		var svc = BuildService();
		var result = await svc.GetByIdAsync("id_that_does_not_exist");

		await Assert.That(result.Value).IsTypeOf<NotFound>();
	}

	[Test]
	public async Task UpdateAsync_ChangesMarkdownAndBumpsRevision()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc, markdown: "Original content.");

		var updateResult = await svc.UpdateAsync(created.Id, "Updated content.", "#2", "v2");

		var updated = updateResult.Expect<WikiPage>();
		await Assert.That(updated.RevisionNumber).IsEqualTo(2);
		await Assert.That(updated.MarkdownSource).IsEqualTo("Updated content.");
		await Assert.That(updated.LastEditorDbref).IsEqualTo("#2");
	}

	[Test]
	public async Task UpdateAsync_RenderedHtmlReflectsNewContent()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc, markdown: "# Old");

		var updated = (await svc.UpdateAsync(created.Id, "# New Heading", "#1", "rework")).Expect<WikiPage>();
		await Assert.That(updated.RenderedHtml).Contains("New Heading");
	}

	[Test]
	public async Task UpdateAsync_MissingId_ReturnsNotFound()
	{
		var svc = BuildService();

		var result = await svc.UpdateAsync("ghost_id", "content", "#1");

		await Assert.That(result.Value).IsTypeOf<NotFound>();
	}

	[Test]
	public async Task DeleteAsync_ExistingPage_ReturnsNoneAndRemovesPage()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc, title: "To Delete");

		var deleteResult = await svc.DeleteAsync(created.Id, "#1");

		await Assert.That(deleteResult.Value).IsTypeOf<None>();
		var getResult = await svc.GetByIdAsync(created.Id);
		await Assert.That(getResult.Value).IsTypeOf<NotFound>();
	}

	[Test]
	public async Task DeleteAsync_SlugBecomesAvailableAfterDeletion()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc, title: "Reusable Slug");
		await svc.DeleteAsync(created.Id, "#1");

		// Should not return error — slug freed
		var recreated = await CreatePageAsync(svc, title: "Reusable Slug");
		await Assert.That(recreated.Slug).IsEqualTo("reusable_slug");
	}

	[Test]
	public async Task DeleteAsync_MissingId_ReturnsNotFound()
	{
		var svc = BuildService();
		var result = await svc.DeleteAsync("ghost_id", "#1");

		await Assert.That(result.Value).IsTypeOf<NotFound>();
	}

	[Test]
	public async Task GetRevisionsAsync_AfterCreate_HasOneRevision()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc);

		var revisions = await svc.GetRevisionsAsync(created.Id);

		await Assert.That(revisions.Count).IsEqualTo(1);
		await Assert.That(revisions[0].RevisionNumber).IsEqualTo(1);
	}

	[Test]
	public async Task GetRevisionsAsync_AfterTwoUpdates_HasThreeRevisions()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc, markdown: "v1");
		await svc.UpdateAsync(created.Id, "v2", "#1");
		await svc.UpdateAsync(created.Id, "v3", "#1");

		// GetRevisionsAsync returns newest first; take=20 covers all
		var revisions = await svc.GetRevisionsAsync(created.Id);

		await Assert.That(revisions.Count).IsEqualTo(3);
	}

	[Test]
	public async Task GetRevisionAsync_ValidRevisionNumber_ReturnsRevision()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc, markdown: "First edition.");

		var rev = (await svc.GetRevisionAsync(created.Id, 1)).Expect<WikiRevision>();
		await Assert.That(rev.RevisionNumber).IsEqualTo(1);
		await Assert.That(rev.MarkdownSource).IsEqualTo("First edition.");
	}

	[Test]
	public async Task GetRevisionAsync_MissingRevisionNumber_ReturnsNotFound()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc);

		var result = await svc.GetRevisionAsync(created.Id, 99);

		await Assert.That(result.Value).IsTypeOf<NotFound>();
	}

	[Test]
	public async Task ProtectAsync_SetsAPageRequirementAndUnprotectClearsIt()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc);
		await Assert.That(await svc.IsRestrictedAsync(created.Id)).IsFalse();

		var protResult = await svc.ProtectAsync(created.Id);

		await Assert.That(protResult.Value).IsTypeOf<None>();
		var set = (await svc.GetRequirementsAsync()).For(WikiRuleTarget.ForPage(created.Id));
		await Assert.That(set!.For(WikiAction.Edit)).IsEquivalentTo([PortalPermission.WikiAdmin]);
		await Assert.That(set.For(WikiAction.Delete)).IsEquivalentTo([PortalPermission.WikiAdmin]);

		await svc.ProtectAsync(created.Id, protect: false);
		await Assert.That(await svc.IsRestrictedAsync(created.Id)).IsFalse();
	}

	[Test]
	public async Task ProtectAsync_MissingId_ReturnsNotFound()
	{
		var svc = BuildService();

		var result = await svc.ProtectAsync("ghost_id");

		await Assert.That(result.Value).IsTypeOf<NotFound>();
	}

	/// <summary>The cached requirements follow a write and a page deletion at once.</summary>
	[Test]
	public async Task Requirements_FollowWritesAndDeletion()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc);
		var category = WikiRuleTarget.ForCategory("lore");

		await Assert.That((await svc.GetRequirementsAsync()).For(category)).IsNull();
		(await svc.SetRequirementsAsync(category,
			new Dictionary<WikiAction, IReadOnlyList<string>> { [WikiAction.Edit] = [" Lore.Edit ", "lore.edit"] }, "#1")).Expect<None>();
		await Assert.That((await svc.GetRequirementsAsync()).For(category)!.For(WikiAction.Edit)).IsEquivalentTo(["lore.edit"]);

		await svc.ProtectAsync(created.Id);
		await svc.DeleteAsync(created.Id, "#1");
		await Assert.That((await svc.GetRequirementsAsync()).HasPageRules(created.Id)).IsFalse();
	}

	[Test]
	public async Task GetByNamespaceAsync_ReturnsOnlyPagesInNamespace()
	{
		var svc = BuildService();
		await CreatePageAsync(svc, title: "Main One", ns: WikiNamespace.Main);
		await CreatePageAsync(svc, title: "Main Two", ns: WikiNamespace.Main);
		await CreatePageAsync(svc, title: "Help One", ns: WikiNamespace.Help);

		var mainPages = await svc.GetByNamespaceAsync(WikiNamespace.Main);
		var helpPages = await svc.GetByNamespaceAsync(WikiNamespace.Help);

		await Assert.That(mainPages.Count).IsEqualTo(2);
		await Assert.That(helpPages.Count).IsEqualTo(1);
	}

	[Test]
	public async Task GetRecentChangesAsync_ReturnsNewestFirst()
	{
		var svc = BuildService();
		await CreatePageAsync(svc, title: "Page A");
		await Task.Delay(5); // ensure different timestamps
		await CreatePageAsync(svc, title: "Page B");

		var recent = await svc.GetRecentChangesAsync(count: 10);

		await Assert.That(recent.Count).IsEqualTo(2);
		await Assert.That(recent[0].Title).IsEqualTo("Page B");
		await Assert.That(recent[1].Title).IsEqualTo("Page A");
	}

	[Test]
	public async Task GetRecentChangesAsync_RespectsCount()
	{
		var svc = BuildService();
		for (var i = 0; i < 5; i++)
			await CreatePageAsync(svc, title: $"Page {i}");

		var recent = await svc.GetRecentChangesAsync(count: 3);

		await Assert.That(recent.Count).IsEqualTo(3);
	}

	/// <summary>
	/// After editing a page via <see cref="IWikiService.UpdateAsync"/>, the next
	/// call to <see cref="IWikiService.GetBySlugAsync"/> must return HTML that
	/// reflects the new content, not the old render.
	/// </summary>
	[Test]
	public async Task WikiCache_EditPage_NextReadGetsFreshRender()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc, markdown: "**original**");

		var before = (await svc.GetBySlugAsync(created.Slug)).Expect<WikiPage>();
		await Assert.That(before.RenderedHtml).Contains("original");

		var updateResult = await svc.UpdateAsync(created.Id, "**updated**", "#1", "edit");
		await Assert.That(updateResult.Value).IsTypeOf<WikiPage>();

		var after = (await svc.GetBySlugAsync(created.Slug)).Expect<WikiPage>();
		await Assert.That(after.RenderedHtml).Contains("updated");
		await Assert.That(after.RenderedHtml).DoesNotContain("original");
	}

	// ---- Translations -------------------------------------------------------

	[Test]
	public async Task UpsertTranslationAsync_CreatesTheFirstRevisionOfATranslation()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Dragons");

		var result = await svc.UpsertTranslationAsync(
			page.Id, "fr", "Dragons (fr)", "corps **fr**", "#2", "première traduction",
			published: true, expectedRevisionNumber: null);

		var translation = result.Expect<WikiTranslation>();
		await Assert.That(translation.Locale).IsEqualTo("fr");
		await Assert.That(translation.Title).IsEqualTo("Dragons (fr)");
		await Assert.That(translation.RevisionNumber).IsEqualTo(1);
		await Assert.That(translation.RenderedHtml).Contains("<strong>fr</strong>");
		await Assert.That(translation.PlainText).Contains("corps");
		await Assert.That(translation.Published).IsTrue();
	}

	[Test]
	public async Task GetAllTranslationsAsync_ReturnsEveryLocaleWithItsBody_OrderedAndPaged()
	{
		var svc = BuildService();
		var first = await CreatePageAsync(svc, "Alpha");
		var second = await CreatePageAsync(svc, "Beta");
		foreach (var (page, locale) in new[] { (first, "fr"), (first, "de"), (second, "fr") })
		{
			var written = await svc.UpsertTranslationAsync(
				page.Id, locale, $"{page.Title} ({locale})", $"corps {page.Slug} {locale}", "#2", null,
				published: true, expectedRevisionNumber: null);
			await Assert.That(written.Value).IsTypeOf<WikiTranslation>();
		}

		var all = await svc.GetAllTranslationsAsync(0, 50);

		await Assert.That(all.Count).IsEqualTo(3);
		await Assert.That(all.Select(t => $"{t.PageId}/{t.Locale}").ToList())
			.IsEquivalentTo(new[] { $"{first.Id}/de", $"{first.Id}/fr", $"{second.Id}/fr" })
			.Because("the order is (page, locale) so paging through the stream is stable");
		await Assert.That(all.All(t => t.PlainText.Contains("corps")))
			.IsTrue()
			.Because("search matches on bodies, so unlike GetTranslationsAsync this cannot be bodyless");

		var window = await svc.GetAllTranslationsAsync(skip: 1, take: 1);
		await Assert.That(window.Count).IsEqualTo(1);
		await Assert.That(window[0].Locale).IsEqualTo("fr");
		await Assert.That(window[0].PageId).IsEqualTo(first.Id);
	}

	[Test]
	public async Task GetAllTranslationsAsync_IncludesUnpublishedDrafts()
	{
		// The contract says drafts are included and filtering is the caller's job. If this ever started
		// filtering, the callers that DO filter would keep passing while the ones that forgot would too.
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Gamma");
		await svc.UpsertTranslationAsync(
			page.Id, "fr", "Gamma (fr)", "brouillon", "#2", null,
			published: false, expectedRevisionNumber: null);

		var all = await svc.GetAllTranslationsAsync();

		await Assert.That(all.Count).IsEqualTo(1);
		await Assert.That(all[0].Published).IsFalse();
	}

	[Test]
	public async Task UpsertTranslationAsync_NormalisesTheLocaleTag()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Dragons");

		var translation = (await svc.UpsertTranslationAsync(page.Id, "FR-ca", "T", "m", "#2", null, true, expectedRevisionNumber: null)).Expect<WikiTranslation>();

		await Assert.That(translation.Locale).IsEqualTo("fr-CA");
	}

	[Test]
	public async Task UpsertTranslationAsync_SecondCallBumpsTheRevisionInsteadOfDuplicating()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Dragons");
		await svc.UpsertTranslationAsync(page.Id, "fr", "v1", "corps v1", "#2", null, true, expectedRevisionNumber: null);

		var second = (await svc.UpsertTranslationAsync(
			page.Id, "fr", "v2", "corps v2", "#3", "révision", true, expectedRevisionNumber: 1)).Expect<WikiTranslation>();

		await Assert.That(second.RevisionNumber).IsEqualTo(2);
		await Assert.That(second.MarkdownSource).IsEqualTo("corps v2");
		await Assert.That(second.LastEditorDbref).IsEqualTo("#3");
		await Assert.That((await svc.GetTranslationsAsync(page.Id)).Count)
			.IsEqualTo(1)
			.Because("upsert must not create a second row for the same (PageId, Locale)");
	}

	[Test]
	public async Task UpsertTranslationAsync_NullExpectedRevisionMeansCreateOnly()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Dragons");
		await svc.UpsertTranslationAsync(page.Id, "fr", "v1", "corps v1", "#2", null, true, expectedRevisionNumber: null);

		var again = await svc.UpsertTranslationAsync(
			page.Id, "fr", "écrasé", "corps écrasé", "#3", null, true, expectedRevisionNumber: null);

		await Assert.That(again.Value)
			.IsEqualTo(WikiWriteConflict.AlreadyExists)
			.Because("a caller who passed null believed it was creating a translation, not overwriting one");
		var stored = (await svc.GetTranslationAsync(page.Id, "fr")).Expect<WikiTranslation>();
		await Assert.That(stored.MarkdownSource)
			.IsEqualTo("corps v1");
	}

	[Test]
	public async Task UpsertTranslationAsync_RejectsAStaleExpectedRevision()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Dragons");
		await svc.UpsertTranslationAsync(page.Id, "fr", "v1", "corps v1", "#2", null, true, expectedRevisionNumber: null);
		await svc.UpsertTranslationAsync(page.Id, "fr", "v2", "corps v2", "#3", null, true, expectedRevisionNumber: 1);

		// A second translator who loaded revision 1 and is only now saving.
		var stale = await svc.UpsertTranslationAsync(
			page.Id, "fr", "perdu", "corps perdu", "#4", null, true, expectedRevisionNumber: 1);

		await Assert.That(stale.Value).IsEqualTo(WikiWriteConflict.StaleRevision);
		var stored = (await svc.GetTranslationAsync(page.Id, "fr")).Expect<WikiTranslation>();
		await Assert.That(stored.MarkdownSource)
			.IsEqualTo("corps v2")
			.Because("the winner's prose must survive; the loser reloads and the human decides");
		var revisions = await svc.GetRevisionsForLocaleAsync(page.Id, "fr", 0, 20);
		await Assert.That(revisions.Count).IsEqualTo(2);
		await Assert.That(revisions.Select(r => r.MarkdownSource))
			.DoesNotContain("corps perdu")
			.Because("a rejected write must leave no revision behind");
	}

	[Test]
	public async Task UpsertTranslationAsync_ReportsAConflictWhenTheTranslationWasDeletedMidEdit()
	{
		// The third lost-write shape: the editor loaded revision 1 and somebody deleted the locale before
		// they saved. It is not a malformed request — the caller must reload, exactly as for a stale
		// revision — so it must not be classified alongside "your body was invalid".
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Dragons");
		await svc.UpsertTranslationAsync(page.Id, "fr", "v1", "corps v1", "#2", null, true, expectedRevisionNumber: null);
		await svc.DeleteTranslationAsync(page.Id, "fr", "#3");

		var orphaned = await svc.UpsertTranslationAsync(
			page.Id, "fr", "v2", "corps v2", "#2", null, true, expectedRevisionNumber: 1);

		await Assert.That(orphaned.Value).IsEqualTo(WikiWriteConflict.TranslationGone);
		await Assert.That((await svc.GetTranslationAsync(page.Id, "fr")).Value).IsTypeOf<NotFound>()
			.Because("a compare-and-swap must not resurrect a row somebody deliberately deleted");
	}

	[Test]
	public async Task UpsertTranslationAsync_RejectsAnUnparseableLocale()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Dragons");

		var result = await svc.UpsertTranslationAsync(page.Id, "not a locale", "T", "m", "#2", null, true, expectedRevisionNumber: null);

		await Assert.That(result.Value).IsTypeOf<Error<string>>()
			.Because("a malformed locale is the caller's mistake to fix, not a race it lost");
	}

	[Test]
	public async Task UpsertTranslationAsync_RejectsShadowingTheSourceLocale()
	{
		var svc = BuildService();
		var page = (await svc.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "en")).Expect<WikiPage>();

		var result = await svc.UpsertTranslationAsync(page.Id, "en", "T", "m", "#2", null, true, expectedRevisionNumber: null);

		await Assert.That(result.Value).IsTypeOf<Error<string>>()
			.Because("no row may shadow the source; the page itself is edited instead");
	}

	[Test]
	public async Task UpsertTranslationAsync_RejectsAnUnknownPage()
	{
		var svc = BuildService();

		var result = await svc.UpsertTranslationAsync("ghost", "fr", "T", "m", "#2", null, true, expectedRevisionNumber: null);

		await Assert.That(result.Value).IsTypeOf<Error<string>>();
	}

	[Test]
	public async Task GetTranslationAsync_ReturnsNotFoundForAMissingLocale()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Dragons");

		var result = await svc.GetTranslationAsync(page.Id, "de");

		await Assert.That(result.Value).IsTypeOf<NotFound>();
	}

	[Test]
	public async Task GetTranslationsAsync_ReturnsBodylessSummariesIncludingDrafts()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Dragons");
		await svc.UpsertTranslationAsync(page.Id, "fr", "Dragons (fr)", "m", "#2", null, published: true, expectedRevisionNumber: null);
		await svc.UpsertTranslationAsync(page.Id, "de", "Drachen", "m", "#2", null, published: false, expectedRevisionNumber: null);

		var summaries = await svc.GetTranslationsAsync(page.Id);

		await Assert.That(summaries.Count).IsEqualTo(2);
		await Assert.That(summaries.Select(s => s.Locale).Order()).IsEquivalentTo(new[] { "de", "fr" });
		await Assert.That(summaries.Single(s => s.Locale == "de").Published)
			.IsFalse()
			.Because("storage returns every row; visibility filtering is the caller's job");
	}

	[Test]
	public async Task GetRevisionsForLocaleAsync_IsASeparateStreamFromTheSource()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Dragons", markdown: "v1");
		await svc.UpdateAsync(page.Id, "v2", "#1");
		await svc.UpsertTranslationAsync(page.Id, "fr", "T", "fr1", "#2", null, true, expectedRevisionNumber: null);
		await svc.UpsertTranslationAsync(page.Id, "fr", "T", "fr2", "#2", null, true, expectedRevisionNumber: 1);

		var french = await svc.GetRevisionsForLocaleAsync(page.Id, "fr", 0, 20);
		var source = await svc.GetRevisionsAsync(page.Id);

		await Assert.That(french.Count).IsEqualTo(2);
		await Assert.That(french.All(r => r.Locale == "fr")).IsTrue();
		await Assert.That(source.Count)
			.IsEqualTo(2)
			.Because("GetRevisionsAsync must keep returning only the source stream for its five existing callers");
		await Assert.That(source.All(r => r.Locale.Length == 0)).IsTrue();
	}

	/// <summary>
	/// An unrecognised tag names no stream. It used to normalise to empty, which is the source stream's
	/// marker, so a junk <c>lang</c> read the source page's history as if it were a translation's.
	/// </summary>
	[Test]
	public async Task LocaleRevisionReads_WithAnUnrecognisedTag_FindNothing()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Wyverns", markdown: "source v1");

		await Assert.That(await svc.GetRevisionsForLocaleAsync(page.Id, "not a locale", 0, 20)).IsEmpty();
		await Assert.That(await svc.GetRevisionForLocaleAsync(page.Id, "not a locale", 1) is NotFound).IsTrue();
		await Assert.That((await svc.GetRevisionsForLocaleAsync(page.Id, string.Empty, 0, 20)).Count)
			.IsEqualTo(1)
			.Because("empty is still the source stream");
	}

	[Test]
	public async Task GetRevisionAsync_NeverReturnsATranslationRevision()
	{
		// The two rollback paths (WikiController.Rollback, @wiki/rollback) feed this method's Markdown
		// straight back into the source page, so a translation revision leaking through here would restore
		// French prose over an English page. Translation streams restart at 1, so r1 is the collision.
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Dragons", markdown: "en v1");
		await svc.UpsertTranslationAsync(page.Id, "fr", "T", "corps fr", "#2", null, true, expectedRevisionNumber: null);

		var revision = (await svc.GetRevisionAsync(page.Id, 1)).Expect<WikiRevision>();
		await Assert.That(revision.Locale).IsEqualTo(string.Empty);
		await Assert.That(revision.MarkdownSource)
			.IsEqualTo("en v1")
			.Because("a rollback must restore the source body, never a translation's");
	}

	[Test]
	public async Task GetRevisionForLocaleAsync_ReturnsTheRequestedLocalesRevisionNotTheSources()
	{
		// Numbering restarts at 1 per locale, so revision 1 exists in both streams with different prose.
		// If this returned the source row the history page would diff French against English and render it
		// as a plausible rewrite rather than an error.
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Dragons", markdown: "en v1");
		await svc.UpsertTranslationAsync(page.Id, "fr", "T", "corps fr", "#2", null, true, expectedRevisionNumber: null);

		var french = await svc.GetRevisionForLocaleAsync(page.Id, "fr", 1);
		var source = await svc.GetRevisionForLocaleAsync(page.Id, string.Empty, 1);

		var frenchRevision = french.Expect<WikiRevision>();
		await Assert.That(frenchRevision.Locale).IsEqualTo("fr");
		await Assert.That(frenchRevision.MarkdownSource).IsEqualTo("corps fr");
		var sourceRevision = source.Expect<WikiRevision>();
		await Assert.That(sourceRevision.MarkdownSource)
			.IsEqualTo("en v1")
			.Because("the empty stream is the source's, and the two rows share a revision number");
	}

	[Test]
	public async Task GetRevisionForLocaleAsync_IsNotFoundWhenThatStreamLacksTheNumber()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Dragons", markdown: "en v1");
		await svc.UpdateAsync(page.Id, "en v2", "#1");
		await svc.UpsertTranslationAsync(page.Id, "fr", "T", "corps fr", "#2", null, true, expectedRevisionNumber: null);

		// Source revision 2 exists; French stops at 1.
		var result = await svc.GetRevisionForLocaleAsync(page.Id, "fr", 2);

		await Assert.That(result.Value).IsTypeOf<NotFound>()
			.Because("a missing revision in one locale must not fall through to another locale's row");
	}

	[Test]
	public async Task DeleteTranslationAsync_RemovesTheRowAndItsRevisions()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Dragons");
		await svc.UpsertTranslationAsync(page.Id, "fr", "T", "m", "#2", null, true, expectedRevisionNumber: null);

		var deleted = await svc.DeleteTranslationAsync(page.Id, "fr", "#2");

		await Assert.That(deleted.Value).IsTypeOf<None>();
		await Assert.That((await svc.GetTranslationAsync(page.Id, "fr")).Value).IsTypeOf<NotFound>();
		await Assert.That((await svc.GetRevisionsForLocaleAsync(page.Id, "fr", 0, 20)).Count).IsEqualTo(0);
	}

	[Test]
	public async Task DeleteTranslationAsync_DeletingTheLastTranslationIsAllowed()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Dragons");
		await svc.UpsertTranslationAsync(page.Id, "fr", "T", "m", "#2", null, true, expectedRevisionNumber: null);

		await svc.DeleteTranslationAsync(page.Id, "fr", "#2");

		await Assert.That((await svc.GetTranslationsAsync(page.Id)).Count).IsEqualTo(0);
		await Assert.That((await svc.GetBySlugAsync(page.Slug, WikiNamespace.Main)).Value).IsTypeOf<WikiPage>()
			.Because("removing the last translation must not remove the page");
	}

	[Test]
	public async Task DeleteTranslationAsync_ReturnsNotFoundForAMissingLocale()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Dragons");

		await Assert.That((await svc.DeleteTranslationAsync(page.Id, "fr", "#2")).Value).IsTypeOf<NotFound>();
	}

	[Test]
	public async Task DeleteAsync_CascadesToTranslationsAndTheirRevisions()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Dragons");
		await svc.UpsertTranslationAsync(page.Id, "fr", "T", "m", "#2", null, true, expectedRevisionNumber: null);
		await svc.UpsertTranslationAsync(page.Id, "de", "T", "m", "#2", null, true, expectedRevisionNumber: null);

		await svc.DeleteAsync(page.Id, "#1");

		await Assert.That((await svc.GetTranslationsAsync(page.Id)).Count).IsEqualTo(0);
		await Assert.That((await svc.GetRevisionsForLocaleAsync(page.Id, "fr", 0, 20)).Count).IsEqualTo(0);
		await Assert.That((await svc.GetRevisionsAsync(page.Id)).Count).IsEqualTo(0);
	}

	[Test]
	public async Task CreateAsync_StampsTheSourceLocaleWhenSupplied()
	{
		var svc = BuildService();

		var page = (await svc.CreateAsync("Dragons", "body", "#1", WikiNamespace.Main, "fr-CA")).Expect<WikiPage>();

		await Assert.That(page.SourceLocale).IsEqualTo("fr-CA");
	}

	[Test]
	public async Task CreateAsync_RejectsAnUnparseableSourceLocale()
	{
		var svc = BuildService();

		var result = await svc.CreateAsync("Dragons", "body", "#1", WikiNamespace.Main, "not a locale");

		await Assert.That(result.Value).IsTypeOf<Error<string>>()
			.Because("SourceLocale is materialised and authoritative, so a junk tag must not reach storage");
	}

	[Test]
	public async Task CreateAsync_CanonicalisesTheSourceLocaleItStores()
	{
		var svc = BuildService();

		var page = (await svc.CreateAsync("Dragons", "body", "#1", WikiNamespace.Main, "PT-br")).Expect<WikiPage>();

		await Assert.That(page.SourceLocale).IsEqualTo("pt-BR");
	}

	[Test]
	public async Task CreateAsync_LeavesSourceLocaleUnstampedWhenNotSupplied()
	{
		var svc = BuildService();

		var page = (await svc.CreateAsync("Dragons", "body", "#1")).Expect<WikiPage>();

		await Assert.That(page.SourceLocale)
			.IsEqualTo(string.Empty)
			.Because("null means 'not stamped', a transient state the Tasks 7-9 backfill closes. It is NOT a "
				+ "read-time synonym for Wiki.DefaultLocale — the two real create paths (Tasks 12 and 20) "
				+ "pass IWikiLocalizationService.DefaultLocale so this branch is only reached by tests");
	}
}
