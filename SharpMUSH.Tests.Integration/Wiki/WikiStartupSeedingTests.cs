using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration.Wiki;

/// <summary>
/// Verifies that <see cref="SharpMUSH.Server.StartupHandler"/> correctly seeds the
/// "home" wiki page on startup, and that the seeded page is retrievable by slug via
/// <see cref="IWikiService.GetBySlugAsync"/>.
///
/// These tests were added to root-cause a 404 on PUT /api/wiki/home.  The GET
/// endpoint worked (page loaded in the browser), but the PUT handler internally
/// calls GetBySlugAsync then UpdateAsync(id, …) and was returning NotFound.
/// The tests below reproduce that exact sequence so the failure mode is reproducible
/// in isolation, separate from auth, middleware, and HTTP routing.
/// </summary>
[NotInParallel]
public class WikiStartupSeedingTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactory { get; init; }

	private IWikiService Wiki =>
		WebAppFactory.Services.GetRequiredService<ISharpDatabase>() as IWikiService
		?? throw new InvalidOperationException(
			"ISharpDatabase does not implement IWikiService in this backend configuration.");

	// ── Seeding ───────────────────────────────────────────────────────────────

	[Test]
	public async Task HomePageIsSeeded_GetBySlugReturnsFound()
	{
		var result = await Wiki.GetBySlugAsync("home", WikiNamespace.Main);

		await Assert.That(result.IsT0)
			.IsTrue()
			.Because("StartupHandler.StartAsync seeds the Home page before the server accepts requests");
	}

	[Test]
	public async Task HomePageIsSeeded_SlugIsHome()
	{
		var result = await Wiki.GetBySlugAsync("home", WikiNamespace.Main);

		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(result.AsT0.Slug).IsEqualTo("home");
	}

	[Test]
	public async Task HomePageIsSeeded_TitleIsHome()
	{
		var result = await Wiki.GetBySlugAsync("home", WikiNamespace.Main);

		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(result.AsT0.Title).IsEqualTo("Home");
	}

	[Test]
	public async Task HomePageIsSeeded_NamespaceIsMain()
	{
		var result = await Wiki.GetBySlugAsync("home", WikiNamespace.Main);

		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(result.AsT0.Namespace).IsEqualTo("main");
	}

	// ── Cross-lookup consistency ──────────────────────────────────────────────

	[Test]
	public async Task HomePageIsSeeded_GetByIdRoundTrip()
	{
		var bySlug = await Wiki.GetBySlugAsync("home", WikiNamespace.Main);
		await Assert.That(bySlug.IsT0).IsTrue();

		var id = bySlug.AsT0.Id;
		await Assert.That(id).IsNotEmpty()
			.Because("ArangoDB assigns a non-empty _id on CreateAsync");

		var byId = await Wiki.GetByIdAsync(id);

		await Assert.That(byId.IsT0).IsTrue();
		await Assert.That(byId.AsT0.Slug).IsEqualTo("home");
	}

	// ── PUT controller path reproduction ─────────────────────────────────────

	/// <summary>
	/// Reproduces the exact call sequence inside <c>WikiController.Put</c>:
	/// <list type="number">
	///   <item>GetBySlugAsync(slug) to resolve the DB id</item>
	///   <item>UpdateAsync(id, markdown, editor, summary)</item>
	/// </list>
	/// If this test fails, the 404 originates in the DB layer.
	/// If this test passes, the bug is above the DB layer (auth, middleware,
	/// HTTP client headers, etc.).
	/// </summary>
	[Test]
	public async Task PutControllerPath_SlugLookupThenUpdateById_Succeeds()
	{
		// ── step 1: slug lookup (same as WikiController.Put) ──────────────────
		var lookup = await Wiki.GetBySlugAsync("home", WikiNamespace.Main);
		await Assert.That(lookup.IsT0).IsTrue();

		var pageId = lookup.AsT0.Id;

		// ── step 2: update by id (same as WikiController.Put) ─────────────────
		var updateResult = await Wiki.UpdateAsync(
			id: pageId,
			markdown: lookup.AsT0.MarkdownSource + "\n\n<!-- PUT path test -->",
			editorDbref: "#1",
			editSummary: "integration-test edit via put-path reproduction");

		await Assert.That(updateResult.IsT0).IsTrue();
		await Assert.That(updateResult.AsT0.RevisionNumber)
			.IsGreaterThan(lookup.AsT0.RevisionNumber)
			.Because("UpdateAsync must increment the revision counter");

		// ── step 3: verify the edit is visible via slug lookup ────────────────
		var afterUpdate = await Wiki.GetBySlugAsync("home", WikiNamespace.Main);
		await Assert.That(afterUpdate.IsT0).IsTrue();
		await Assert.That(afterUpdate.AsT0.MarkdownSource)
			.Contains("<!-- PUT path test -->")
			.Because("the updated markdown must be persisted and returned by slug");
	}
}
