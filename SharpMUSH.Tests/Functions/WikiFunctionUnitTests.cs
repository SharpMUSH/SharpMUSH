using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// Integration tests for the wiki() softcode functions, run against the seeded
/// wiki pages ("home" in main, "markdown_guide" in help) plus pages created here.
/// </summary>
public class WikiFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IWikiService WikiService => WebAppFactoryArg.Services.GetRequiredService<IWikiService>();

	private async Task<string> Eval(string expression) =>
		(await Parser.FunctionParse(MModule.single(expression)))!.Message!.ToPlainText();

	[Test]
	public async Task Wiki_Title_ReturnsSeededHomeTitle()
	{
		await Assert.That(await Eval("wiki(home, title)")).IsEqualTo("Home");
	}

	[Test]
	public async Task Wiki_DefaultField_ReturnsPlainText()
	{
		var result = await Eval("wiki(home)");

		await Assert.That(result).Contains("Getting started");
		await Assert.That(result).DoesNotContain("##"); // markdown stripped
	}

	[Test]
	public async Task Wiki_NamespacePrefix_ResolvesHelpPage()
	{
		await Assert.That(await Eval("wiki(help:markdown_guide, title)")).IsEqualTo("Markdown Guide");
		await Assert.That(await Eval("wiki(help:Markdown Guide, namespace)")).IsEqualTo("help");
	}

	[Test]
	public async Task Wiki_UnknownPage_ReturnsError()
	{
		await Assert.That(await Eval("wiki(definitely_not_a_page)"))
			.IsEqualTo("#-1 NO SUCH WIKI PAGE");
	}

	[Test]
	public async Task Wiki_UnknownField_ReturnsError()
	{
		await Assert.That(await Eval("wiki(home, nonsense)"))
			.IsEqualTo("#-1 UNKNOWN WIKI FIELD");
	}

	[Test]
	public async Task Wiki_RevisionField_IsNumeric()
	{
		var revision = await Eval("wiki(home, revision)");

		await Assert.That(int.TryParse(revision, out var value)).IsTrue();
		await Assert.That(value).IsGreaterThanOrEqualTo(1);
	}

	[Test]
	public async Task WikiList_HelpNamespace_ContainsGuideReference()
	{
		var result = await Eval("wikilist(help)");

		await Assert.That(result).Contains("help:general:markdown_guide");
	}

	[Test]
	public async Task WikiList_UnknownNamespace_ReturnsError()
	{
		await Assert.That(await Eval("wikilist(bogus)"))
			.IsEqualTo("#-1 NO SUCH WIKI NAMESPACE");
	}

	[Test]
	public async Task WikiSearch_FindsPageByBody()
	{
		var created = await WikiService.CreateAsync(
			"Function Search Target", "Contains the plugh-marker token.", "#1");
		await Assert.That(created.IsT0).IsTrue();

		var result = await Eval("wikisearch(plugh-marker)");

		await Assert.That(result).Contains("function_search_target");
	}

	[Test]
	public async Task WikiRecent_ReturnsReferences_AndValidatesCount()
	{
		var recent = await Eval("wikirecent()");
		await Assert.That(recent.Length).IsGreaterThan(0);

		var error = await Eval("wikirecent(9999)");
		await Assert.That(error).StartsWith("#-1");
	}

	/// <summary>Creates a page with one published French translation and returns its slug.</summary>
	private async Task<string> SeedTranslatedPageAsync(string title)
	{
		var created = await WikiService.CreateAsync(
			title, "en fn body", "#1", WikiNamespace.Main, "general", "en");
		await Assert.That(created.IsT0).IsTrue();
		var page = created.AsT0;

		// Create-only: this is the first write for fr, so there is no revision to compare against.
		var translated = await WikiService.UpsertTranslationAsync(
			page.Id, "fr", $"{title} (fr)", "corps fn fr", "#1", null, published: true,
			expectedRevisionNumber: null);
		await Assert.That(translated.IsT0).IsTrue();

		return page.Slug;
	}

	[Test]
	public async Task Wiki_ThirdArgumentSelectsTheLocale()
	{
		var slug = await SeedTranslatedPageAsync("Fn Locale Page");

		await Assert.That(await Eval($"wiki({slug},title,fr)")).IsEqualTo("Fn Locale Page (fr)");
		await Assert.That(await Eval($"wiki({slug},text,fr)")).Contains("corps fn fr");
	}

	[Test]
	public async Task Wiki_LocaleFieldReturnsTheServedLocale()
	{
		var slug = await SeedTranslatedPageAsync("Fn Served Locale Page");

		await Assert.That(await Eval($"wiki({slug},locale,fr)")).IsEqualTo("fr");
		await Assert.That(await Eval($"wiki({slug},locale,de)"))
			.IsEqualTo("en")
			.Because("the locale field reports what was served, which is how softcode detects a fallback");
	}

	[Test]
	public async Task Wiki_UnparseableLocaleFallsBackRatherThanErroring()
	{
		var slug = await SeedTranslatedPageAsync("Fn Bad Locale Page");

		await Assert.That(await Eval($"wiki({slug},text,not a locale)")).Contains("en fn body");
	}

	[Test]
	public async Task Wiki_DraftTranslationIsNeverReachableFromSoftcode()
	{
		var created = await WikiService.CreateAsync(
			"Fn Draft Locale Page", "en draft-host body", "#1",
			WikiNamespace.Main, "general", "en");
		var page = created.AsT0;
		await WikiService.UpsertTranslationAsync(
			page.Id, "fr", "Brouillon fn", "corps brouillon fn", "#1", null, published: false,
			expectedRevisionNumber: null);

		await Assert.That(await Eval($"wiki({page.Slug},text,fr)"))
			.Contains("en draft-host body")
			.Because("softcode has no per-locale permission to check, so a draft is never reachable from it");
	}

	[Test]
	public async Task Wiki_FourArgumentsIsStillAnArgumentCountError()
	{
		// MaxArgs moved 2 → 3 for the locale; it must not have become unbounded.
		await Assert.That(await Eval("wiki(home,text,fr,extra)")).StartsWith("#-1");
	}

	/// <remarks>
	/// The design spec says <c>wikilist()</c> and <c>wikirecent()</c> "return localized titles". They do
	/// not return titles at all — they return page *references*, the canonical slug (or ns:category:slug),
	/// which is what makes their output a valid input to <c>wiki()</c> and <c>@wiki</c>. A slug has no
	/// locale dimension, so there is nothing here to localize, and localizing anyway would cost one
	/// translation lookup per row for output that is byte-identical. This test pins the contract so the
	/// spec sentence does not get "fixed" into a breaking change.
	/// </remarks>
	[Test]
	public async Task WikiList_ReturnsReferencesAndIsLocaleIndependent()
	{
		var slug = await SeedTranslatedPageAsync("Fn Listed Locale Page");

		var listed = await Eval("wikilist(main)");

		await Assert.That(listed).Contains(slug);
		await Assert.That(listed)
			.DoesNotContain("(fr)")
			.Because("a reference is a slug, never a title, so no locale can change it");
	}
}
