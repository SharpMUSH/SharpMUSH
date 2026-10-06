using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Models.Widgets;
using SharpMUSH.Client.Pages;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal.Widgets;
using SharpMUSH.Tests.BUnit.Components.Characters;
using SharpMUSH.Tests.BUnit.Components.Scenes;
using AccountCharacter = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// The widgets and pages that show what the server lets the acting character see — a scene list
/// holds only the private scenes that character may see, a profile's live scene and partners are
/// counted from them — read it once and kept it through a switch of character: the previous
/// character's private scenes stayed on screen and the new one's were missing. Each now reads again
/// when the acting character changes, and a scene list also when this tab starts a scene.
/// </summary>
/// <remarks>
/// The fake answers by address alone, so each test plays the server by changing the answer and then
/// switching: what is on screen afterwards is only right if the component asked again.
/// </remarks>
public class ViewerScopedReloadTests : TrackingBunitContext
{
	private readonly CharactersApiFake _api;
	private AccountAuthService _auth = default!;

	public ViewerScopedReloadTests()
	{
		IHttpClientFactory factory;
		(_api, factory, _) = CharactersApiFake.Install(this);
		Services.AddSingleton(sp => new WikiService(factory, NullLogger<WikiService>.Instance));
		Services.AddSingleton(sp => new GameCommandService(factory));
		_api.Extra["/api/wiki/recent?count=10"] = "[]";
		_api.Extra[SceneJson.Active] = SceneJson.List(SceneJson.Scene("S1", "Salt Market at Dusk"));
	}

	private async Task SignInAsync()
	{
		_auth = await CharactersApiFake.SignedInAsync(this,
			new AccountCharacter(313, 1, "Ilsa Varn", "PLAYER", IsActing: true),
			new AccountCharacter(314, 1, "Wren Halloway", "PLAYER"));
		Services.AddSingleton(_auth);
	}

	private void SwitchToWren() => _auth.SetActiveCharacter(_auth.Characters.Single(c => c.Name == "Wren Halloway"));

	private static void WaitFor<T>(IRenderedComponent<T> cut, string text) where T : Microsoft.AspNetCore.Components.IComponent =>
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains(text, StringComparison.Ordinal)) throw new InvalidOperationException($"no '{text}' yet");
		}, TimeSpan.FromSeconds(5));

	private const string WrensPrivateScene = "Wren's Private Errand";

	private static string TwoLiveScenes() => SceneJson.List(
		SceneJson.Scene("S1", "Salt Market at Dusk"),
		SceneJson.Scene("S5", WrensPrivateScene, isPublic: false));

	[Test]
	public async Task LiveNowWidget_SwitchingCharacter_ListsWhatTheNewCharacterMaySee()
	{
		await SignInAsync();
		var cut = Render<ActiveSceneWidget>();
		WaitFor(cut, "Salt Market at Dusk");
		_api.Extra[SceneJson.Active] = TwoLiveScenes();

		SwitchToWren();

		WaitFor(cut, WrensPrivateScene);
		await Assert.That(cut.FindAll("a[href='/scenes/S5/live']").Count).IsEqualTo(1);
	}

	[Test]
	public async Task LiveNowWidget_AStartedScene_IsListed()
	{
		await SignInAsync();
		var cut = Render<ActiveSceneWidget>();
		WaitFor(cut, "Salt Market at Dusk");
		_api.Extra[SceneJson.Active] = TwoLiveScenes();

		Services.GetRequiredService<SceneService>().ReportChanged();

		WaitFor(cut, WrensPrivateScene);
		await Assert.That(cut.FindAll("a[href='/scenes/S5/live']").Count).IsEqualTo(1);
	}

	[Test]
	public async Task StatsWidget_SwitchingCharacter_RecountsTheActiveScenes()
	{
		await SignInAsync();
		var cut = Render<StatsWidget>();
		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".stats-tile-value")[1].TextContent != "1") throw new InvalidOperationException("not counted yet");
		}, TimeSpan.FromSeconds(5));
		_api.Extra[SceneJson.Active] = TwoLiveScenes();

		SwitchToWren();

		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".stats-tile-value")[1].TextContent != "2") throw new InvalidOperationException("still the previous character's count");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".stats-tile-label")[1].TextContent).IsEqualTo("Active Scenes");
	}

	private IRenderedComponent<T> InProfile<T>() where T : Microsoft.AspNetCore.Components.IComponent
	{
		var cut = Render<WikiBodyBiographyTests.CascadingWrapper>(p => p.AddChildContent<T>()
			.Add(x => x.Context, new ProfilePageContext("Tomas Reyes", false)));
		return cut.FindComponent<T>();
	}

	[Test]
	public async Task RecentScenesWidget_SwitchingCharacter_ListsWhatTheNewCharacterMaySee()
	{
		await SignInAsync();
		_api.Extra["/api/scenes?participant=%23312&count=3"] = SceneJson.List(SceneJson.Scene("S1", "Salt Market at Dusk"));
		var cut = InProfile<RecentScenesWidget>();
		WaitFor(cut, "Salt Market at Dusk");
		_api.Extra["/api/scenes?participant=%23312&count=3"] = TwoLiveScenes();

		SwitchToWren();

		WaitFor(cut, WrensPrivateScene);
		await Assert.That(cut.FindAll(".recent-scenes-rows a.kit-row").Count).IsEqualTo(2);
	}

	[Test]
	public async Task OftenPlaysWithWidget_SwitchingCharacter_CountsFromWhatTheNewCharacterMaySee()
	{
		await SignInAsync();
		const string partners = "/api/scenes/partners?participant=%23312&count=4";
		_api.Extra[partners] = """[{"dbref":"#313","name":"Ilsa Varn","scenes":4}]""";
		var cut = InProfile<OftenPlaysWithWidget>();
		WaitFor(cut, "Ilsa Varn");
		_api.Extra[partners] = """[{"dbref":"#313","name":"Ilsa Varn","scenes":4},{"dbref":"#314","name":"Wren Halloway","scenes":1}]""";

		SwitchToWren();

		WaitFor(cut, "Wren Halloway");
		await Assert.That(cut.FindAll("a.kit-portrait").Count).IsEqualTo(2);
	}

	[Test]
	public async Task ProfilePage_SwitchingCharacter_ShowsTheLiveSceneTheNewCharacterMaySee()
	{
		await SignInAsync();
		var layouts = Substitute.For<ILayoutService>();
		layouts.GetLayoutAsync(Arg.Any<string>()).Returns(new LayoutConfiguration(new Dictionary<WidgetZone, List<WidgetPlacement>>(), new LayoutSettings(false, false)));
		Services.AddSingleton(layouts);
		Services.AddSingleton<IWidgetRegistry>(new WidgetRegistry());
		_api.Extra["/http/profile?objid=%23312%3A1"] = """{"character":"Tomas Reyes","objid":"#312:1","dbref":"#312","fields":{}}""";
		_api.Extra["/api/scenes?participant=%23312&count=5"] = "[]";
		var cut = Render<CharacterProfile>(p => p.Add(x => x.Name, "Tomas Reyes"));
		cut.WaitForAssertion(() => cut.Find(".kit-banner"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".char-profile-pill--scene").Count).IsEqualTo(0);
		_api.Extra["/api/scenes?participant=%23312&count=5"] = SceneJson.List(SceneJson.Scene("S5", WrensPrivateScene, isPublic: false));

		SwitchToWren();

		cut.WaitForAssertion(() => cut.Find(".char-profile-pill--scene"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".char-profile-pill--scene").TextContent).Contains(WrensPrivateScene);
	}

	/// <summary>A private scene's log goes from the screen at the switch, not when the new character's read answers.</summary>
	[Test]
	public async Task SceneLog_SwitchingCharacter_PutsThePreviousLogAwayAtOnce()
	{
		await SignInAsync();
		_api.Extra["/api/scenes/S5"] = SceneJson.Scene("S5", WrensPrivateScene, isPublic: false);
		_api.Extra["/api/scenes/S5/poses"] = "[]";
		var cut = Render<SceneDetail>(p => p.Add(c => c.Id, "S5"));
		WaitFor(cut, WrensPrivateScene);
		var gate = new TaskCompletionSource();
		_api.OnRequest = request => request.RequestUri!.AbsolutePath == "/api/scenes/S5" ? gate.Task : Task.CompletedTask;

		SwitchToWren();

		cut.WaitForAssertion(() =>
		{
			if (cut.Markup.Contains(WrensPrivateScene, StringComparison.Ordinal)) throw new InvalidOperationException("the previous character's log is still on screen");
		}, TimeSpan.FromSeconds(5));
		gate.SetResult();
		WaitFor(cut, WrensPrivateScene);
	}

	[Test]
	public async Task SceneLog_SwitchingToACharacterWhoMayNotSeeIt_SaysItIsNotFound()
	{
		await SignInAsync();
		_api.Extra["/api/scenes/S5"] = SceneJson.Scene("S5", WrensPrivateScene, isPublic: false);
		_api.Extra["/api/scenes/S5/poses"] = "[]";
		var cut = Render<SceneDetail>(p => p.Add(c => c.Id, "S5"));
		WaitFor(cut, WrensPrivateScene);
		_api.Extra.Remove("/api/scenes/S5");

		SwitchToWren();

		cut.WaitForAssertion(() => cut.Find(".scene-not-found"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Markup).DoesNotContain(WrensPrivateScene);
	}
}
