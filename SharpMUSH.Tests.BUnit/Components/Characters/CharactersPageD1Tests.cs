using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Pages;

namespace SharpMUSH.Tests.BUnit.Components.Characters;

/// <summary>
/// /characters in the D1 kit: the plain header with the count, category chips, and the directory as
/// portrait tiles. The address filters it: <c>?q=</c> by name, <c>?online=1</c> to who is online,
/// <c>?new=1</c> to the last fortnight's characters.
/// </summary>
public class CharactersPageD1Tests : TrackingBunitContext
{
	private readonly BunitNavigationManager _nav;

	public CharactersPageD1Tests()
	{
		CharactersApiFake.Install(this);
		Services.AddSingleton(CharactersApiFake.Anonymous(this));
		_nav = Services.GetRequiredService<BunitNavigationManager>();
	}

	private IRenderedComponent<SharpMUSH.Client.Pages.Characters> RenderAt(string path)
	{
		_nav.NavigateTo(path);
		var cut = Render<SharpMUSH.Client.Pages.Characters>();
		cut.WaitForAssertion(() => cut.Find(".char-grid .kit-portrait, .char-empty"), TimeSpan.FromSeconds(5));
		return cut;
	}

	private static List<string> Names(IRenderedComponent<SharpMUSH.Client.Pages.Characters> cut) =>
		cut.FindAll(".char-grid .kit-portrait .kit-portrait-label").Select(e => e.TextContent).ToList();

	[Test]
	public async Task TheDirectory_IsAPlainHeaderAndPortraitTiles()
	{
		var cut = RenderAt("/characters");
		await Assert.That(cut.Find(".kit-page-head h1").TextContent).IsEqualTo("Characters");
		await Assert.That(Names(cut).Count).IsEqualTo(5);
		var tomas = cut.FindAll(".char-grid a.kit-portrait").Single(a => a.TextContent.Contains("Tomas Reyes"));
		await Assert.That(tomas.GetAttribute("href")).IsEqualTo("/character/Tomas%20Reyes");
		await Assert.That(tomas.QuerySelector("img")!.GetAttribute("src")).IsEqualTo("/api/wiki-assets/t/tomas.jpg");
	}

	[Test]
	public async Task CategoryChips_Filter()
	{
		var cut = RenderAt("/characters");
		cut.FindAll(".kit-chips button").Single(b => b.TextContent == "Guard").Click();
		await Assert.That(Names(cut)).IsEquivalentTo(new[] { "Dace Kellan", "Wren Halloway" });
	}

	[Test]
	public async Task TheAddress_FiltersByNameOnlineAndNew()
	{
		await Assert.That(Names(RenderAt("/characters?q=ilsa"))).IsEquivalentTo(new[] { "Ilsa Varn" });
		await Assert.That(Names(RenderAt("/characters?online=1"))).IsEquivalentTo(new[] { "Tomas Reyes", "Wren Halloway" });
		await Assert.That(Names(RenderAt("/characters?new=1"))).IsEquivalentTo(new[] { "Magister Oake", "Wren Halloway" });
	}
}
