using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Server.Resources;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// The starter wiki pages the setup wizard offers render through the real wiki pipeline, and each files itself in
/// the category tree the Setting and Policies hubs list from.
/// </summary>
public class StarterWikiPagesTests
{
	[Test]
	public async Task EveryPage_Renders_AndPromptsTheAdministrator()
	{
		var pipeline = new WikiMarkdigPipeline();
		foreach (var page in StarterWikiPages.All.Where(p => p.Namespace == WikiNamespace.Main))
		{
			var html = pipeline.RenderToHtml(page.Markdown);
			await Assert.That(html).Contains("<h1").Because($"{page.Title} opens with its title");
			await Assert.That(html).Contains("Fill in:").Because($"{page.Title} tells the administrator what to write");
		}
	}

	[Test]
	public async Task Home_LinksTheFourHubs()
	{
		var html = new WikiMarkdigPipeline().RenderToHtml(StarterWikiPages.Home);

		foreach (var hub in new[] { "Getting Started", "Theme", "Setting", "Policies" })
		{
			await Assert.That(html).Contains($">{hub}</a>");
		}

		// The front page lists recent changes in its own panel below Home.
		await Assert.That(html).DoesNotContain("data-directive=\"recent\"");
	}

	/// <summary>Home opens with the logo, so the front page and the wiki route show it as the banner.</summary>
	[Test]
	public async Task Home_OpensWithTheBannerImage()
	{
		var html = new WikiMarkdigPipeline().RenderToHtml(StarterWikiPages.Home);

		await Assert.That(WikiImages.LeadImageUrl(html)).IsEqualTo("/assets/Logo.svg");
		await Assert.That(html).Contains("<div class=\"center\">");
	}

	[Test]
	public async Task GettingStarted_OpensWithBlueprintArtwork()
	{
		var page = StarterWikiPages.All.Single(p => p is { Title: "Getting Started", Namespace: WikiNamespace.Main });
		var html = new WikiMarkdigPipeline().RenderToHtml(page.Markdown);

		await Assert.That(WikiImages.LeadImageUrl(html)).IsEqualTo("/assets/presets/wiki/getting-started.webp");
		await Assert.That(html).Contains("alt=\"An adventurer and cat study a blueprint map of a fantasy world\"");
	}

	[Test]
	public async Task Theme_OpensWithAtelierArtwork()
	{
		var page = StarterWikiPages.All.Single(p => p is { Title: "Theme", Namespace: WikiNamespace.Main });
		var html = new WikiMarkdigPipeline().RenderToHtml(page.Markdown);

		await Assert.That(WikiImages.LeadImageUrl(html)).IsEqualTo("/assets/presets/wiki/theme.webp");
		await Assert.That(html).Contains("alt=\"Costumes and painted scenery fill a theatrical atelier\"");
	}

	[Test]
	public async Task Setting_ListsItsSubcategories()
	{
		var setting = StarterWikiPages.All.Single(p => p is { Title: "Setting", Namespace: WikiNamespace.Main });
		var html = new WikiMarkdigPipeline().RenderToHtml(setting.Markdown);

		foreach (var category in new[] { "Places", "History", "Factions", "Peoples" })
		{
			await Assert.That(html).Contains($"data-directive=\"category\" data-arg=\"{category}\"");
		}
	}

	/// <summary>Categories are the page's own field; no starter page writes one into its text.</summary>
	[Test]
	public async Task NoPage_CarriesACategoryInItsText()
	{
		foreach (var page in StarterWikiPages.All)
		{
			await Assert.That(page.Markdown).DoesNotContain("[[Category:").Because($"{page.Namespace}:{page.Title}");
		}
	}

	/// <summary>Each hub is filed in its own category, and every category a starter page is filed in is one the set writes.</summary>
	[Test]
	public async Task EveryCategoryUsed_IsWritten()
	{
		var written = StarterWikiPages.All.Where(p => p.Namespace == WikiNamespace.Category).Select(p => p.Title).ToHashSet();

		foreach (var page in StarterWikiPages.All)
		{
			if (page.Namespace == WikiNamespace.Main)
			{
				await Assert.That(page.Categories).IsEquivalentTo([page.Title]);
			}

			foreach (var category in page.Categories)
			{
				await Assert.That(written).Contains(category);
			}
		}
	}

	[Test]
	public async Task OnlyTheThemeSettingAndPolicyHubs_AreProtected()
	{
		var protectedPages = StarterWikiPages.All.Where(p => p.Protect).Select(p => $"{p.Namespace}:{p.Title}").ToList();

		await Assert.That(protectedPages).IsEquivalentTo(["Main:Theme", "Main:Setting", "Main:Policies"]);
	}
}
