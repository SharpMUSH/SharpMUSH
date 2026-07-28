using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// Unit tests for the static helper methods on <see cref="WikiController"/>
/// that generate pre-render HTML for bot responses.
/// These exercise the HTML generation logic without a running server.
/// </summary>
/// <remarks>
/// The <see cref="LocalizedWikiPage"/> wrapper here is synthetic rather than resolved through
/// <c>IWikiLocalizationService</c>, unlike <see cref="WikiPrerenderLocaleTests"/>. These tests are about
/// HTML escaping and the 200-character description truncation, which need exact control over
/// <c>PlainText</c> and <c>Title</c> — values the real pipeline would derive rather than accept. No locale
/// invariant is exercised here; every case pins <c>Locale</c> to the source locale.
/// </remarks>
public class WikiControllerHtmlTests
{
	private static WikiPage MakePage(
			string title = "Magic System",
			string slug = "magic_system",
			string ns = "main",
			string markdown = "# Magic System\n\nThis is the magic system.",
			string rendered = "<h1>Magic System</h1><p>This is the magic system.</p>",
			string plain = "Magic System This is the magic system.") =>
			new WikiPage(
					Id: "test-id-1",
					Slug: slug,
					Title: title,
					Namespace: ns,
					MarkdownSource: markdown,
					RenderedHtml: rendered,
					PlainText: plain,
					AuthorDbref: "#1",
					LastEditorDbref: "#1",
					CreatedAt: DateTimeOffset.UtcNow,
					UpdatedAt: DateTimeOffset.UtcNow,
					IsProtected: false,
					RevisionNumber: 1)
			{
				SourceLocale = "en",
			};

	/// <summary>Wraps a page as its own source-locale resolution: no translation, nothing to alternate to.</summary>
	private static LocalizedWikiPage Localized(WikiPage page) => new(
			Page: page,
			Locale: "en",
			RequestedLocale: "en",
			Title: page.Title,
			MarkdownSource: page.MarkdownSource,
			RenderedHtml: page.RenderedHtml,
			PlainText: page.PlainText,
			Published: page.Published,
			RevisionNumber: page.RevisionNumber);

	private static readonly string[] SingleLocale = ["en"];

	[Test]
	public async Task GeneratePrerenderHtml_ContainsDoctype()
	{
		var page = MakePage();
		var html = WikiController.GeneratePrerenderHtml(Localized(page), "https://example.com/wiki/Magic_System", SingleLocale, "en");

		await Assert.That(html).Contains("<!DOCTYPE html>");
	}

	[Test]
	public async Task GeneratePrerenderHtml_ContainsCanonicalLink()
	{
		var page = MakePage();
		var url = "https://example.com/wiki/Magic_System";
		var html = WikiController.GeneratePrerenderHtml(Localized(page), url, SingleLocale, "en");

		await Assert.That(html).Contains($"rel=\"canonical\"");
		await Assert.That(html).Contains(url);
	}

	[Test]
	public async Task GeneratePrerenderHtml_ContainsOgTitle()
	{
		var page = MakePage(title: "Magic System");
		var html = WikiController.GeneratePrerenderHtml(Localized(page), "https://example.com/wiki/Magic_System", SingleLocale, "en");

		await Assert.That(html).Contains("og:title");
		await Assert.That(html).Contains("Magic System");
	}

	[Test]
	public async Task GeneratePrerenderHtml_ContainsOgTypeArticle()
	{
		var page = MakePage();
		var html = WikiController.GeneratePrerenderHtml(Localized(page), "https://example.com/wiki/Magic_System", SingleLocale, "en");

		await Assert.That(html).Contains("og:type");
		await Assert.That(html).Contains("article");
	}

	[Test]
	public async Task GeneratePrerenderHtml_ContainsRenderedHtmlBody()
	{
		var page = MakePage(rendered: "<p>The rendered content.</p>");
		var html = WikiController.GeneratePrerenderHtml(Localized(page), "https://example.com/wiki/Magic_System", SingleLocale, "en");

		await Assert.That(html).Contains("<p>The rendered content.</p>");
	}

	[Test]
	public async Task GeneratePrerenderHtml_LongPlainText_DescriptionTruncatedAt200Chars()
	{
		var longPlain = new string('A', 300);
		var page = MakePage(plain: longPlain);
		var html = WikiController.GeneratePrerenderHtml(Localized(page), "https://example.com/wiki/Test", SingleLocale, "en");

		await Assert.That(html).Contains("og:description");
		await Assert.That(html).Contains(new string('A', 200));
	}

	[Test]
	public async Task GeneratePrerenderHtml_TitleHtmlEncoded_SpecialCharsEscaped()
	{
		var page = MakePage(title: "Magic & Mayhem <script>");
		var html = WikiController.GeneratePrerenderHtml(Localized(page), "https://example.com/wiki/Magic_And_Mayhem", SingleLocale, "en");

		await Assert.That(html).DoesNotContain("<script>");
		await Assert.That(html).Contains("&amp;");
	}

	[Test]
	public async Task GenerateCharacterPrerenderHtml_ContainsOgTypeProfile()
	{
		var page = MakePage(title: "Gandalf", ns: "character");
		var html = WikiController.GenerateCharacterPrerenderHtml(Localized(page), "https://example.com/character/Gandalf", SingleLocale, "en");

		await Assert.That(html).Contains("og:type");
		await Assert.That(html).Contains("profile");
	}

	[Test]
	public async Task GenerateCharacterPrerenderHtml_ContainsCharacterName()
	{
		var page = MakePage(title: "Gandalf", ns: "character");
		var html = WikiController.GenerateCharacterPrerenderHtml(Localized(page), "https://example.com/character/Gandalf", SingleLocale, "en");

		await Assert.That(html).Contains("Gandalf");
	}

	[Test]
	public async Task GenerateCharacterPrerenderHtml_TitleFormatIncludesSiteName()
	{
		var page = MakePage(title: "Gandalf", ns: "character");
		var html = WikiController.GenerateCharacterPrerenderHtml(
				Localized(page), "https://example.com/character/Gandalf", SingleLocale, "en", "MyMUSH");

		await Assert.That(html).Contains("Gandalf - MyMUSH");
	}

	[Test]
	public async Task GenerateCharacterPrerenderHtml_ContainsCanonicalLink()
	{
		var page = MakePage(title: "Gandalf", ns: "character");
		var url = "https://example.com/character/Gandalf";
		var html = WikiController.GenerateCharacterPrerenderHtml(Localized(page), url, SingleLocale, "en");

		await Assert.That(html).Contains("rel=\"canonical\"");
		await Assert.That(html).Contains(url);
	}
}
