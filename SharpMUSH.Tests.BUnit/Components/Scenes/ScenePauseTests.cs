using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Client.Pages;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Tests.BUnit.Components.Characters;
using AccountCharacter = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Components.Scenes;

/// <summary>
/// Pausing a scene from its page. The owner, acting as its owner, pauses a running scene (with a new time
/// or without) and resumes a paused one, through the same +scene verbs a player types; anyone else sees
/// neither control. A paused scene carries a PAUSED tag on its page and its card.
/// </summary>
public class ScenePauseTests : TrackingBunitContext
{
	private readonly CharactersApiFake _api;
	private readonly List<PortalCommandRequest> _commands = [];

	public ScenePauseTests()
	{
		(_api, _, _) = CharactersApiFake.Install(this);
		_api.OnRequest = async request =>
		{
			if (request.Method != HttpMethod.Post || request.RequestUri!.AbsolutePath != "/api/commands") return;
			var command = (await request.Content!.ReadFromJsonAsync<PortalCommandRequest>())!;
			lock (_commands) _commands.Add(command);
		};
		Services.AddSingleton(Substitute.For<ITerminalService>());
		Services.AddSingleton(sp => new GameCommandService(sp.GetRequiredService<IHttpClientFactory>()));
		_api.Extra["/api/scenes/S1/poses"] = "[]";
	}

	private BunitNavigationManager Nav => Services.GetRequiredService<BunitNavigationManager>();

	/// <summary>What the engine answers: the verb's output and the scene's status after it ran.</summary>
	private void Answer(string status, params string[] output) =>
		_api.Extra["POST /api/commands"] = JsonSerializer.Serialize(
			new PortalCommandResponse(output, status, Truncated: false), JsonSerializerOptions.Web);

	private List<PortalCommandRequest> Commands()
	{
		lock (_commands) return [.. _commands];
	}

	/// <summary>Renders S1's page acting as <paramref name="dbref"/>; SceneJson scenes are owned by #313.</summary>
	private async Task<IRenderedComponent<SceneDetail>> RenderAsActingAsync(int dbref, string status, long? scheduledFor = null)
	{
		_api.Extra["/api/scenes/S1"] = SceneJson.Scene("S1", "Salt Market at Dusk", status: status, scheduledFor: scheduledFor);
		Services.AddSingleton(await CharactersApiFake.SignedInAsync(this, new AccountCharacter(dbref, 1, "Someone", "PLAYER", IsActing: true)));
		Nav.NavigateTo("/scenes/S1");
		var cut = Render<SceneDetail>(p => p.Add(c => c.Id, "S1"));
		cut.WaitForAssertion(() => cut.Find(".kit-page-head h1"), TimeSpan.FromSeconds(5));
		return cut;
	}

	[Test]
	public async Task TheOwner_PausesARunningScene_WithTheVerbAPlayerTypes()
	{
		var cut = await RenderAsActingAsync(313, "active");

		cut.Find(".scene-detail-pause").Click();
		cut.WaitForAssertion(() => cut.Find(".scene-pause-submit"), TimeSpan.FromSeconds(5));

		Answer("paused");
		_api.Extra["/api/scenes/S1"] = SceneJson.Scene("S1", "Salt Market at Dusk", status: "paused");
		cut.Find(".scene-pause-submit").Click();

		cut.WaitForAssertion(() => cut.Find(".scene-detail-paused"), TimeSpan.FromSeconds(5));
		var sent = Commands().Single();
		await Assert.That(sent.Command).IsEqualTo("+scene/pause S1").Because("no new time was chosen");
		await Assert.That(sent.Result).IsEqualTo("scene(S1,status)");
		await Assert.That(sent.Character).IsEqualTo("#313:1");
		await Assert.That(cut.FindAll(".scene-pause").Count).IsEqualTo(0).Because("the form closes once the scene is paused");
		await Assert.That(cut.FindAll(".scene-detail-resume").Count).IsEqualTo(1).Because("a paused scene is resumed from the same place");
	}

	[Test]
	public async Task TheOwner_ResumesAPausedScene()
	{
		var due = DateTimeOffset.UtcNow.AddDays(2);
		var cut = await RenderAsActingAsync(313, "paused", due.ToUnixTimeMilliseconds());

		await Assert.That(cut.Find(".scene-detail-paused").TextContent.Trim()).IsEqualTo("PAUSED");
		await Assert.That(cut.Find(".scene-detail-scheduled").TextContent).IsEqualTo(due.ToLocalTime().ToString("MMM d, yyyy HH:mm"));

		Answer("active");
		_api.Extra["/api/scenes/S1"] = SceneJson.Scene("S1", "Salt Market at Dusk", status: "active");
		cut.Find(".scene-detail-resume").Click();

		cut.WaitForAssertion(() => cut.Find(".scene-detail-live"), TimeSpan.FromSeconds(5));
		await Assert.That(Commands().Single().Command).IsEqualTo("+scene/start S1");
	}

	[Test]
	public async Task ARefusal_IsShown_AndTheSceneStaysAsItWas()
	{
		var cut = await RenderAsActingAsync(313, "active");
		cut.Find(".scene-detail-pause").Click();
		cut.WaitForAssertion(() => cut.Find(".scene-pause-submit"), TimeSpan.FromSeconds(5));

		Answer("active", "You do not own that scene.");
		cut.Find(".scene-pause-submit").Click();

		cut.WaitForAssertion(() => cut.Find(".scene-action-error"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".scene-action-error").TextContent).Contains("You do not own that scene.");
		await Assert.That(cut.FindAll(".scene-detail-live").Count).IsEqualTo(1);
		await Assert.That(cut.FindAll(".scene-pause").Count).IsEqualTo(1).Because("the form stays open to try again");
	}

	[Test]
	[Arguments("active")]
	[Arguments("paused")]
	public async Task SomeoneElse_SeesNeitherControl(string status)
	{
		var cut = await RenderAsActingAsync(314, status);

		await Assert.That(cut.FindAll(".scene-detail-pause").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".scene-detail-resume").Count).IsEqualTo(0);
	}

	[Test]
	public async Task AFinishedScene_HasNoControls()
	{
		var cut = await RenderAsActingAsync(313, "finished");

		await Assert.That(cut.FindAll(".scene-detail-pause").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".scene-detail-resume").Count).IsEqualTo(0);
	}

	[Test]
	public async Task TheOwner_EditsThePitch_AndThePageShowsIt()
	{
		var cut = await RenderAsActingAsync(313, "active");
		await Assert.That(cut.FindAll(".scene-detail-pitch").Count).IsEqualTo(0);

		cut.Find(".scene-detail-edit").Click();
		cut.WaitForAssertion(() => cut.Find(".scene-edit-submit"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".scene-edit-title input").GetAttribute("value")).IsEqualTo("Salt Market at Dusk")
			.Because("the form opens on the scene as it is");
		cut.Find(".scene-edit-pitch textarea").Change("Masks and music; a feud.");

		Answer("Masks and music; a feud.", "Pitch set for scene S1.");
		_api.Extra["/api/scenes/S1"] = SceneJson.Scene("S1", "Salt Market at Dusk", summary: "Masks and music; a feud.");
		cut.Find(".scene-edit-submit").Click();

		cut.WaitForAssertion(() => cut.Find(".scene-detail-pitch"), TimeSpan.FromSeconds(5));
		var sent = Commands().Single();
		await Assert.That(sent.Command).IsEqualTo("+scene/pitch S1=Masks and music%; a feud.")
			.Because("only what changed is sent, naming the scene, with the semicolon kept from ending the command");
		await Assert.That(sent.Result).IsEqualTo("scene(S1,summary)");
		await Assert.That(sent.Character).IsEqualTo("#313:1");
		await Assert.That(cut.Find(".scene-detail-pitch").TextContent).IsEqualTo("Masks and music; a feud.");
		await Assert.That(cut.FindAll(".scene-edit").Count).IsEqualTo(0).Because("the form closes once it is saved");
	}

	[Test]
	public async Task TheOwner_MakesTheScenePrivate()
	{
		var cut = await RenderAsActingAsync(313, "active");
		cut.Find(".scene-detail-edit").Click();
		cut.WaitForAssertion(() => cut.Find(".scene-edit-submit"), TimeSpan.FromSeconds(5));
		cut.Find(".scene-edit-public input").Change(false);

		Answer("0", "Scene S1 is now private.");
		_api.Extra["/api/scenes/S1"] = SceneJson.Scene("S1", "Salt Market at Dusk", isPublic: false);
		cut.Find(".scene-edit-submit").Click();

		cut.WaitForState(() => cut.FindAll(".scene-edit").Count == 0, TimeSpan.FromSeconds(5));
		await Assert.That(Commands().Single().Command).IsEqualTo("+scene/private S1");
	}

	[Test]
	public async Task AnEditTheGameRefuses_IsShown_AndTheFormStaysOpen()
	{
		var cut = await RenderAsActingAsync(313, "active");
		cut.Find(".scene-detail-edit").Click();
		cut.WaitForAssertion(() => cut.Find(".scene-edit-submit"), TimeSpan.FromSeconds(5));
		cut.Find(".scene-edit-title input").Change("Ash and Salt");

		Answer("Salt Market at Dusk", "That scene is not yours to change. Its owner or a wizard can do it.");
		cut.Find(".scene-edit-submit").Click();

		cut.WaitForAssertion(() => cut.Find(".scene-action-error"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".scene-action-error").TextContent).Contains("not yours to change");
		await Assert.That(Commands().Single().Command).IsEqualTo("+scene/title S1=Ash and Salt");
		await Assert.That(cut.FindAll(".scene-edit").Count).IsEqualTo(1);
	}

	[Test]
	public async Task SomeoneElse_CannotEdit()
	{
		var cut = await RenderAsActingAsync(314, "active");
		await Assert.That(cut.FindAll(".scene-detail-edit").Count).IsEqualTo(0);
	}

	[Test]
	public async Task AScheduledScene_CanBeAddedToACalendar()
	{
		var start = new DateTimeOffset(2030, 3, 4, 18, 30, 0, TimeSpan.Zero);
		var cut = await RenderAsActingAsync(314, "scheduled", start.ToUnixTimeMilliseconds());

		var google = cut.Find(".scene-calendar-google").GetAttribute("href")!;
		await Assert.That(google).StartsWith("https://calendar.google.com/calendar/render?action=TEMPLATE");
		await Assert.That(google).Contains("&text=Salt%20Market%20at%20Dusk");
		await Assert.That(google).Contains("&dates=20300304T183000Z/20300304T203000Z");
		await Assert.That(google).Contains("&location=The%20Salt%20Market");

		var ics = cut.Find(".scene-calendar-ics");
		await Assert.That(ics.GetAttribute("download")).IsEqualTo("scene-S1.ics");
		var href = ics.GetAttribute("href")!;
		await Assert.That(href).StartsWith("data:text/calendar;charset=utf-8,");
		var file = Uri.UnescapeDataString(href["data:text/calendar;charset=utf-8,".Length..]);
		await Assert.That(file).Contains("DTSTART:20300304T183000Z\r\n");
		await Assert.That(file).Contains("SUMMARY:Salt Market at Dusk\r\n");
		await Assert.That(file).Contains("URL:http://localhost/scenes/S1\r\n");
	}

	[Test]
	[Arguments("active")]
	[Arguments("finished")]
	public async Task ASceneThatIsNotWaiting_HasNoCalendarLinks(string status)
	{
		var cut = await RenderAsActingAsync(314, status, new DateTimeOffset(2030, 3, 4, 18, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds());
		await Assert.That(cut.FindAll(".scene-calendar").Count).IsEqualTo(0);
	}

	[Test]
	public async Task TheSchedule_IsAnAgendaByDayAndTime_WithPausedScenesLast()
	{
		var first = new DateTimeOffset(2030, 3, 4, 18, 30, 0, TimeSpan.Zero);
		var second = first.AddDays(1);
		_api.Extra[SceneJson.Scheduled] = SceneJson.List(
			SceneJson.Scene("S4", "Night of Lamps", status: "scheduled", room: "", scheduledFor: first.ToUnixTimeMilliseconds()),
			SceneJson.Scene("S5", "The Harbour Ball", status: "paused", scheduledFor: second.ToUnixTimeMilliseconds()),
			SceneJson.Scene("S1", "Salt Market at Dusk", status: "paused"));
		Services.AddSingleton(CharactersApiFake.Anonymous(this));
		Nav.NavigateTo("/scenes?scheduled=1");
		var cut = Render<SharpMUSH.Client.Pages.Scenes>();

		cut.WaitForAssertion(() => cut.Find(".schedule-row"), TimeSpan.FromSeconds(5));
		var days = cut.FindAll(".schedule-day-head").Select(h => h.TextContent.Trim()).ToList();
		await Assert.That(string.Join(" | ", days)).IsEqualTo(string.Join(" | ",
			first.ToLocalTime().ToString("D", CultureInfo.CurrentCulture),
			second.ToLocalTime().ToString("D", CultureInfo.CurrentCulture),
			"Paused, no time set")).Because("each day is a heading, and the scenes with no time close the agenda");

		var rows = cut.FindAll(".schedule-row");
		await Assert.That(rows[0].QuerySelector(".schedule-time")!.TextContent).IsEqualTo(first.ToLocalTime().ToString("t", CultureInfo.CurrentCulture));
		await Assert.That(rows[0].QuerySelector(".schedule-title")!.GetAttribute("href")).IsEqualTo("/scenes/S4");
		await Assert.That(rows[0].QuerySelector(".schedule-meta")!.TextContent).Contains("No room yet");
		await Assert.That(rows[0].QuerySelector(".schedule-meta")!.TextContent).Contains("Hosted by Ilsa Varn");
		await Assert.That(rows[1].QuerySelector(".schedule-paused")!.TextContent.Trim()).IsEqualTo("PAUSED");
		await Assert.That(rows[2].QuerySelector(".schedule-title")!.TextContent).IsEqualTo("Salt Market at Dusk");
		await Assert.That(cut.FindAll(".scene-card").Count).IsEqualTo(0).Because("the schedule is an agenda, not the archive's cards");
	}
}
