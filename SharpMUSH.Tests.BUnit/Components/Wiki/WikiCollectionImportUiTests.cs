using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Client.Components;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Components.Wiki;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components.Wiki;

/// <summary>Explicit real-component acceptance using API output from the pinned full-import probe.</summary>
[Explicit]
public class WikiCollectionImportUiTests : TrackingBunitContext
{
	[Test]
	public async Task FullImportedCollectionHasBoundedNavigationAndUsableTopicJourneys()
	{
		using var culture = CultureScope.For("en");
		var directory = Environment.GetEnvironmentVariable("SHARPMUSH_WIKI_CAPTURE_PATH")
			?? throw new InvalidOperationException("Run Pf2eCollectionImportTests with SHARPMUSH_WIKI_CAPTURE_PATH before this explicit UI acceptance probe.");
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
		var navigation = JsonSerializer.Deserialize<WikiNavigationDto>(await File.ReadAllTextAsync(Path.Join(directory, "navigation.json")), options)!;
		using var pages = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Join(directory, "pages.json")));
		var (api, _) = WikiApiFake.Install(this);
		api.CollectionRows = pages.RootElement.EnumerateArray().Select(row => row.GetRawText()).ToArray();
		api.NavigationResult = navigation;
		Services.AddSingleton(PortalLocalizer.Create());
		var theme = Substitute.For<IThemeService>();
		theme.Current.Returns(ThemeResolver.Resolve(BuiltInThemes.Phosphor));
		Services.AddSingleton(theme);
		var nav = Services.GetRequiredService<BunitNavigationManager>();
		nav.NavigateTo("/wiki/collections/package%3Apf2e-remaster");
		var browser = Render<ThemeProvider>(p => p.AddChildContent<WikiCollectionBrowser>(b => b.Add(x => x.Collection, navigation.Collection)));
		browser.WaitForAssertion(() => { if (browser.FindAll(".wiki-collection-page").Count != 50) throw new InvalidOperationException("Full import page missing."); });
		await Assert.That(navigation.Total).IsEqualTo(4711);
		await Assert.That(browser.Find(".wiki-collection-range").TextContent).Contains("4711");
		await File.WriteAllTextAsync(Path.Join(directory, "collection.html"), browser.Markup);
		await browser.Find(".wiki-collection-search input").InputAsync("Blinded");
		await browser.Find(".wiki-collection-search").SubmitAsync();
		await Assert.That(browser.FindAll(".wiki-collection-page").Any(row => row.GetAttribute("href") == "/wiki/main/blinded")).IsTrue();
		await browser.Find(".wiki-collection-search input").InputAsync("");
		await browser.Find(".wiki-collection-search").SubmitAsync();
		var ancestry = browser.FindAll(".wiki-collection-category-list button").Single(row => row.TextContent.StartsWith("Ancestries", StringComparison.Ordinal));
		await ancestry.ClickAsync(new());
		await Assert.That(browser.FindAll(".wiki-collection-page").Any(row => row.GetAttribute("href") == "/wiki/main/dwarf")).IsTrue();
		await File.WriteAllTextAsync(Path.Join(directory, "ancestries.html"), browser.Markup);
		using var sidebar = Render<ThemeProvider>(p => p.AddChildContent<WikiSidebar>());
		sidebar.WaitForAssertion(() => sidebar.Find(".wiki-side-collections a"));
		await Assert.That(sidebar.FindAll(".wiki-side-cats a").Count).IsLessThanOrEqualTo(5);
		await File.WriteAllTextAsync(Path.Join(directory, "sidebar.html"), sidebar.Markup);
		browser.Dispose();
		api.NavigationResult = JsonSerializer.Deserialize<WikiNavigationDto>(await File.ReadAllTextAsync(Path.Join(directory, "home-navigation.json")), options)!;
		api.CollectionRows = [];
		nav.NavigateTo("/wiki");
		using var home = Render<ThemeProvider>(p => p.AddChildContent<WikiIndexWidget>());
		home.WaitForAssertion(() => home.Find(".wiki-collection-entry"));
		await Assert.That(home.FindAll(".wiki-collection-entry").Count).IsEqualTo(1);
		await Assert.That(home.FindAll(".wiki-cat-name").Any(row => row.TextContent == "Alchemist Feats")).IsFalse();
		await File.WriteAllTextAsync(Path.Join(directory, "home.html"), home.Markup);
		await File.WriteAllTextAsync(Path.Join(directory, "theme-parts.json"), JsonSerializer.Serialize(ThemeResolver.Resolve(BuiltInThemes.Phosphor).Parts));
	}
}
