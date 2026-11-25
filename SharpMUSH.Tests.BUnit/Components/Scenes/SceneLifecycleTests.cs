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
/// Finishing and cancelling a scene from its page, and who may: its owner, a holder of scene.manage (who runs any
/// scene as its owner would), and for finishing a holder of scene.close. Both endings ask once before they run,
/// since neither can be taken back.
/// </summary>
public class SceneLifecycleTests : TrackingBunitContext
{
	private readonly CharactersApiFake _api;
	private readonly List<PortalCommandRequest> _commands = [];
	private readonly HashSet<string> _granted = new(StringComparer.Ordinal);

	public SceneLifecycleTests()
	{
		(_api, _, _) = CharactersApiFake.Install(this);
		_api.OnRequest = async request =>
		{
			if (request.Method != HttpMethod.Post) return;
			switch (request.RequestUri!.AbsolutePath)
			{
				case "/api/commands":
					var command = (await request.Content!.ReadFromJsonAsync<PortalCommandRequest>())!;
					lock (_commands) _commands.Add(command);
					break;
				case "/api/commands/eval":
					var eval = (await request.Content!.ReadFromJsonAsync<PortalEvalRequest>())!;
					var holds = eval.Arguments is [var permission] && _granted.Contains(permission) ? "1" : "0";
					_api.Extra["POST /api/commands/eval"] = JsonSerializer.Serialize(
						new PortalCommandResponse([], holds, Truncated: false), JsonSerializerOptions.Web);
					break;
			}
		};
		Services.AddSingleton(Substitute.For<ITerminalService>());
		Services.AddSingleton(sp => new GameCommandService(sp.GetRequiredService<IHttpClientFactory>()));
		_api.Extra["/api/scenes/S1/poses"] = "[]";
	}

	private BunitNavigationManager Nav => Services.GetRequiredService<BunitNavigationManager>();

	private void Answer(string status, params string[] output) =>
		_api.Extra["POST /api/commands"] = JsonSerializer.Serialize(
			new PortalCommandResponse(output, status, Truncated: false), JsonSerializerOptions.Web);

	private List<PortalCommandRequest> Commands()
	{
		lock (_commands) return [.. _commands];
	}

	/// <summary>Renders S1's page acting as <paramref name="dbref"/>; SceneJson scenes are owned by #313.</summary>
	private async Task<IRenderedComponent<SceneDetail>> RenderAsActingAsync(int dbref, string status)
	{
		_api.Extra["/api/scenes/S1"] = SceneJson.Scene("S1", "Salt Market at Dusk", status: status);
		Services.AddSingleton(await CharactersApiFake.SignedInAsync(this, new AccountCharacter(dbref, 1, "Someone", "PLAYER", IsActing: true)));
		Nav.NavigateTo("/scenes/S1");
		var cut = Render<SceneDetail>(p => p.Add(c => c.Id, "S1"));
		cut.WaitForAssertion(() => cut.Find(".kit-page-head h1"), TimeSpan.FromSeconds(5));
		return cut;
	}

	[Test]
	public async Task TheOwner_FinishesARunningScene_AfterConfirming()
	{
		var cut = await RenderAsActingAsync(313, "active");

		await cut.Find(".scene-detail-finish").ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".scene-finish-submit"), TimeSpan.FromSeconds(5));
		await Assert.That(Commands()).IsEmpty().Because("opening the confirmation runs nothing");

		Answer("finished", "Scene S1 finished.");
		_api.Extra["/api/scenes/S1"] = SceneJson.Scene("S1", "Salt Market at Dusk", status: "finished");
		await cut.Find(".scene-finish-submit").ClickAsync();

		cut.WaitForState(() => cut.FindAll(".scene-finish").Count == 0, TimeSpan.FromSeconds(5));
		var sent = Commands().Single();
		await Assert.That(sent.Command).IsEqualTo("+scene/finish S1");
		await Assert.That(sent.Result).IsEqualTo("scene(S1,status)");
		await Assert.That(sent.Character).IsEqualTo("#313:1");
		await Assert.That(cut.FindAll(".scene-detail-finish").Count).IsEqualTo(0).Because("a finished scene has no controls");
		await Assert.That(cut.FindAll(".scene-detail-pause").Count).IsEqualTo(0);
	}

	[Test]
	public async Task KeepingTheScene_ClosesTheConfirmation_AndRunsNothing()
	{
		var cut = await RenderAsActingAsync(313, "paused");
		await cut.Find(".scene-detail-finish").ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".scene-finish"), TimeSpan.FromSeconds(5));

		await cut.Find(".scene-finish .kit-capsule:not(.scene-finish-submit)").ClickAsync();

		cut.WaitForState(() => cut.FindAll(".scene-finish").Count == 0, TimeSpan.FromSeconds(5));
		await Assert.That(Commands()).IsEmpty();
	}

	[Test]
	public async Task ASceneManager_RunsSomeoneElsesScene_AsThemselves()
	{
		_granted.Add("scene.manage");
		var cut = await RenderAsActingAsync(314, "active");

		cut.WaitForAssertion(() => cut.Find(".scene-detail-finish"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".scene-detail-pause").Count).IsEqualTo(1);
		await Assert.That(cut.FindAll(".scene-detail-edit").Count).IsEqualTo(1);

		await cut.Find(".scene-detail-pause").ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".scene-pause-submit"), TimeSpan.FromSeconds(5));
		Answer("paused");
		_api.Extra["/api/scenes/S1"] = SceneJson.Scene("S1", "Salt Market at Dusk", status: "paused");
		await cut.Find(".scene-pause-submit").ClickAsync();

		cut.WaitForAssertion(() => cut.Find(".scene-detail-paused"), TimeSpan.FromSeconds(5));
		var sent = Commands().Single();
		await Assert.That(sent.Command).IsEqualTo("+scene/pause S1");
		await Assert.That(sent.Character).IsEqualTo("#314:1");
	}

	[Test]
	public async Task AWizard_HoldingControlAll_RunsAnyScene()
	{
		_granted.Add("control.all");
		var cut = await RenderAsActingAsync(314, "active");

		cut.WaitForAssertion(() => cut.Find(".scene-detail-finish"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".scene-detail-pause").Count).IsEqualTo(1)
			.Because("the verbs count control.all as scene.manage");
	}

	[Test]
	public async Task ASceneCloser_MayOnlyFinish()
	{
		_granted.Add("scene.close");
		var cut = await RenderAsActingAsync(314, "active");

		cut.WaitForAssertion(() => cut.Find(".scene-detail-finish"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".scene-detail-pause").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".scene-detail-edit").Count).IsEqualTo(0);

		await cut.Find(".scene-detail-finish").ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".scene-finish-submit"), TimeSpan.FromSeconds(5));
		Answer("finished");
		_api.Extra["/api/scenes/S1"] = SceneJson.Scene("S1", "Salt Market at Dusk", status: "finished");
		await cut.Find(".scene-finish-submit").ClickAsync();

		cut.WaitForState(() => cut.FindAll(".scene-finish").Count == 0, TimeSpan.FromSeconds(5));
		await Assert.That(Commands().Single().Character).IsEqualTo("#314:1");
	}

	[Test]
	[Arguments("active")]
	[Arguments("paused")]
	[Arguments("scheduled")]
	public async Task SomeoneElse_SeesNoEnding(string status)
	{
		var cut = await RenderAsActingAsync(314, status);

		await Assert.That(cut.FindAll(".scene-detail-finish").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".scene-detail-cancel").Count).IsEqualTo(0);
	}

	[Test]
	public async Task TheOwner_CancelsAScheduledScene_ButCannotFinishIt()
	{
		var cut = await RenderAsActingAsync(313, "scheduled");
		await Assert.That(cut.FindAll(".scene-detail-finish").Count).IsEqualTo(0).Because("a scene that never ran has nothing to finish");

		await cut.Find(".scene-detail-cancel").ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".scene-cancel-submit"), TimeSpan.FromSeconds(5));
		Answer("cancelled", "Cancelled scene S1.");
		_api.Extra["/api/scenes/S1"] = SceneJson.Scene("S1", "Salt Market at Dusk", status: "cancelled");
		await cut.Find(".scene-cancel-submit").ClickAsync();

		cut.WaitForState(() => cut.FindAll(".scene-cancel").Count == 0, TimeSpan.FromSeconds(5));
		await Assert.That(Commands().Single().Command).IsEqualTo("+scene/cancel S1");
		await Assert.That(cut.FindAll(".scene-detail-resume").Count).IsEqualTo(0).Because("a cancelled scene can't be started again");
	}

	[Test]
	public async Task ARefusedCancel_IsShown_AndTheConfirmationStays()
	{
		var cut = await RenderAsActingAsync(313, "scheduled");
		await cut.Find(".scene-detail-cancel").ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".scene-cancel-submit"), TimeSpan.FromSeconds(5));

		Answer("scheduled", "Scene S1 has poses in it, so only a wizard or someone holding the scene.manage permission can cancel it.");
		await cut.Find(".scene-cancel-submit").ClickAsync();

		cut.WaitForAssertion(() => cut.Find(".scene-action-error"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".scene-action-error").TextContent).Contains("has poses in it");
		await Assert.That(cut.FindAll(".scene-cancel").Count).IsEqualTo(1);
	}
}
