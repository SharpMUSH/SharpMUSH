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
	public async Task APausedScene_IsTaggedOnTheSchedule()
	{
		_api.Extra[SceneJson.Scheduled] = SceneJson.List(SceneJson.Scene("S1", "Salt Market at Dusk", status: "paused"));
		Services.AddSingleton(CharactersApiFake.Anonymous(this));
		Nav.NavigateTo("/scenes?scheduled=1");
		var cut = Render<SharpMUSH.Client.Pages.Scenes>();

		cut.WaitForAssertion(() => cut.Find(".scene-card"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".scene-card-paused").TextContent.Trim()).IsEqualTo("PAUSED");
		await Assert.That(cut.FindAll(".scene-card-live").Count).IsEqualTo(0);
	}
}
