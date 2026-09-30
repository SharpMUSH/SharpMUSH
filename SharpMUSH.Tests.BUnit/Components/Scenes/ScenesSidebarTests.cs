using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Components.Scenes;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Components.Characters;
using AccountCharacter = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Components.Scenes;

/// <summary>
/// The Scenes section sidebar: "Scenes · ● N live · N recent", Browse (All scenes, Live now, Your scenes
/// for a viewer with an acting character, Scheduled, Finished), and the running scenes as image rows
/// into their live view. Collapsed keeps the icons and the scene leads.
/// </summary>
public class ScenesSidebarTests : TrackingBunitContext
{
	private readonly CharactersApiFake _api;

	public ScenesSidebarTests()
	{
		(_api, _, _) = CharactersApiFake.Install(this);
		_api.Extra[SceneJson.Recent] = SceneJson.List(
			SceneJson.Scene("S1", "Salt Market at Dusk", image: "/api/wiki-assets/s/salt.jpg"),
			SceneJson.Scene("S2", "Lamplighters' Vigil"),
			SceneJson.Scene("S3", "The Long Tide", status: "finished"));
		_api.Extra[SceneJson.Active] = SceneJson.List(
			SceneJson.Scene("S1", "Salt Market at Dusk", image: "/api/wiki-assets/s/salt.jpg"),
			SceneJson.Scene("S2", "Lamplighters' Vigil"));
		_api.Extra[SceneJson.Participant(313)] = SceneJson.List(SceneJson.Scene("S3", "The Long Tide", status: "finished"));
	}

	private BunitNavigationManager Nav => Services.GetRequiredService<BunitNavigationManager>();

	private IRenderedComponent<ScenesSidebar> RenderAt(string path, AccountAuthService auth, bool collapsed = false)
	{
		Services.AddSingleton(auth);
		Nav.NavigateTo(path);
		var cut = Render<ScenesSidebar>(p => p.Add(x => x.Collapsed, collapsed));
		cut.WaitForAssertion(() => cut.Find(".scenes-side-live a.kit-row"), TimeSpan.FromSeconds(5));
		return cut;
	}

	private static Task<AccountAuthService> ActingAsync(TrackingBunitContext ctx) =>
		CharactersApiFake.SignedInAsync(ctx, new AccountCharacter(313, 1, "Ilsa Varn", "PLAYER", IsActing: true));

	[Test]
	public async Task Header_CountsLiveAndRecent()
	{
		var cut = RenderAt("/scenes", CharactersApiFake.Anonymous(this));
		var sub = cut.Find(".kit-side-sub").TextContent;
		await Assert.That(sub).Contains("2 live");
		await Assert.That(sub).Contains("3 recent");
		await Assert.That(cut.Find(".kit-side-sub .scenes-side-dot")).IsNotNull();
	}

	[Test]
	public async Task Browse_LinksEveryListTheApiServes_WithTheLiveCount()
	{
		var cut = RenderAt("/scenes", CharactersApiFake.Anonymous(this));
		var rows = cut.FindAll(".scenes-side-browse a.kit-row");
		await Assert.That(rows.Select(r => r.GetAttribute("href")))
			.IsEquivalentTo(new[] { "/scenes", "/scenes/active", "/scenes?scheduled=1", "/scenes?finished=1" });
		await Assert.That(rows[1].QuerySelector(".kit-row-count")!.TextContent).IsEqualTo("2");
		await Assert.That(rows[0].GetAttribute("aria-current")).IsEqualTo("page");
		await Assert.That(rows.Skip(1).All(r => r.GetAttribute("aria-current") is null)).IsTrue();
	}

	[Test]
	[Arguments("/scenes/active", "/scenes/active")]
	[Arguments("/scenes?scheduled=1", "/scenes?scheduled=1")]
	[Arguments("/scenes?finished=1", "/scenes?finished=1")]
	public async Task TheCurrentRow_FollowsTheAddress(string path, string current)
	{
		var cut = RenderAt(path, CharactersApiFake.Anonymous(this));
		var marked = cut.FindAll(".scenes-side-browse a.kit-row[aria-current='page']");
		await Assert.That(marked.Count).IsEqualTo(1);
		await Assert.That(marked[0].GetAttribute("href")).IsEqualTo(current);
	}

	[Test]
	public async Task YourScenes_IsNotOffered_ToAnAnonymousVisitor()
	{
		var cut = RenderAt("/scenes", CharactersApiFake.Anonymous(this));
		await Assert.That(cut.FindAll(".scenes-side-browse a[href='/scenes?mine=1']").Count).IsEqualTo(0);
	}

	[Test]
	public async Task YourScenes_IsNotOffered_ToAnAccountActingAsNobody()
	{
		var signedIn = await CharactersApiFake.SignedInAsync(this, new AccountCharacter(313, 1, "Ilsa Varn", "PLAYER"));
		var cut = RenderAt("/scenes", signedIn);
		await Assert.That(cut.FindAll(".scenes-side-browse a[href='/scenes?mine=1']").Count).IsEqualTo(0);
	}

	[Test]
	public async Task YourScenes_CountsTheActingCharactersScenes_AndIsCurrentOnMine()
	{
		var cut = RenderAt("/scenes?mine=1", await ActingAsync(this));
		cut.WaitForAssertion(() => cut.Find(".scenes-side-browse a[href='/scenes?mine=1'] .kit-row-count"), TimeSpan.FromSeconds(5));
		var mine = cut.Find(".scenes-side-browse a[href='/scenes?mine=1']");
		await Assert.That(mine.QuerySelector(".kit-row-count")!.TextContent).IsEqualTo("1");
		await Assert.That(mine.GetAttribute("aria-current")).IsEqualTo("page");
		await Assert.That(cut.Find(".scenes-side-browse a[href='/scenes']").GetAttribute("aria-current")).IsNull();
	}

	[Test]
	public async Task LiveNow_ListsTheRunningScenes_IntoTheirLiveView()
	{
		var cut = RenderAt("/scenes/S2", CharactersApiFake.Anonymous(this));
		var rows = cut.FindAll(".scenes-side-live a.kit-row");
		await Assert.That(rows.Count).IsEqualTo(2);
		await Assert.That(rows[0].GetAttribute("href")).IsEqualTo("/scenes/S1/live");
		await Assert.That(rows[0].QuerySelector("img.kit-row-img")!.GetAttribute("src")).IsEqualTo("/api/wiki-assets/s/salt.jpg");
		await Assert.That(rows[1].QuerySelector(".kit-row-fallback svg")).IsNotNull().Because("scenes have no image yet: the icon");
		await Assert.That(rows[1].GetAttribute("aria-current")).IsEqualTo("page").Because("reading that scene's log");
		await Assert.That(rows[0].GetAttribute("aria-current")).IsNull();
	}

	[Test]
	public async Task LiveNow_ShowsAtMostFive()
	{
		_api.Extra[SceneJson.Active] = SceneJson.List(Enumerable.Range(1, 7).Select(i => SceneJson.Scene($"L{i}", $"Scene {i}")).ToArray());
		var cut = RenderAt("/scenes", CharactersApiFake.Anonymous(this));
		await Assert.That(cut.FindAll(".scenes-side-live a.kit-row").Count).IsEqualTo(5);
		await Assert.That(cut.Find(".scenes-side-browse a[href='/scenes/active'] .kit-row-count").TextContent).IsEqualTo("7");
	}

	[Test]
	public async Task Collapsed_KeepsTheIconsAndSceneLeads_Only()
	{
		var cut = RenderAt("/scenes", await ActingAsync(this), collapsed: true);
		await Assert.That(cut.FindAll(".kit-side-head").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".kit-section-label").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".scenes-side-browse a.kit-row--collapsed").Count).IsGreaterThanOrEqualTo(4);
		await Assert.That(cut.FindAll(".scenes-side-live a.kit-row--collapsed").Count).IsEqualTo(2);
		await Assert.That(cut.FindAll(".kit-row-label").Count).IsEqualTo(0);
	}
}
