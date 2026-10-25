using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Tests.Authentication;
using SharpMUSH.Configuration.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services;
using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// Unit tests for <see cref="SeoController"/>: the sitemap must include only published
/// wiki pages with namespace-aware URLs and yyyy-MM-dd lastmod values, and robots.txt
/// must advertise the sitemap and block admin paths.
/// </summary>
public class SeoControllerTests
{
	private static SeoController MakeController(WikiStoreService wiki, SitemapCache? sitemap = null)
	{
		// A real localization service over the same storage, not a substitute: a double would return an
		// empty locale list for every page and the sitemap's alternates would never be exercised.
		var monitor = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();
		monitor.CurrentValue.Returns(TestSharpMushOptions.Create());
		var localization = new WikiLocalizationService(
			wiki, new WikiLocaleResolver(monitor), NullLogger<WikiLocalizationService>.Instance);
		var access = new WikiAccessService(wiki, InMemoryRoleRegistry.Seeded(), new PermissionResolver());
		var controller = new SeoController(wiki, localization, access, sitemap ?? new SitemapCache(new Clock()), NullLogger<SeoController>.Instance);

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
		var wiki = InMemoryWikiStore.CreateService();
		var page = (await wiki.CreateAsync("Getting Started", "# hello", "#1")).Expect<WikiPage>();

		var xml = await GetSitemapXml(MakeController(wiki));

		await Assert.That(xml).Contains("<?xml");
		await Assert.That(xml).Contains($"https://example.com/wiki/main/{page.Slug}");
	}

	[Test]
	public async Task Sitemap_UnpublishedPage_Excluded()
	{
		var wiki = InMemoryWikiStore.CreateService();
		var published = (await wiki.CreateAsync("Visible Page", "# visible", "#1")).Expect<WikiPage>();
		var draft = (await wiki.CreateAsync("Secret Draft", "# hidden", "#1")).Expect<WikiPage>();
		await wiki.SetMetadataAsync(draft.Id, [], published: false);

		var xml = await GetSitemapXml(MakeController(wiki));

		await Assert.That(xml).Contains($"https://example.com/wiki/main/{published.Slug}");
		await Assert.That(xml).DoesNotContain(draft.Slug);
	}

	[Test]
	public async Task Sitemap_Lastmod_UsesIsoDateFormat()
	{
		var wiki = InMemoryWikiStore.CreateService();
		var page = (await wiki.CreateAsync("Dated Page", "# dated", "#1")).Expect<WikiPage>();

		var xml = await GetSitemapXml(MakeController(wiki));

		var expected = $"<lastmod>{page.UpdatedAt:yyyy-MM-dd}</lastmod>";
		await Assert.That(xml).Contains(expected);
		await Assert.That(Regex.IsMatch(xml, @"<lastmod>\d{4}-\d{2}-\d{2}</lastmod>")).IsTrue();
	}

	[Test]
	public async Task Sitemap_CharacterNamespacePage_MapsToCharacterUrl()
	{
		var wiki = InMemoryWikiStore.CreateService();
		var page = (await wiki.CreateAsync("Aria Stormwind", "# bio", "#1", WikiNamespace.Character)).Expect<WikiPage>();

		var xml = await GetSitemapXml(MakeController(wiki));

		await Assert.That(xml).Contains($"https://example.com/character/{page.Slug}");
	}

	[Test]
	public async Task Sitemap_IncludesRootAndWikiEntries()
	{
		var wiki = InMemoryWikiStore.CreateService();

		var xml = await GetSitemapXml(MakeController(wiki));

		await Assert.That(xml).Contains("<loc>https://example.com/</loc>");
		await Assert.That(xml).Contains("<loc>https://example.com/wiki</loc>");
		await Assert.That(xml).Contains("http://www.sitemaps.org/schemas/sitemap/0.9");
	}

	[Test]
	public async Task Robots_ContainsSitemapAndDisallowRules()
	{
		var wiki = InMemoryWikiStore.CreateService();
		var controller = MakeController(wiki);

		var result = controller.Robots();
		var content = (ContentResult)result;
		var text = content.Content!;

		await Assert.That(text).Contains("Sitemap: https://example.com/sitemap.xml");
		await Assert.That(text).Contains("Disallow: /admin/");
		await Assert.That(text).Contains("Disallow: /api/");
		await Assert.That(text).Contains("User-agent: *");
	}

	private sealed class Clock : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
		public override DateTimeOffset GetUtcNow() => Now;
	}

	/// <summary>
	/// A crawler fetching the sitemap again within <see cref="SitemapCache.Lifetime"/> gets the list already built,
	/// not another scan of every page; once it has passed, the list is built again.
	/// </summary>
	[Test]
	public async Task Sitemap_IsBuiltOncePerLifetime()
	{
		var wiki = InMemoryWikiStore.CreateService();
		var clock = new Clock();
		var sitemap = new SitemapCache(clock);
		var first = (await wiki.CreateAsync("First Page", "# one", "#1")).Expect<WikiPage>();
		await GetSitemapXml(MakeController(wiki, sitemap));

		var later = (await wiki.CreateAsync("Later Page", "# two", "#1")).Expect<WikiPage>();
		var cached = await GetSitemapXml(MakeController(wiki, sitemap));
		await Assert.That(cached).Contains(first.Slug);
		await Assert.That(cached).DoesNotContain(later.Slug);

		clock.Now += SitemapCache.Lifetime;
		await Assert.That(await GetSitemapXml(MakeController(wiki, sitemap))).Contains(later.Slug);
	}

	/// <summary>The cached list holds paths, so each request's own host is what its URLs carry.</summary>
	[Test]
	public async Task Sitemap_FromTheCache_UsesTheRequestsHost()
	{
		var wiki = InMemoryWikiStore.CreateService();
		var sitemap = new SitemapCache(new Clock());
		var page = (await wiki.CreateAsync("Hosted Page", "# hosted", "#1")).Expect<WikiPage>();
		await GetSitemapXml(MakeController(wiki, sitemap));

		var other = MakeController(wiki, sitemap);
		other.HttpContext.Request.Host = new HostString("mirror.example.org");

		await Assert.That(await GetSitemapXml(other)).Contains($"https://mirror.example.org/wiki/main/{page.Slug}");
	}

	/// <summary>The sitemap reads pages in batches; every batch's pages are listed, not only the first.</summary>
	[Test]
	public async Task Sitemap_ListsPagesPastTheFirstBatch()
	{
		var wiki = InMemoryWikiStore.CreateService();
		var slugs = new List<string>();
		for (var i = 0; i < 501; i++)
			slugs.Add((await wiki.CreateAsync($"Batch Page {i:D3}", "# body", "#1")).Expect<WikiPage>().Slug);

		var xml = await GetSitemapXml(MakeController(wiki));

		foreach (var slug in slugs)
			await Assert.That(xml).Contains($"/wiki/main/{slug}<");
	}
}
