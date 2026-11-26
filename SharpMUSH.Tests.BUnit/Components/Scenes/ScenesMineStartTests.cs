using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Tests.BUnit.Components.Characters;
using AccountCharacter = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Components.Scenes;

/// <summary>
/// Starting a scheduled scene from its card on "Your scenes" (/scenes?mine=1), without opening the scene: offered to
/// whoever runs it (its owner, or a holder of scene.manage), and run as +scene/start with the status read back.
/// </summary>
public class ScenesMineStartTests : TrackingBunitContext
{
	private readonly CharactersApiFake _api;
	private readonly List<PortalCommandRequest> _commands = [];

	private static readonly long Tomorrow = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeMilliseconds();

	public ScenesMineStartTests()
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
		// Every permission the page asks about answers the same, so concurrent asks cannot cross.
		Holds(false);
		Listed("scheduled");
	}

	private BunitNavigationManager Nav => Services.GetRequiredService<BunitNavigationManager>();

	private void Holds(bool permission) =>
		_api.Extra["POST /api/commands/eval"] = $$"""{"output":[],"result":"{{(permission ? 1 : 0)}}","truncated":false}""";

	/// <summary>"Your scenes" for #313 and #314 lists S4, owned by #313 (as every SceneJson scene is).</summary>
	private void Listed(string status)
	{
		var list = SceneJson.List(SceneJson.Scene("S4", "Night of Lamps", status: status, poses: 0, scheduledFor: Tomorrow));
		_api.Extra[SceneJson.Participant(313)] = list;
		_api.Extra[SceneJson.Participant(314)] = list;
		_api.Extra[SceneJson.Recent] = list;
	}

	private void Answer(string status, params string[] output) =>
		_api.Extra["POST /api/commands"] = JsonSerializer.Serialize(
			new PortalCommandResponse(output, status, Truncated: false), JsonSerializerOptions.Web);

	private List<PortalCommandRequest> Commands()
	{
		lock (_commands) return [.. _commands];
	}

	private async Task<IRenderedComponent<SharpMUSH.Client.Pages.Scenes>> RenderAsync(int dbref, string path = "/scenes?mine=1")
	{
		Services.AddSingleton(await CharactersApiFake.SignedInAsync(this, new AccountCharacter(dbref, 1, "Someone", "PLAYER", IsActing: true)));
		Nav.NavigateTo(path);
		var cut = Render<SharpMUSH.Client.Pages.Scenes>();
		cut.WaitForAssertion(() => cut.Find(".scene-card"), TimeSpan.FromSeconds(5));
		return cut;
	}

	[Test]
	public async Task TheOwner_StartsAScheduledScene_FromItsCard()
	{
		var cut = await RenderAsync(313);
		await Assert.That(cut.Find(".scene-card-start").TextContent.Trim()).IsEqualTo("Start now");

		Answer("active", "Scene S4 started.");
		Listed("active");
		await cut.Find(".scene-card-start").ClickAsync();

		cut.WaitForAssertion(() => cut.Find(".scene-card-live"), TimeSpan.FromSeconds(5));
		var sent = Commands().Single();
		await Assert.That(sent.Command).IsEqualTo("+scene/start S4");
		await Assert.That(sent.Result).IsEqualTo("scene(S4,status)");
		await Assert.That(sent.Character).IsEqualTo("#313:1");
		await Assert.That(cut.FindAll(".scene-card-start").Count).IsEqualTo(0).Because("a running scene is joined, not started");
		await Assert.That(cut.FindAll(".scene-card-start-error").Count).IsEqualTo(0);
	}

	[Test]
	public async Task APausedScene_IsResumed()
	{
		Listed("paused");
		var cut = await RenderAsync(313);

		await Assert.That(cut.Find(".scene-card-start").TextContent.Trim()).IsEqualTo("Resume scene");
	}

	[Test]
	public async Task ARefusedStart_SaysWhy_AndTheCardStays()
	{
		var cut = await RenderAsync(313);

		Answer("scheduled", "The room for scene S4 could not be made.");
		await cut.Find(".scene-card-start").ClickAsync();

		cut.WaitForAssertion(() => cut.Find(".scene-card-start-error"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".scene-card-start-error").TextContent).Contains("The room for scene S4 could not be made.");
		await Assert.That(cut.FindAll(".scene-card-start").Count).IsEqualTo(1);
	}

	[Test]
	public async Task SomeoneWhoDoesNotRunTheScene_IsNotOfferedStart()
	{
		var cut = await RenderAsync(314);

		await Assert.That(cut.FindAll(".scene-card-start").Count).IsEqualTo(0);
	}

	[Test]
	public async Task ASceneManager_StartsSomeoneElsesScene_AsThemselves()
	{
		Holds(true);
		var cut = await RenderAsync(314);
		cut.WaitForAssertion(() => cut.Find(".scene-card-start"), TimeSpan.FromSeconds(5));

		Answer("active");
		Listed("active");
		await cut.Find(".scene-card-start").ClickAsync();

		cut.WaitForAssertion(() => cut.Find(".scene-card-live"), TimeSpan.FromSeconds(5));
		await Assert.That(Commands().Single().Character).IsEqualTo("#314:1");
	}

	[Test]
	[Arguments("finished")]
	[Arguments("cancelled")]
	[Arguments("active")]
	public async Task AScene_NotWaitingToStart_HasNoStart(string status)
	{
		Listed(status);
		var cut = await RenderAsync(313);

		await Assert.That(cut.FindAll(".scene-card-start").Count).IsEqualTo(0);
	}

	[Test]
	public async Task TheWholeArchive_LeavesStartToTheScenesOwnPage()
	{
		var cut = await RenderAsync(313, "/scenes");

		await Assert.That(cut.FindAll(".scene-card-start").Count).IsEqualTo(0);
	}
}
