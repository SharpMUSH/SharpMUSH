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

	[Test]
	public async Task HomePageIsSeeded_GetBySlugReturnsFound()
	{
		var result = await Wiki.GetBySlugAsync("home", "general", WikiNamespace.Main);

		await Assert.That(result.IsT0)
			.IsTrue()
			.Because("StartupHandler.StartAsync seeds the Home page before the server accepts requests");
	}

	[Test]
	public async Task HomePageIsSeeded_SlugIsHome()
	{
		var result = await Wiki.GetBySlugAsync("home", "general", WikiNamespace.Main);

		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(result.AsT0.Slug).IsEqualTo("home");
	}

	[Test]
	public async Task HomePageIsSeeded_TitleIsHome()
	{
		var result = await Wiki.GetBySlugAsync("home", "general", WikiNamespace.Main);

		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(result.AsT0.Title).IsEqualTo("Home");
	}

	[Test]
	public async Task MarkdownGuideIsSeeded_InHelpNamespace()
	{
		var result = await Wiki.GetBySlugAsync("markdown_guide", "general", WikiNamespace.Help);

		await Assert.That(result.IsT0)
			.IsTrue()
			.Because("StartupHandler seeds the Help:Markdown Guide page on startup");
		await Assert.That(result.AsT0.Title).IsEqualTo("Markdown Guide");
		await Assert.That(result.AsT0.RenderedHtml).Contains("data-directive=\"recent\"");
	}

	[Test]
	public async Task HomePageIsSeeded_NamespaceIsMain()
	{
		var result = await Wiki.GetBySlugAsync("home", "general", WikiNamespace.Main);

		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(result.AsT0.Namespace).IsEqualTo("main");
	}

	[Test]
	public async Task HomePageIsSeeded_GetByIdRoundTrip()
	{
		var bySlug = await Wiki.GetBySlugAsync("home", "general", WikiNamespace.Main);
		await Assert.That(bySlug.IsT0).IsTrue();

		var id = bySlug.AsT0.Id;
		await Assert.That(id).IsNotEmpty()
			.Because("ArangoDB assigns a non-empty _id on CreateAsync");

		var byId = await Wiki.GetByIdAsync(id);

		await Assert.That(byId.IsT0).IsTrue();
		await Assert.That(byId.AsT0.Slug).IsEqualTo("home");
	}

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
		var lookup = await Wiki.GetBySlugAsync("home", "general", WikiNamespace.Main);
		await Assert.That(lookup.IsT0).IsTrue();

		var pageId = lookup.AsT0.Id;

		var updateResult = await Wiki.UpdateAsync(
			id: pageId,
			markdown: lookup.AsT0.MarkdownSource + "\n\n<!-- PUT path test -->",
			editorDbref: "#1",
			editSummary: "integration-test edit via put-path reproduction");

		await Assert.That(updateResult.IsT0).IsTrue();
		await Assert.That(updateResult.AsT0.RevisionNumber)
			.IsGreaterThan(lookup.AsT0.RevisionNumber)
			.Because("UpdateAsync must increment the revision counter");

		var afterUpdate = await Wiki.GetBySlugAsync("home", "general", WikiNamespace.Main);
		await Assert.That(afterUpdate.IsT0).IsTrue();
		await Assert.That(afterUpdate.AsT0.MarkdownSource)
			.Contains("<!-- PUT path test -->")
			.Because("the updated markdown must be persisted and returned by slug");
	}

	/// <summary>The (namespace, category, slug) identity of every page <c>SeedWikiPagesAsync</c> creates.</summary>
	private static readonly (WikiNamespace Ns, string Category, string Slug)[] SeededPages =
	[
		(WikiNamespace.Main, "general", "home"),
		(WikiNamespace.Help, "general", "markdown_guide"),
		(WikiNamespace.Help, "general", "application_schema_guide")
	];

	[Test]
	public async Task SeededPagesCarryAnExplicitSourceLocale()
	{
		var result = await Wiki.GetBySlugAsync("home", "general", WikiNamespace.Main);

		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(result.AsT0.SourceLocale)
			.IsEqualTo("en")
			.Because("the seeded pages are English, and labelling them keeps a non-English game from mislabelling them");
	}

	[Test]
	public async Task SeededHelpPagesCarryAnExplicitSourceLocale()
	{
		foreach (var (ns, category, slug) in SeededPages.Where(p => p.Ns == WikiNamespace.Help))
		{
			var result = await Wiki.GetBySlugAsync(slug, category, ns);

			await Assert.That(result.IsT0).IsTrue().Because($"{slug} is seeded at startup");
			await Assert.That(result.AsT0.SourceLocale).IsEqualTo("en");
		}
	}

	[Test]
	public async Task NoTranslationsAreSeeded()
	{
		foreach (var (ns, category, slug) in SeededPages)
		{
			var result = await Wiki.GetBySlugAsync(slug, category, ns);
			await Assert.That(result.IsT0).IsTrue();

			var translations = await Wiki.GetTranslationsAsync(result.AsT0.Id);

			await Assert.That(translations)
				.IsEmpty()
				.Because("machine-quality translated help is worse than a visible gap the fallback notice makes actionable");
		}
	}

	[Test]
	public async Task SeedingStaysIdempotentWithSourceLocalePresent()
	{
		// StartupHandler ran once before this suite. A second pass must be a no-op, not a duplicate or an
		// error, and must not change the recorded source locale.
		var before = (await Wiki.GetBySlugAsync("home", "general", WikiNamespace.Main)).AsT0;

		var second = await Wiki.CreateAsync("Home", "different body", "#1", WikiNamespace.Main, "general", "en");

		await Assert.That(second.IsT1)
			.IsTrue()
			.Because("CreateAsync rejects a duplicate identity, which is what makes seeding idempotent");
		var after = (await Wiki.GetBySlugAsync("home", "general", WikiNamespace.Main)).AsT0;
		await Assert.That(after.SourceLocale).IsEqualTo(before.SourceLocale);
		await Assert.That(after.MarkdownSource).IsEqualTo(before.MarkdownSource);
	}

	/// <remarks>
	/// Scoped to the seeded pages on purpose. The session database is shared and never reset, and other
	/// suites legitimately create pages through the no-locale <c>CreateAsync</c> overload, which stores
	/// <see cref="string.Empty"/>; asserting over <c>GetAllPagesAsync</c> would make this pass or fail on
	/// test ordering. The seeded pages are the ones the migration and the seeder are jointly responsible for.
	/// </remarks>
	[Test]
	public async Task NoSeededPageIsLeftUnstampedAndItsRevisionStreamSurvives()
	{
		foreach (var (ns, category, slug) in SeededPages)
		{
			var lookup = await Wiki.GetBySlugAsync(slug, category, ns);
			await Assert.That(lookup.IsT0).IsTrue();
			var page = lookup.AsT0;

			await Assert.That(page.SourceLocale.Length)
				.IsGreaterThan(0)
				.Because("nothing normalises an empty SourceLocale to the default on read, so an unstamped "
					+ "row stays unstamped and would resolve against a blank locale forever");

			// The migration rewrote every pre-existing revision row to carry the source stream's marker
			// before the unique (PageId, Locale, RevisionNumber) constraint was created. What is observable
			// afterwards is that the stream is still whole: contiguous numbers ending at the page's current
			// revision. Asserting `r.Locale == ""` instead would be vacuous — GetRevisionsAsync filters on
			// exactly that, so it cannot tell a backfilled row from one the backfill missed.
			var revisions = await Wiki.GetRevisionsAsync(page.Id, 0, 50);

			await Assert.That(revisions.Count)
				.IsGreaterThan(0)
				.Because("seeding writes revision 1, and the backfill must not have orphaned it");
			await Assert.That(revisions.Max(r => r.RevisionNumber))
				.IsEqualTo(page.RevisionNumber)
				.Because("the source stream's head must still match the page after the backfill");
			await Assert.That(revisions.Select(r => r.RevisionNumber).Distinct().Count())
				.IsEqualTo(revisions.Count)
				.Because("a duplicate number in one stream is what the unique constraint exists to prevent");
		}
	}
}
