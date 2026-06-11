using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Server.Controllers;
using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// Unit tests for <see cref="SeoController"/>: the sitemap must include only published
/// wiki pages with namespace-aware URLs and yyyy-MM-dd lastmod values, and robots.txt
/// must advertise the sitemap and block admin paths.
/// </summary>
public class SeoControllerTests
{
	private static SeoController MakeController(InMemoryWikiService wiki)
	{
		var controller = new SeoController(wiki, NullLogger<SeoController>.Instance);

		var httpContext = new DefaultHttpContext();
		httpContext.Request.Scheme = "https";
		httpContext.Request.Host = new HostString("example.com");

		controller.ControllerContext = new ControllerContext
		{
			HttpContext = httpContext
		};
		return controller;
	}

	private static async Task<string> GetSitemapXml(SeoController controller)
	{
		var result = await controller.Sitemap();
		var content = (ContentResult)result;
		return content.Content!;
	}

	[Test]
	public async Task Sitemap_PublishedMainPage_IncludedWithWikiUrl()
	{
		var wiki = new InMemoryWikiService(new WikiMarkdigPipeline());
		var created = await wiki.CreateAsync("Getting Started", "# hello", "#1");
		var page = created.AsT0;

		var xml = await GetSitemapXml(MakeController(wiki));

		await Assert.That(xml).Contains("<?xml");
		await Assert.That(xml).Contains($"https://example.com/wiki/main/general/{page.Slug}");
	}

	[Test]
	public async Task Sitemap_UnpublishedPage_Excluded()
	{
		var wiki = new InMemoryWikiService(new WikiMarkdigPipeline());
		var published = (await wiki.CreateAsync("Visible Page", "# visible", "#1")).AsT0;
		var draft = (await wiki.CreateAsync("Secret Draft", "# hidden", "#1")).AsT0;
		await wiki.SetMetadataAsync(draft.Id, null, [], published: false);

		var xml = await GetSitemapXml(MakeController(wiki));

		await Assert.That(xml).Contains($"https://example.com/wiki/main/general/{published.Slug}");
		await Assert.That(xml).DoesNotContain(draft.Slug);
	}

	[Test]
	public async Task Sitemap_Lastmod_UsesIsoDateFormat()
	{
		var wiki = new InMemoryWikiService(new WikiMarkdigPipeline());
		var page = (await wiki.CreateAsync("Dated Page", "# dated", "#1")).AsT0;

		var xml = await GetSitemapXml(MakeController(wiki));

		var expected = $"<lastmod>{page.UpdatedAt:yyyy-MM-dd}</lastmod>";
		await Assert.That(xml).Contains(expected);
		await Assert.That(Regex.IsMatch(xml, @"<lastmod>\d{4}-\d{2}-\d{2}</lastmod>")).IsTrue();
	}

	[Test]
	public async Task Sitemap_CharacterNamespacePage_MapsToCharacterUrl()
	{
		var wiki = new InMemoryWikiService(new WikiMarkdigPipeline());
		var page = (await wiki.CreateAsync("Aria Stormwind", "# bio", "#1", WikiNamespace.Character)).AsT0;

		var xml = await GetSitemapXml(MakeController(wiki));

		await Assert.That(xml).Contains($"https://example.com/character/{page.Slug}");
	}

	[Test]
	public async Task Sitemap_IncludesRootAndWikiEntries()
	{
		var wiki = new InMemoryWikiService(new WikiMarkdigPipeline());

		var xml = await GetSitemapXml(MakeController(wiki));

		await Assert.That(xml).Contains("<loc>https://example.com/</loc>");
		await Assert.That(xml).Contains("<loc>https://example.com/wiki</loc>");
		await Assert.That(xml).Contains("http://www.sitemaps.org/schemas/sitemap/0.9");
	}

	[Test]
	public async Task Robots_ContainsSitemapAndDisallowRules()
	{
		var wiki = new InMemoryWikiService(new WikiMarkdigPipeline());
		var controller = MakeController(wiki);

		var result = controller.Robots();
		var content = (ContentResult)result;
		var text = content.Content!;

		await Assert.That(text).Contains("Sitemap: https://example.com/sitemap.xml");
		await Assert.That(text).Contains("Disallow: /admin/");
		await Assert.That(text).Contains("Disallow: /api/");
		await Assert.That(text).Contains("User-agent: *");
	}
}
