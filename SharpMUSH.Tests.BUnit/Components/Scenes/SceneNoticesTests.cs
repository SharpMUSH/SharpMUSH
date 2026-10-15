using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using SharpMUSH.Client.Components.Scenes;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components.Scenes;

/// <summary>
/// A notice the scene hub sends to one member (<c>@scene/notice</c>) shows on that member's scene page until
/// dismissed. The hub already routes it to them alone; the page shows only notices for the scene it shows.
/// </summary>
public class SceneNoticesTests : TrackingBunitContext
{
	private readonly FakeSceneHub _hub = new();

	public SceneNoticesTests()
	{
		Services.AddMudServices();
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		Services.AddSingleton<ISceneHubControl>(_hub);
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private static SceneEventMessage Event(string sceneId, string eventType, string content) => new(
		SceneId: sceneId, EventType: eventType, ActorName: string.Empty, PoseId: string.Empty, Content: content,
		Markup: content, Tags: [], Source: string.Empty, Location: string.Empty, Timestamp: 1, ActorObjId: null, Recipient: "#18");

	[Test]
	public async Task A_notice_for_this_scene_shows_until_dismissed()
	{
		var cut = Render<SceneNotices>(p => p.Add(x => x.SceneId, "42"));

		_hub.RaiseScene(Event("42", "notice", "Someone in scene 42 asks to pause what is happening now."));

		cut.WaitForAssertion(() => cut.Find(".scene-notice"));
		await Assert.That(cut.Find(".scene-notice-text").TextContent)
			.IsEqualTo("Someone in scene 42 asks to pause what is happening now.");
		await Assert.That(cut.Find(".scene-notices").GetAttribute("role")).IsEqualTo("alert");

		await cut.Find(".scene-notice-dismiss").ClickAsync();

		await Assert.That(cut.FindAll(".scene-notice")).IsEmpty();
	}

	/// <summary>The page is reused when the route moves to another scene; the last scene's notices do not follow.</summary>
	[Test]
	public async Task Moving_to_another_scene_drops_the_last_scenes_notices()
	{
		var cut = Render<SceneNotices>(p => p.Add(x => x.SceneId, "42"));
		_hub.RaiseScene(Event("42", "notice", "Someone in scene 42 asks to pause what is happening now."));
		cut.WaitForAssertion(() => cut.Find(".scene-notice"));

		cut.Render(p => p.Add(x => x.SceneId, "7"));

		await Assert.That(cut.FindAll(".scene-notice")).IsEmpty();
	}

	/// <summary>Switching character keeps the page mounted; one character's notices are not the next one's.</summary>
	[Test]
	public async Task Switching_character_drops_the_last_characters_notices()
	{
		var cut = Render<SceneNotices>(p => p.Add(x => x.SceneId, "42").Add(x => x.ViewerName, "Ann"));
		_hub.RaiseScene(Event("42", "notice", "Someone in scene 42 asks to pause what is happening now."));
		cut.WaitForAssertion(() => cut.Find(".scene-notice"));

		cut.Render(p => p.Add(x => x.SceneId, "42").Add(x => x.ViewerName, "Bea"));

		await Assert.That(cut.FindAll(".scene-notice")).IsEmpty();
	}

	[Test]
	public async Task Poses_and_other_scenes_notices_are_not_shown()
	{
		var cut = Render<SceneNotices>(p => p.Add(x => x.SceneId, "42"));

		_hub.RaiseScene(Event("42", "pose", "Ann waves."));
		_hub.RaiseScene(Event("7", "notice", "Someone in scene 7 asks to skip what is happening now."));
		cut.Render();

		await Assert.That(cut.FindAll(".scene-notice")).IsEmpty();
	}
}
