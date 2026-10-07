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
		Services.AddSingleton(sp => new GameCommandService(sp.GetRequiredService<IHttpClientFactory>()));
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
	public async Task Mine_ASlowAnswerForThePreviousCharacter_DoesNotReplaceTheCurrentOnes()
	{
		var gate = new TaskCompletionSource();
		_api.Extra[SceneJson.Participant(314)] = SceneJson.List(SceneJson.Scene("S2", "Lamplighters' Vigil"));
		_api.OnRequest = request => request.RequestUri!.PathAndQuery == SceneJson.Participant(313) ? gate.Task : Task.CompletedTask;
		var auth = await CharactersApiFake.SignedInAsync(this,
			new AccountCharacter(313, 1, "Ilsa Varn", "PLAYER", IsActing: true),
			new AccountCharacter(314, 1, "Wren Halloway", "PLAYER"));

		var cut = RenderAt<SharpMUSH.Client.Pages.Scenes>("/scenes?mine=1", auth);
		auth.SetActiveCharacter(auth.Characters.Single(c => c.Name == "Wren Halloway"));
		cut.WaitForAssertion(() => cut.Find(".scene-card"), TimeSpan.FromSeconds(5));
		gate.SetResult();
		await Task.Delay(100);

		cut.WaitForAssertion(() => cut.Find(".scene-card"), TimeSpan.FromSeconds(5));
		await Assert.That(Titles(cut)).IsEquivalentTo(new[] { "Lamplighters' Vigil" })
			.Because("Ilsa's scenes answered last, but the page now acts as Wren");
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

	/// <summary>
	/// Switching character on /scenes/active lists what the new character may see. The page read the
	/// live list once, so the previous character's private scenes stayed and the new one's were missing.
	/// </summary>
	[Test]
	public async Task LiveCards_SwitchingCharacter_ListsWhatTheNewCharacterMaySee()
	{
		var auth = await CharactersApiFake.SignedInAsync(this,
			new AccountCharacter(313, 1, "Ilsa Varn", "PLAYER", IsActing: true),
			new AccountCharacter(314, 1, "Wren Halloway", "PLAYER"));
		var cut = RenderAt<ScenesActive>("/scenes/active", auth);
		cut.WaitForAssertion(() => cut.Find(".scene-card"), TimeSpan.FromSeconds(5));
		_api.Extra[SceneJson.Active] = SceneJson.List(SceneJson.Scene("S5", "Wren's Private Errand"));

		auth.SetActiveCharacter(auth.Characters.Single(c => c.Name == "Wren Halloway"));

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Wren's Private Errand", StringComparison.Ordinal)) throw new InvalidOperationException("still the previous character's list");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".scene-card").Count).IsEqualTo(1);
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
		cut.WaitForAssertion(() => cut.Find(".scene-detail-log .story-row"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".kit-page-head .kit-page-kicker").TextContent).IsEqualTo("Lower Docks");
		await Assert.That(cut.Find(".kit-page-head h1").TextContent).IsEqualTo("Salt Market at Dusk");
		await Assert.That(cut.Find(".kit-page-head .scene-detail-live").TextContent).Contains("LIVE");
		var actions = cut.FindAll(".kit-page-actions a");
		await Assert.That(actions.Select(a => a.GetAttribute("href"))).IsEquivalentTo(new[] { "/scenes/S1/live", "/scenes" });
		await Assert.That(cut.FindAll(".kit-card .scene-detail-log .story-row").Count).IsEqualTo(2);

		var details = cut.Find(".scene-detail-aside").TextContent;
		await Assert.That(details).Contains("2 poses");
		await Assert.That(details).Contains("Ilsa Varn");
		var cast = cut.FindAll(".scene-detail-cast a.mention");
		await Assert.That(cast.Select(a => a.GetAttribute("href")))
			.IsEquivalentTo(new[] { "/character/Ilsa%20Varn", "/character/Wren%20Halloway" });
	}

	/// <summary>
	/// On a phone the details and cast come before the log, folded into one line naming the cast, so a long
	/// log does not put them out of reach; the line unfolds them.
	/// </summary>
	[Test]
	public async Task TheDetailsAndCast_FoldIntoOneLine_ThatNamesTheCast_AndUnfolds()
	{
		var cut = RenderDetail("S1");
		cut.WaitForAssertion(() => cut.Find(".scene-detail-log .story-row"), TimeSpan.FromSeconds(5));

		var toggle = cut.Find(".scene-detail-about-toggle");
		await Assert.That(toggle.QuerySelector(".scene-detail-about-cast")!.TextContent).IsEqualTo("Ilsa Varn, Wren Halloway");
		await Assert.That(toggle.GetAttribute("aria-expanded")).IsEqualTo("false");
		await Assert.That(cut.Find(".scene-detail-aside").ClassList).DoesNotContain("scene-detail-aside-open");

		await toggle.ClickAsync();

		await Assert.That(cut.Find(".scene-detail-about-toggle").GetAttribute("aria-expanded")).IsEqualTo("true");
		await Assert.That(cut.Find(".scene-detail-aside").ClassList).Contains("scene-detail-aside-open");
	}

	/// <summary>
	/// A scene that has not run has no start, length or log to show: its page leads with when it is due and
	/// who hosts it, and the log's place says when it begins.
	/// </summary>
	[Test]
	public async Task AScheduledScene_ShowsWhenItStarts_AndItsHost_NotAnEmptyLog()
	{
		var due = DateTimeOffset.UtcNow.AddDays(3);
		_api.Extra["/api/scenes/S6"] = SceneJson.Scene("S6", "Harbor Watch", status: "scheduled", room: "", poses: 0,
			scheduledFor: due.ToUnixTimeMilliseconds());
		_api.Extra["/api/scenes/S6/poses"] = "[]";
		var cut = RenderDetail("S6");
		cut.WaitForAssertion(() => cut.Find(".scene-detail-not-started"), TimeSpan.FromSeconds(5));

		var when = due.ToLocalTime().ToString("MMM d, yyyy HH:mm");
		await Assert.That(cut.Find(".scene-detail-not-started").TextContent).Contains(when);
		await Assert.That(cut.Find(".kit-page-head .scene-detail-upcoming")).IsNotNull();
		await Assert.That(cut.Find(".kit-page-head .scene-detail-due").TextContent).IsEqualTo(when);
		var details = cut.Find(".scene-detail-aside").TextContent;
		await Assert.That(details).Contains("Host");
		await Assert.That(details).DoesNotContain("0 poses");
		await Assert.That(cut.Find(".kit-page-head").TextContent).DoesNotContain("0 poses");
	}

	/// <summary>An upcoming scene's card says when it is due, with no room it does not have and no "0 poses".</summary>
	[Test]
	public async Task AnUpcomingCard_LeavesOutTheRoomAndPoses_AndShowsItsPitch()
	{
		_api.Extra[SceneJson.Recent] = SceneJson.List(
			SceneJson.Scene("S6", "Harbor Watch", status: "scheduled", room: "", poses: 0,
				scheduledFor: DateTimeOffset.UtcNow.AddDays(3).ToUnixTimeMilliseconds(), summary: "A crate goes missing."));
		var cut = RenderAt<SharpMUSH.Client.Pages.Scenes>("/scenes", CharactersApiFake.Anonymous(this));
		cut.WaitForAssertion(() => cut.Find(".scene-card"), TimeSpan.FromSeconds(5));

		var meta = cut.Find(".scene-card-meta");
		await Assert.That(meta.QuerySelectorAll("span").Length).IsEqualTo(1).Because("only when it is due, with no separator before it");
		await Assert.That(meta.TextContent).DoesNotContain("poses");
		await Assert.That(cut.Find(".scene-card-pitch").TextContent).IsEqualTo("A crate goes missing.");
	}

	[Test]
	public async Task ASceneLog_FiltersByTag_WithChips()
	{
		var cut = RenderDetail("S1");
		cut.WaitForAssertion(() => cut.Find(".scene-detail-log .story-row"), TimeSpan.FromSeconds(5));

		var chips = cut.FindAll(".kit-chips button");
		await Assert.That(chips.Select(c => c.TextContent)).IsEquivalentTo(new[] { "All", "combat", "dialogue" });
		await chips.Single(c => c.TextContent == "combat").ClickAsync();

		cut.WaitForAssertion(() =>
		{
			if (cut.Markup.Contains("fourth bell", StringComparison.Ordinal)) throw new InvalidOperationException("not filtered yet");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(cut.Markup).Contains("sets her lantern down");
		await Assert.That(cut.Find(".kit-chips button[aria-checked='true']").TextContent).IsEqualTo("combat");
	}

	/// <summary>
	/// A log longer than one streamed batch renders whole, and the chips and cast cover poses that arrived in
	/// the last batch — they are worked out again once the stream ends.
	/// </summary>
	[Test]
	public async Task ALongLog_StreamsInWhole_WithChipsAndCastFromTheLastBatch()
	{
		var count = SceneService.PoseStreamBatch * 2 + 7;
		static string PoseJson(int i, string author, string tag) =>
			"{\"id\":\"L" + i + "\",\"sceneId\":\"S9\",\"authorDbref\":\"#313\",\"authorName\":\"" + author + "\",\"showAsName\":\"" + author
			+ "\",\"originDbref\":\"#40\",\"originName\":\"Lower Docks\",\"source\":\"pose\",\"tags\":[" + (tag.Length == 0 ? "" : "\"" + tag + "\"")
			+ "],\"meta\":{},\"createdAt\":" + (1700000000000 + i) + ",\"isDeleted\":false,\"content\":\"line " + i + "\",\"markup\":\"line " + i
			+ "\",\"editCount\":1,\"lastEditedAt\":null,\"lastEditorDbref\":null,\"lastEditorName\":null}";
		_api.Extra["/api/scenes/S9"] = SceneJson.Scene("S9", "A Long Night", room: "Lower Docks", poses: count);
		_api.Extra["/api/scenes/S9/poses"] = "[" + string.Join(",", Enumerable.Range(0, count)
			.Select(i => i == count - 1 ? PoseJson(i, "Late Arrival", "finale") : PoseJson(i, "Ilsa Varn", ""))) + "]";

		var cut = RenderDetail("S9");
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains($"line {count - 1}", StringComparison.Ordinal)) throw new InvalidOperationException("log still streaming");
		}, TimeSpan.FromSeconds(5));

		await Assert.That(cut.Markup).Contains("line 0");
		await Assert.That(cut.Markup).Contains($"line {SceneService.PoseStreamBatch + 3}");
		await Assert.That(cut.FindAll(".kit-chips button").Select(c => c.TextContent)).IsEquivalentTo(new[] { "All", "finale" });
		await Assert.That(cut.FindAll(".scene-detail-cast a.mention").Select(a => a.TextContent.Trim())).Contains("Late Arrival");
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
