using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Client.Pages;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Components.Characters;
using AccountCharacter = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Components.Scenes;

/// <summary>
/// /scenes, /scenes/active and /scenes/{id} in the D1 kit. The archive's filter is the address the
/// sidebar links to (<c>?mine=1</c>, <c>?scheduled=1</c>, <c>?finished=1</c>) rather than tabs; each
/// list is scene cards; a scene's log is a card with its tag chips, and an aside with its details.
/// </summary>
public class ScenesPagesD1Tests : TrackingBunitContext
{
	private readonly CharactersApiFake _api;
	private readonly List<string> _asked = [];

	public ScenesPagesD1Tests()
	{
		(_api, _, _) = CharactersApiFake.Install(this);
		_api.OnRequest = request =>
		{
			lock (_asked) _asked.Add(request.RequestUri!.PathAndQuery);
			return Task.CompletedTask;
		};
		Services.AddSingleton(Substitute.For<ITerminalService>());
		_api.Extra[SceneJson.Recent] = SceneJson.List(
			SceneJson.Scene("S1", "Salt Market at Dusk"),
			SceneJson.Scene("S2", "Lamplighters' Vigil"),
			SceneJson.Scene("S3", "The Long Tide", status: "finished"));
		_api.Extra[SceneJson.Active] = SceneJson.List(
			SceneJson.Scene("S1", "Salt Market at Dusk"),
			SceneJson.Scene("S2", "Lamplighters' Vigil"));
		_api.Extra[SceneJson.Scheduled] = SceneJson.List(
			SceneJson.Scene("S4", "Night of Lamps", status: "new", scheduledFor: DateTimeOffset.UtcNow.AddDays(2).ToUnixTimeMilliseconds()));
		_api.Extra[SceneJson.Finished] = SceneJson.List(SceneJson.Scene("S3", "The Long Tide", status: "finished"));
		_api.Extra[SceneJson.Participant(313)] = SceneJson.List(SceneJson.Scene("S3", "The Long Tide", status: "finished"));
		_api.Extra["/api/scenes/S1"] = SceneJson.Scene("S1", "Salt Market at Dusk", room: "Lower Docks", poses: 2);
		_api.Extra["/api/scenes/S1/poses"] = """
			[{"id":"P1","sceneId":"S1","authorDbref":"#313","authorName":"Ilsa Varn","showAsName":"Ilsa Varn",
			  "originDbref":"#40","originName":"Lower Docks","source":"pose","tags":["combat"],"meta":{},
			  "createdAt":1700000100000,"isDeleted":false,"content":"sets her lantern down","markup":"sets her lantern down",
			  "editCount":1,"lastEditedAt":null,"lastEditorDbref":null,"lastEditorName":null},
			 {"id":"P2","sceneId":"S1","authorDbref":"#314","authorName":"Wren Halloway","showAsName":"Wren Halloway",
			  "originDbref":"#40","originName":"Lower Docks","source":"say","tags":["dialogue"],"meta":{},
			  "createdAt":1700000300000,"isDeleted":false,"content":"says it was the fourth bell","markup":"says it was the fourth bell",
			  "editCount":1,"lastEditedAt":null,"lastEditorDbref":null,"lastEditorName":null}]
			""";
	}

	private BunitNavigationManager Nav => Services.GetRequiredService<BunitNavigationManager>();

	private IRenderedComponent<T> RenderAt<T>(string path, AccountAuthService auth) where T : Microsoft.AspNetCore.Components.IComponent
	{
		Services.AddSingleton(auth);
		Nav.NavigateTo(path);
		return Render<T>();
	}

	private IRenderedComponent<SceneDetail> RenderDetail(string id)
	{
		Services.AddSingleton(CharactersApiFake.Anonymous(this));
		Nav.NavigateTo($"/scenes/{id}");
		return Render<SceneDetail>(p => p.Add(c => c.Id, id));
	}

	private static Task<AccountAuthService> ActingAsync(TrackingBunitContext ctx) =>
		CharactersApiFake.SignedInAsync(ctx, new AccountCharacter(313, 1, "Ilsa Varn", "PLAYER", IsActing: true));

	private static List<string> Titles<T>(IRenderedComponent<T> cut) where T : Microsoft.AspNetCore.Components.IComponent =>
		cut.FindAll(".scene-card .scene-card-title").Select(t => t.TextContent.Trim()).ToList();

	[Test]
	public async Task TheArchive_IsAPlainHeader_AndSceneCards_WithNoTabs()
	{
		var cut = RenderAt<SharpMUSH.Client.Pages.Scenes>("/scenes", CharactersApiFake.Anonymous(this));
		cut.WaitForAssertion(() => cut.Find(".scene-card"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".kit-page-head .kit-page-kicker").TextContent).IsEqualTo("Scenes");
		await Assert.That(cut.Find(".kit-page-head h1").TextContent).IsEqualTo("Scene archive");
		await Assert.That(Titles(cut)).IsEquivalentTo(new[] { "Salt Market at Dusk", "Lamplighters' Vigil", "The Long Tide" });
		await Assert.That(cut.FindAll(".mud-tabs").Count).IsEqualTo(0).Because("the sidebar filters the archive now");
	}

	[Test]
	[Arguments("/scenes?scheduled=1", "Scheduled", "Night of Lamps")]
	[Arguments("/scenes?finished=1", "Finished", "The Long Tide")]
	public async Task TheAddress_PicksTheList(string path, string title, string scene)
	{
		var cut = RenderAt<SharpMUSH.Client.Pages.Scenes>(path, CharactersApiFake.Anonymous(this));
		cut.WaitForAssertion(() => cut.Find(".scene-card"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".kit-page-head h1").TextContent).IsEqualTo(title);
		await Assert.That(Titles(cut)).IsEquivalentTo(new[] { scene });
	}

	[Test]
	public async Task Mine_ListsTheActingCharactersScenes()
	{
		var cut = RenderAt<SharpMUSH.Client.Pages.Scenes>("/scenes?mine=1", await ActingAsync(this));
		cut.WaitForAssertion(() => cut.Find(".scene-card"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".kit-page-head h1").TextContent).IsEqualTo("Your scenes");
		await Assert.That(cut.Find(".kit-page-head .kit-page-desc").TextContent).Contains("Ilsa Varn");
		await Assert.That(Titles(cut)).IsEquivalentTo(new[] { "The Long Tide" });
	}

	[Test]
	public async Task Mine_WithoutACharacter_SaysHowToGetOne_AndAsksTheServerNothing()
	{
		var cut = RenderAt<SharpMUSH.Client.Pages.Scenes>("/scenes?mine=1", CharactersApiFake.Anonymous(this));
		cut.WaitForAssertion(() => cut.Find(".scenes-mine-none"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.FindAll(".scene-card").Count).IsEqualTo(0);
		lock (_asked) _asked.RemoveAll(p => !p.Contains("participant", StringComparison.Ordinal));
		await Assert.That(_asked).IsEmpty();
	}

	[Test]
	public async Task FollowingASidebarLink_ReloadsTheList_WithoutANewPage()
	{
		var cut = RenderAt<SharpMUSH.Client.Pages.Scenes>("/scenes", CharactersApiFake.Anonymous(this));
		cut.WaitForAssertion(() => cut.Find(".scene-card"), TimeSpan.FromSeconds(5));

		Nav.NavigateTo("/scenes?finished=1");

		cut.WaitForAssertion(() =>
		{
			if (Titles(cut).Count != 1) throw new InvalidOperationException($"still {Titles(cut).Count} scenes");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(Titles(cut)).IsEquivalentTo(new[] { "The Long Tide" });
	}

	[Test]
	public async Task LiveCards_OfferJoin_ToAViewerWithACharacter()
	{
		var cut = RenderAt<ScenesActive>("/scenes/active", await ActingAsync(this));
		cut.WaitForAssertion(() => cut.Find(".scene-card"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".kit-page-head h1").TextContent).IsEqualTo("Live now");
		var live = cut.FindAll(".scene-card-actions a.kit-capsule--primary");
		await Assert.That(live.Count).IsEqualTo(2);
		await Assert.That(live[0].TextContent.Trim()).IsEqualTo("Join");
	}

	[Test]
	public async Task LiveCards_OfferWatch_ToAnAnonymousVisitor()
	{
		var cut = RenderAt<ScenesActive>("/scenes/active", CharactersApiFake.Anonymous(this));
		cut.WaitForAssertion(() => cut.Find(".scene-card"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".scene-card-actions a.kit-capsule--primary").TextContent.Trim()).IsEqualTo("Watch Live");
	}

	[Test]
	public async Task ASceneLog_HasAPlainHeader_TheLogInACard_AndItsDetails()
	{
		var cut = RenderDetail("S1");
		cut.WaitForAssertion(() => cut.Find(".scene-log"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".kit-page-head .kit-page-kicker").TextContent).IsEqualTo("Lower Docks");
		await Assert.That(cut.Find(".kit-page-head h1").TextContent).IsEqualTo("Salt Market at Dusk");
		await Assert.That(cut.Find(".kit-page-head .scene-detail-live").TextContent).Contains("LIVE");
		var actions = cut.FindAll(".kit-page-actions a");
		await Assert.That(actions.Select(a => a.GetAttribute("href"))).IsEquivalentTo(new[] { "/scenes/S1/live", "/scenes" });
		await Assert.That(cut.Find(".kit-card .scene-log")).IsNotNull();

		var details = cut.Find(".scene-detail-aside").TextContent;
		await Assert.That(details).Contains("2 poses");
		await Assert.That(details).Contains("Ilsa Varn");
		var cast = cut.FindAll(".scene-detail-cast a.mention");
		await Assert.That(cast.Select(a => a.GetAttribute("href")))
			.IsEquivalentTo(new[] { "/character/Ilsa%20Varn", "/character/Wren%20Halloway" });
	}

	[Test]
	public async Task ASceneLog_FiltersByTag_WithChips()
	{
		var cut = RenderDetail("S1");
		cut.WaitForAssertion(() => cut.Find(".scene-log"), TimeSpan.FromSeconds(5));

		var chips = cut.FindAll(".kit-chips button");
		await Assert.That(chips.Select(c => c.TextContent)).IsEquivalentTo(new[] { "All", "combat", "dialogue" });
		chips.Single(c => c.TextContent == "combat").Click();

		cut.WaitForAssertion(() =>
		{
			if (cut.Markup.Contains("fourth bell", StringComparison.Ordinal)) throw new InvalidOperationException("not filtered yet");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(cut.Markup).Contains("sets her lantern down");
		await Assert.That(cut.Find(".kit-chips button[aria-checked='true']").TextContent).IsEqualTo("combat");
	}

	[Test]
	public async Task AMissingScene_SaysSo_AndLinksBack()
	{
		var cut = RenderDetail("NOPE");
		cut.WaitForAssertion(() => cut.Find(".scene-not-found"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".scene-not-found").TextContent).Contains("Scene not found.");
		await Assert.That(cut.FindAll("a[href='/scenes']").Count).IsEqualTo(1);
	}
}
