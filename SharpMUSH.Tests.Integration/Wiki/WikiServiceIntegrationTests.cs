using Microsoft.Extensions.DependencyInjection;
using OneOf.Types;
using SharpMUSH.Library;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration.Wiki;

/// <summary>
/// Integration tests for wiki persistence against the configured DB backend.
/// Relies on <see cref="ServerWebAppFactory"/> which spins up the appropriate container
/// (via Testcontainers) and boots the full application stack. The backend is selected
/// by the <c>SHARPMUSH_DATABASE_PROVIDER</c> environment variable (arangodb / memgraph / surrealdb).
///
/// IWikiService is exposed through the ISharpDatabase singleton; all tests retrieve it
/// from the DI container and verify identical semantics across all three DB providers.
/// </summary>
[NotInParallel]
public class WikiServiceIntegrationTests
{
    [ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
    public required ServerWebAppFactory WebAppFactory { get; init; }

    private IWikiService Wiki => WebAppFactory.Services.GetRequiredService<ISharpDatabase>() as IWikiService
        ?? throw new InvalidOperationException("ISharpDatabase does not implement IWikiService in this configuration.");

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Creates a page, asserts success, returns the resulting <see cref="WikiPage"/>.</summary>
    private async Task<WikiPage> CreatePageAsync(
        string title,
        WikiNamespace ns = WikiNamespace.Main,
        string markdown = "Hello **world**.",
        string editor = "#1")
    {
        var result = await Wiki.CreateAsync(title, markdown, editor, ns);
        await Assert.That(result.IsT0).IsTrue();
        return result.AsT0;
    }

    // ── CreateAsync ───────────────────────────────────────────────────────────

    [Test]
    public async Task CreateAsync_ReturnsPageWithAssignedId()
    {
        var page = await CreatePageAsync($"IntegCreate {Guid.NewGuid():N}");

        await Assert.That(page.Id).IsNotNull();
        await Assert.That(page.Id).IsNotEmpty();
    }

    [Test]
    public async Task CreateAsync_SlugDerivedFromTitle()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var page = await CreatePageAsync($"My Cool Page {uid}");

        await Assert.That(page.Slug).IsEqualTo($"my_cool_page_{uid}");
    }

    [Test]
    public async Task CreateAsync_SetsTitleAndNamespace()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var page = await CreatePageAsync($"Help Intro {uid}", ns: WikiNamespace.Help);

        await Assert.That(page.Title).IsEqualTo($"Help Intro {uid}");
        await Assert.That(page.Namespace).IsEqualTo("help");
    }

    [Test]
    public async Task CreateAsync_StoresMarkdownAndRenderedHtml()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var page = await CreatePageAsync($"RenderTest {uid}", markdown: "Hello **world**.");

        await Assert.That(page.MarkdownSource).IsEqualTo("Hello **world**.");
        await Assert.That(page.RenderedHtml).IsNotNull();
        await Assert.That(page.RenderedHtml).Contains("<strong>world</strong>");
    }

    [Test]
    public async Task CreateAsync_SetsRevisionNumberToOne()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var page = await CreatePageAsync($"RevOne {uid}");

        await Assert.That(page.RevisionNumber).IsEqualTo(1);
    }

    [Test]
    public async Task CreateAsync_SetsAuthorDbref()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var page = await CreatePageAsync($"AuthorTest {uid}", editor: "#42");

        await Assert.That(page.AuthorDbref).IsEqualTo("#42");
        await Assert.That(page.LastEditorDbref).IsEqualTo("#42");
    }

    [Test]
    public async Task CreateAsync_DuplicateSlugSameNamespace_ReturnsError()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var title = $"Duplicate {uid}";
        await CreatePageAsync(title);

        var result = await Wiki.CreateAsync(title, "content", "#1", WikiNamespace.Main);

        await Assert.That(result.IsT1).IsTrue();
        await Assert.That(result.AsT1).IsTypeOf<Error<string>>();
    }

    [Test]
    public async Task CreateAsync_SameTitleDifferentNamespace_BothSucceed()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var title = $"Intro {uid}";
        var main = await CreatePageAsync(title, ns: WikiNamespace.Main);
        var help = await CreatePageAsync(title, ns: WikiNamespace.Help);

        await Assert.That(main.Id).IsNotEqualTo(help.Id);
    }

    [Test]
    public async Task CreateAsync_SameSlugDifferentCategory_BothSucceedAndAreDistinct()
    {
        // Verifies the (Namespace, Category, Slug) unique index in the live provider:
        // the same slug may exist in two categories, but not twice in one.
        var uid = Guid.NewGuid().ToString("N")[..8];
        var title = $"Dragons {uid}";

        var lore = await Wiki.CreateAsync(title, "content", "#1", WikiNamespace.Main, "lore");
        var rules = await Wiki.CreateAsync(title, "content", "#1", WikiNamespace.Main, "rules");
        var dupe = await Wiki.CreateAsync(title, "content", "#1", WikiNamespace.Main, "lore");

        await Assert.That(lore.IsT0).IsTrue();
        await Assert.That(rules.IsT0).IsTrue();
        await Assert.That(lore.AsT0.Id).IsNotEqualTo(rules.AsT0.Id);
        await Assert.That(dupe.IsT1).IsTrue(); // same (ns, category, slug) rejected

        var slug = lore.AsT0.Slug;
        await Assert.That((await Wiki.GetBySlugAsync(slug, "lore", WikiNamespace.Main)).AsT0.Id).IsEqualTo(lore.AsT0.Id);
        await Assert.That((await Wiki.GetBySlugAsync(slug, "rules", WikiNamespace.Main)).AsT0.Id).IsEqualTo(rules.AsT0.Id);
    }

    // ── GetBySlugAsync ────────────────────────────────────────────────────────

    [Test]
    public async Task GetBySlugAsync_ExistingPage_ReturnsPage()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var title = $"Find Me {uid}";
        var created = await CreatePageAsync(title);

        var result = await Wiki.GetBySlugAsync(created.Slug, "general", WikiNamespace.Main);

        await Assert.That(result.IsT0).IsTrue();
        await Assert.That(result.AsT0.Id).IsEqualTo(created.Id);
    }

    [Test]
    public async Task GetBySlugAsync_MissingSlug_ReturnsNotFound()
    {
        var result = await Wiki.GetBySlugAsync($"nonexistent_{Guid.NewGuid():N}", "general", WikiNamespace.Main);

        await Assert.That(result.IsT1).IsTrue();
    }

    [Test]
    public async Task GetBySlugAsync_WrongNamespace_ReturnsNotFound()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var created = await CreatePageAsync($"Ns Test {uid}", ns: WikiNamespace.Main);

        var result = await Wiki.GetBySlugAsync(created.Slug, "general", WikiNamespace.Help);

        await Assert.That(result.IsT1).IsTrue();
    }

    // ── GetByIdAsync ──────────────────────────────────────────────────────────

    [Test]
    public async Task GetByIdAsync_ExistingId_ReturnsPage()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var created = await CreatePageAsync($"ById {uid}");

        var result = await Wiki.GetByIdAsync(created.Id);

        await Assert.That(result.IsT0).IsTrue();
        await Assert.That(result.AsT0.Id).IsEqualTo(created.Id);
    }

    [Test]
    public async Task GetByIdAsync_MissingId_ReturnsNotFound()
    {
        var result = await Wiki.GetByIdAsync($"node_wiki_pages/does_not_exist_{Guid.NewGuid():N}");

        await Assert.That(result.IsT1).IsTrue();
    }

    // ── UpdateAsync ───────────────────────────────────────────────────────────

    [Test]
    public async Task UpdateAsync_ChangesMarkdownAndBumpsRevision()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var created = await CreatePageAsync($"Update Test {uid}", markdown: "Original content.");

        var updateResult = await Wiki.UpdateAsync(created.Id, "Updated content.", "#2", "v2");

        await Assert.That(updateResult.IsT0).IsTrue();
        var updated = updateResult.AsT0;
        await Assert.That(updated.RevisionNumber).IsEqualTo(2);
        await Assert.That(updated.MarkdownSource).IsEqualTo("Updated content.");
        await Assert.That(updated.LastEditorDbref).IsEqualTo("#2");
    }

    [Test]
    public async Task UpdateAsync_RenderedHtmlReflectsNewContent()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var created = await CreatePageAsync($"Render Update {uid}", markdown: "# Old");

        var updateResult = await Wiki.UpdateAsync(created.Id, "# New Heading", "#1", "rework");

        await Assert.That(updateResult.IsT0).IsTrue();
        await Assert.That(updateResult.AsT0.RenderedHtml).Contains("New Heading");
    }

    [Test]
    public async Task UpdateAsync_MissingId_ReturnsNotFound()
    {
        var result = await Wiki.UpdateAsync(
            $"node_wiki_pages/ghost_{Guid.NewGuid():N}", "content", "#1");

        await Assert.That(result.IsT1).IsTrue();
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    [Test]
    public async Task DeleteAsync_ExistingPage_RemovesPage()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var created = await CreatePageAsync($"To Delete {uid}");

        var deleteResult = await Wiki.DeleteAsync(created.Id, "#1");

        await Assert.That(deleteResult.IsT0).IsTrue();
        var getResult = await Wiki.GetByIdAsync(created.Id);
        await Assert.That(getResult.IsT1).IsTrue();
    }

    [Test]
    public async Task DeleteAsync_SlugBecomesAvailableAfterDeletion()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var title = $"Reusable Slug {uid}";
        var created = await CreatePageAsync(title);
        await Wiki.DeleteAsync(created.Id, "#1");

        var recreated = await CreatePageAsync(title);

        await Assert.That(recreated.Slug).IsEqualTo(created.Slug);
    }

    [Test]
    public async Task DeleteAsync_MissingId_ReturnsNotFound()
    {
        var result = await Wiki.DeleteAsync(
            $"node_wiki_pages/ghost_{Guid.NewGuid():N}", "#1");

        await Assert.That(result.IsT1).IsTrue();
    }

    // ── Revisions ─────────────────────────────────────────────────────────────

    [Test]
    public async Task GetRevisionsAsync_AfterCreate_HasOneRevision()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var created = await CreatePageAsync($"Rev Count {uid}");

        var revisions = await Wiki.GetRevisionsAsync(created.Id);

        await Assert.That(revisions.Count).IsEqualTo(1);
        await Assert.That(revisions[0].RevisionNumber).IsEqualTo(1);
    }

    [Test]
    public async Task GetRevisionsAsync_AfterTwoUpdates_HasThreeRevisions()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var created = await CreatePageAsync($"Multi Rev {uid}", markdown: "v1");
        await Wiki.UpdateAsync(created.Id, "v2", "#1");
        await Wiki.UpdateAsync(created.Id, "v3", "#1");

        var revisions = await Wiki.GetRevisionsAsync(created.Id);

        await Assert.That(revisions.Count).IsEqualTo(3);
    }

    [Test]
    public async Task GetRevisionAsync_ValidRevisionNumber_ReturnsRevision()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var created = await CreatePageAsync($"Rev Fetch {uid}", markdown: "First edition.");

        var revResult = await Wiki.GetRevisionAsync(created.Id, 1);

        await Assert.That(revResult.IsT0).IsTrue();
        var rev = revResult.AsT0;
        await Assert.That(rev.RevisionNumber).IsEqualTo(1);
        await Assert.That(rev.MarkdownSource).IsEqualTo("First edition.");
    }

    [Test]
    public async Task GetRevisionAsync_MissingRevisionNumber_ReturnsNotFound()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var created = await CreatePageAsync($"Rev Missing {uid}");

        var result = await Wiki.GetRevisionAsync(created.Id, 99);

        await Assert.That(result.IsT1).IsTrue();
    }

    // ── SetProtectionAsync ────────────────────────────────────────────────────

    [Test]
    public async Task SetProtectionAsync_SetsIsProtectedFlag()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var created = await CreatePageAsync($"Protect Me {uid}");
        await Assert.That(created.IsProtected).IsFalse();

        var protResult = await Wiki.SetProtectionAsync(created.Id, true);

        await Assert.That(protResult.IsT0).IsTrue();
        var fetchResult = await Wiki.GetByIdAsync(created.Id);
        await Assert.That(fetchResult.IsT0).IsTrue();
        await Assert.That(fetchResult.AsT0.IsProtected).IsTrue();
    }

    [Test]
    public async Task SetProtectionAsync_MissingId_ReturnsNotFound()
    {
        var result = await Wiki.SetProtectionAsync(
            $"node_wiki_pages/ghost_{Guid.NewGuid():N}", true);

        await Assert.That(result.IsT1).IsTrue();
    }

    // ── GetByNamespaceAsync ───────────────────────────────────────────────────

    [Test]
    public async Task GetByNamespaceAsync_ReturnsOnlyPagesInNamespace()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        await CreatePageAsync($"Ns Main One {uid}", ns: WikiNamespace.Main);
        await CreatePageAsync($"Ns Main Two {uid}", ns: WikiNamespace.Main);
        await CreatePageAsync($"Ns Help One {uid}", ns: WikiNamespace.Help);

        var mainPages = await Wiki.GetByNamespaceAsync(WikiNamespace.Main);
        var helpPages = await Wiki.GetByNamespaceAsync(WikiNamespace.Help);

        // Other tests may have also created pages in the same session; use >= not ==
        await Assert.That(mainPages.Count).IsGreaterThanOrEqualTo(2);
        await Assert.That(helpPages.Count).IsGreaterThanOrEqualTo(1);
        await Assert.That(mainPages.All(p => p.Namespace == "main")).IsTrue();
        await Assert.That(helpPages.All(p => p.Namespace == "help")).IsTrue();
    }

    // ── GetRecentChangesAsync ─────────────────────────────────────────────────

    [Test]
    public async Task GetRecentChangesAsync_ReturnsNewestFirstAndRespectsCount()
    {
        // Create 3 uniquely named pages so they are guaranteed to exist
        var uid = Guid.NewGuid().ToString("N")[..8];
        await CreatePageAsync($"Rc Page A {uid}");
        await Task.Delay(10); // ensure distinct UpdatedAt timestamps
        var pageB = await CreatePageAsync($"Rc Page B {uid}");
        await Task.Delay(10);
        var pageC = await CreatePageAsync($"Rc Page C {uid}");

        // Fetch only 2 — most recent 2 should be C then B
        var recent = await Wiki.GetRecentChangesAsync(count: 2);

        await Assert.That(recent.Count).IsEqualTo(2);
        // Most recent is C; second is B
        await Assert.That(recent[0].Id).IsEqualTo(pageC.Id);
        await Assert.That(recent[1].Id).IsEqualTo(pageB.Id);
    }

    [Test]
    public async Task GetRecentChangesAsync_UpdatedPageRisesToTop()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var pageA = await CreatePageAsync($"Rc Rise A {uid}");
        await Task.Delay(10);
        await CreatePageAsync($"Rc Rise B {uid}");
        await Task.Delay(10);
        // Now update A — it should become the most recent change
        await Wiki.UpdateAsync(pageA.Id, "updated content", "#1");

        var recent = await Wiki.GetRecentChangesAsync(count: 2);

        await Assert.That(recent[0].Id).IsEqualTo(pageA.Id);
    }
}
