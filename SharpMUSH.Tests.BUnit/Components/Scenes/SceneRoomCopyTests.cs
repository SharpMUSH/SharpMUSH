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
/// The scene-rooms package's portal surface: on a game that has it (scan() finds +scene/create/copy and
/// +scene/start/copy for the character), the Scenes start form offers to run the new scene in a copy of the
/// character's room, /scenes?start=copy opens it that way, and a scene's page offers to start or resume it in a copy.
/// A game without the package shows none of it.
/// </summary>
public class SceneRoomCopyTests : TrackingBunitContext
{
	private readonly CharactersApiFake _api;
	private readonly List<PortalCommandRequest> _commands = [];

	public SceneRoomCopyTests()
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
		var list = SceneJson.List();
		_api.Extra[SceneJson.Recent] = list;
		_api.Extra[SceneJson.Participant(313)] = list;
	}

	private BunitNavigationManager Nav => Services.GetRequiredService<BunitNavigationManager>();

	/// <summary>Every eval the pages send answers the same: the package's commands (and every permission) there or not.</summary>
	private void Installed(bool installed) =>
		_api.Extra["POST /api/commands/eval"] = $$"""{"output":[],"result":"{{(installed ? 1 : 0)}}","truncated":false}""";

	private void Answer(string result, params string[] output) =>
		_api.Extra["POST /api/commands"] = JsonSerializer.Serialize(
			new PortalCommandResponse(output, result, Truncated: false), JsonSerializerOptions.Web);

	private List<PortalCommandRequest> Commands()
	{
		lock (_commands) return [.. _commands];
	}

	private async Task SignInAsync()
	{
		Services.AddSingleton(await CharactersApiFake.SignedInAsync(this, new AccountCharacter(313, 1, "Someone", "PLAYER", IsActing: true)));
	}

	/// <summary>The create the start form sent, after the think that reads the focus before it.</summary>
	private async Task<string> CreatedAsync(IRenderedComponent<SharpMUSH.Client.Pages.Scenes> cut)
	{
		cut.WaitForAssertion(() => Commands().Single(c => c.Command.StartsWith("+scene/", StringComparison.Ordinal)), TimeSpan.FromSeconds(5));
		return await Task.FromResult(Commands().Single(c => c.Command.StartsWith("+scene/", StringComparison.Ordinal)).Command);
	}

	private async Task<IRenderedComponent<SceneDetail>> RenderSceneAsync(string status)
	{
		_api.Extra["/api/scenes/S1"] = SceneJson.Scene("S1", "Salt Market at Dusk", status: status);
		await SignInAsync();
		Nav.NavigateTo("/scenes/S1");
		var cut = Render<SceneDetail>(p => p.Add(c => c.Id, "S1"));
		cut.WaitForAssertion(() => cut.Find(".kit-page-head h1"), TimeSpan.FromSeconds(5));
		return cut;
	}

	[Test]
	public async Task APausedScene_ResumesInACopy_WithTheVerbAPlayerTypes()
	{
		Installed(true);
		var cut = await RenderSceneAsync("paused");
		cut.WaitForAssertion(() => cut.Find(".scene-detail-resume-copy"), TimeSpan.FromSeconds(5));

		Answer("active");
		_api.Extra["/api/scenes/S1"] = SceneJson.Scene("S1", "Salt Market at Dusk", status: "active");
		await cut.Find(".scene-detail-resume-copy").ClickAsync();

		cut.WaitForAssertion(() => cut.Find(".scene-detail-live"), TimeSpan.FromSeconds(5));
		var sent = Commands().Single();
		await Assert.That(sent.Command).IsEqualTo("+scene/start/copy S1");
		await Assert.That(sent.Result).IsEqualTo("scene(S1,status)");
	}

	[Test]
	public async Task WithoutThePackage_ASceneOffersNoCopy()
	{
		Installed(false);
		var cut = await RenderSceneAsync("paused");
		cut.WaitForAssertion(() => cut.Find(".scene-detail-resume"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".scene-detail-resume-copy").Count).IsEqualTo(0);
	}

	[Test]
	public async Task StartCopyAddress_OpensTheStartForm_SetToACopy_AndCreatesItThere()
	{
		Installed(true);
		await SignInAsync();
		Nav.NavigateTo("/scenes?start=copy");
		var cut = Render<SharpMUSH.Client.Pages.Scenes>();
		cut.WaitForAssertion(() => cut.Find(".scene-start-copy input"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".scene-start-copy input").HasAttribute("checked")).IsTrue();

		Answer("#-1 NO SCENE");
		await cut.Find(".scene-start-title input").InputAsync("Night at the Docks");
		await cut.Find(".scene-start-submit").ClickAsync();

		await Assert.That(await CreatedAsync(cut)).IsEqualTo("+scene/create/copy Night at the Docks");
	}

	[Test]
	public async Task WithoutThePackage_TheStartFormHasNoCopyOption()
	{
		Installed(false);
		await SignInAsync();
		Nav.NavigateTo("/scenes?start=copy");
		var cut = Render<SharpMUSH.Client.Pages.Scenes>();
		cut.WaitForAssertion(() => cut.Find(".scene-start-title input"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".scene-start-copy").Count).IsEqualTo(0);

		Answer("#-1 NO SCENE");
		await cut.Find(".scene-start-title input").InputAsync("Night at the Docks");
		await cut.Find(".scene-start-submit").ClickAsync();
		await Assert.That(await CreatedAsync(cut)).IsEqualTo("+scene/create Night at the Docks")
			.Because("a game without the package makes the scene where the character stands");
	}
}
