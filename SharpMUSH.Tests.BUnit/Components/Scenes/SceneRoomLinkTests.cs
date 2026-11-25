using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Components.Scenes;
using SharpMUSH.Client.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Tests.BUnit.Components.Characters;
using AccountCharacter = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Components.Scenes;

/// <summary>
/// A scene's room name: for a character viewing a running scene, a button that runs +scene/go and opens Play; for a
/// scene not running, or a viewer with no character, the name as text.
/// </summary>
public class SceneRoomLinkTests : TrackingBunitContext
{
	private readonly CharactersApiFake _api;
	private readonly List<PortalCommandRequest> _commands = [];

	public SceneRoomLinkTests()
	{
		(_api, _, _) = CharactersApiFake.Install(this);
		_api.OnRequest = async request =>
		{
			if (request.Method != HttpMethod.Post || request.RequestUri!.AbsolutePath != "/api/commands") return;
			var command = (await request.Content!.ReadFromJsonAsync<PortalCommandRequest>())!;
			lock (_commands) _commands.Add(command);
		};
	}

	private static SceneSummary Scene(string status) => new(
		"S7", status, true, false, null, CharactersApiFake.Now - 3_600_000, CharactersApiFake.Now - 120_000, 12,
		"#313", "Ilsa Varn", "#313", "Ilsa Varn", "#40", "Lower Docks", new Dictionary<string, string>());

	private async Task SignInAsync() =>
		Services.AddSingleton(await CharactersApiFake.SignedInAsync(this, new AccountCharacter(52, 1, "Wren", "PLAYER", IsActing: true)));

	private void Answer(string result, params string[] output) =>
		_api.Extra["POST /api/commands"] = JsonSerializer.Serialize(
			new PortalCommandResponse(output, result, Truncated: false), JsonSerializerOptions.Web);

	[Test]
	public async Task ARunningScene_GoesThere_AndOpensPlay()
	{
		await SignInAsync();
		Answer("1");
		var cut = Render<SceneRoomLink>(p => p.Add(x => x.Scene, Scene("active")));

		await cut.Find("button.scene-room-link").ClickAsync();

		var nav = Services.GetRequiredService<BunitNavigationManager>();
		cut.WaitForState(() => nav.Uri.EndsWith("/play", StringComparison.Ordinal), TimeSpan.FromSeconds(5));
		PortalCommandRequest sent;
		lock (_commands) sent = _commands.Single();
		await Assert.That(sent.Command).IsEqualTo("+scene/go S7");
		await Assert.That(sent.Result).IsEqualTo("strmatch(objid(loc(me)),scene(S7,room))");
		await Assert.That(sent.Character).IsEqualTo("#52:1");
	}

	[Test]
	public async Task ARefusal_ShowsBesideTheName_AndStays()
	{
		await SignInAsync();
		Answer("0", "Permission denied. You are FIXED, so you can't travel to a scene.");
		var cut = Render<SceneRoomLink>(p => p.Add(x => x.Scene, Scene("active")));

		await cut.Find("button.scene-room-link").ClickAsync();

		cut.WaitForAssertion(() => cut.Find(".scene-room-refusal"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".scene-room-refusal").TextContent).Contains("FIXED");
		await Assert.That(Services.GetRequiredService<BunitNavigationManager>().Uri).DoesNotEndWith("/play");
	}

	[Test]
	[Arguments("paused")]
	[Arguments("finished")]
	[Arguments("new")]
	public async Task AScene_NotRunning_IsText(string status)
	{
		await SignInAsync();
		var cut = Render<SceneRoomLink>(p => p.Add(x => x.Scene, Scene(status)));
		await Assert.That(cut.FindAll("button").Count).IsEqualTo(0);
		await Assert.That(cut.Markup).Contains("Lower Docks");
	}

	[Test]
	public async Task AViewer_WithNoCharacter_SeesText()
	{
		var cut = Render<SceneRoomLink>(p => p.Add(x => x.Scene, Scene("active")));
		await Assert.That(cut.FindAll("button").Count).IsEqualTo(0);
		await Assert.That(cut.Markup).Contains("Lower Docks");
	}
}
