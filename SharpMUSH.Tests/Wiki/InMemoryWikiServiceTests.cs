using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// Unit tests for <see cref="InMemoryWikiService"/>.
/// All tests run against a fresh service instance to ensure isolation.
/// Note: CreateAsync derives the slug from the title via Slugify(title).
/// </summary>
public class InMemoryWikiServiceTests
{
	// ── Helpers ──────────────────────────────────────────────────────────────

	private static IWikiService BuildService() =>
		new InMemoryWikiService(new WikiMarkdigPipeline());

	private static Task<WikiPage> CreatePageAsync(
		IWikiService svc,
		string title = "Test Page",
		WikiNamespace ns = WikiNamespace.Main,
		string markdown = "Hello **world**.",
		string editor = "#1")
		=> svc.CreateAsync(title, markdown, editor, ns);

	// ── CreateAsync ──────────────────────────────────────────────────────────

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

		// Slugify: lower, spaces→underscores
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
	public async Task CreateAsync_SameTitleSameNamespace_ThrowsInvalidOperation()
	{
		var svc = BuildService();
		await CreatePageAsync(svc, title: "Duplicate");

		await Assert.That(async () => await CreatePageAsync(svc, title: "Duplicate"))
			.Throws<InvalidOperationException>();
	}

	[Test]
	public async Task CreateAsync_SameTitleDifferentNamespace_Succeeds()
	{
		var svc = BuildService();
		var main = await CreatePageAsync(svc, title: "Intro", ns: WikiNamespace.Main);
		var help = await CreatePageAsync(svc, title: "Intro", ns: WikiNamespace.Help);

		await Assert.That(main.Id).IsNotEqualTo(help.Id);
	}

	// ── GetBySlugAsync ────────────────────────────────────────────────────────

	[Test]
	public async Task GetBySlugAsync_ExistingPage_ReturnsPage()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc, title: "Find Me");

		// Slug generated from title: "find_me"
		var found = await svc.GetBySlugAsync("find_me", WikiNamespace.Main);

		await Assert.That(found).IsNotNull();
		await Assert.That(found!.Id).IsEqualTo(created.Id);
	}

	[Test]
	public async Task GetBySlugAsync_MissingSlug_ReturnsNull()
	{
		var svc = BuildService();
		var found = await svc.GetBySlugAsync("nonexistent", WikiNamespace.Main);

		await Assert.That(found).IsNull();
	}

	[Test]
	public async Task GetBySlugAsync_WrongNamespace_ReturnsNull()
	{
		var svc = BuildService();
		await CreatePageAsync(svc, title: "Ns Test", ns: WikiNamespace.Main);

		// "ns_test" exists in Main but not in Help
		var found = await svc.GetBySlugAsync("ns_test", WikiNamespace.Help);

		await Assert.That(found).IsNull();
	}

	// ── GetByIdAsync ──────────────────────────────────────────────────────────

	[Test]
	public async Task GetByIdAsync_ExistingId_ReturnsPage()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc);

		var found = await svc.GetByIdAsync(created.Id);

		await Assert.That(found).IsNotNull();
		await Assert.That(found!.Id).IsEqualTo(created.Id);
	}

	[Test]
	public async Task GetByIdAsync_MissingId_ReturnsNull()
	{
		var svc = BuildService();
		var found = await svc.GetByIdAsync("id_that_does_not_exist");

		await Assert.That(found).IsNull();
	}

	// ── UpdateAsync ───────────────────────────────────────────────────────────

	[Test]
	public async Task UpdateAsync_ChangesMarkdownAndBumpsRevision()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc, markdown: "Original content.");

		var updated = await svc.UpdateAsync(created.Id, "Updated content.", "#2", "v2");

		await Assert.That(updated.RevisionNumber).IsEqualTo(2);
		await Assert.That(updated.MarkdownSource).IsEqualTo("Updated content.");
		await Assert.That(updated.LastEditorDbref).IsEqualTo("#2");
	}

	[Test]
	public async Task UpdateAsync_RenderedHtmlReflectsNewContent()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc, markdown: "# Old");

		var updated = await svc.UpdateAsync(created.Id, "# New Heading", "#1", "rework");

		await Assert.That(updated.RenderedHtml).Contains("New Heading");
	}

	[Test]
	public async Task UpdateAsync_MissingId_ThrowsKeyNotFound()
	{
		var svc = BuildService();

		await Assert.That(async () => await svc.UpdateAsync("ghost_id", "content", "#1"))
			.Throws<KeyNotFoundException>();
	}

	// ── DeleteAsync ───────────────────────────────────────────────────────────

	[Test]
	public async Task DeleteAsync_ExistingPage_ReturnsTrueAndRemovesPage()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc, title: "To Delete");

		var result = await svc.DeleteAsync(created.Id, "#1");

		await Assert.That(result).IsTrue();
		var found = await svc.GetByIdAsync(created.Id);
		await Assert.That(found).IsNull();
	}

	[Test]
	public async Task DeleteAsync_SlugBecomesAvailableAfterDeletion()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc, title: "Reusable Slug");
		await svc.DeleteAsync(created.Id, "#1");

		// Should not throw — slug freed
		var recreated = await CreatePageAsync(svc, title: "Reusable Slug");
		await Assert.That(recreated.Slug).IsEqualTo("reusable_slug");
	}

	[Test]
	public async Task DeleteAsync_MissingId_ReturnsFalse()
	{
		var svc = BuildService();
		var result = await svc.DeleteAsync("ghost_id", "#1");

		await Assert.That(result).IsFalse();
	}

	// ── GetRevisionsAsync ─────────────────────────────────────────────────────

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

	// ── GetRevisionAsync ──────────────────────────────────────────────────────

	[Test]
	public async Task GetRevisionAsync_ValidRevisionNumber_ReturnsRevision()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc, markdown: "First edition.");

		var rev = await svc.GetRevisionAsync(created.Id, 1);

		await Assert.That(rev).IsNotNull();
		await Assert.That(rev!.RevisionNumber).IsEqualTo(1);
		await Assert.That(rev.MarkdownSource).IsEqualTo("First edition.");
	}

	[Test]
	public async Task GetRevisionAsync_MissingRevisionNumber_ReturnsNull()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc);

		var rev = await svc.GetRevisionAsync(created.Id, 99);

		await Assert.That(rev).IsNull();
	}

	// ── SetProtectionAsync ────────────────────────────────────────────────────

	[Test]
	public async Task SetProtectionAsync_SetsIsProtectedFlag()
	{
		var svc = BuildService();
		var created = await CreatePageAsync(svc);
		await Assert.That(created.IsProtected).IsFalse();

		await svc.SetProtectionAsync(created.Id, true);

		var fetched = await svc.GetByIdAsync(created.Id);
		await Assert.That(fetched!.IsProtected).IsTrue();
	}

	[Test]
	public async Task SetProtectionAsync_MissingId_ThrowsKeyNotFound()
	{
		var svc = BuildService();

		await Assert.That(async () => await svc.SetProtectionAsync("ghost_id", true))
			.Throws<KeyNotFoundException>();
	}

	// ── GetByNamespaceAsync ───────────────────────────────────────────────────

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

	// ── GetRecentChangesAsync ─────────────────────────────────────────────────

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
}
