using Bunit;
using SharpMUSH.Client.Components.Scenes;
using SharpMUSH.Client.Models;
using SharpMUSH.Tests.BUnit.Components.Characters;

namespace SharpMUSH.Tests.BUnit.Components.Scenes;

/// <summary>
/// One scene in a list (/scenes, /scenes/active): the title, a LIVE tag while it runs, "room · N poses ·
/// when", Read, and the live view for a running scene — Join for a viewer with a character, Watch live
/// for anyone else.
/// </summary>
public class SceneCardTests : TrackingBunitContext
{
	public SceneCardTests() => CharactersApiFake.Install(this);

	private static SceneSummary Scene(string status = "active", string? title = "Salt Market at Dusk", bool isPublic = true,
		long? scheduledFor = null) => new(
		"S7", status, isPublic, false, scheduledFor, CharactersApiFake.Now - 3_600_000, CharactersApiFake.Now - 120_000, 12,
		"#313", "Ilsa Varn", "#313", "Ilsa Varn", "#40", "Lower Docks",
		title is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["title"] = title });

	[Test]
	public async Task ALiveScene_ShowsTheTag_TheMetaLine_ReadAndJoin()
	{
		var cut = Render<SceneCard>(p => p.Add(x => x.Scene, Scene()).Add(x => x.CanJoin, true));

		await Assert.That(cut.Find(".scene-card-title").TextContent.Trim()).IsEqualTo("Salt Market at Dusk");
		await Assert.That(cut.Find(".scene-card-live").TextContent).Contains("LIVE");
		var meta = cut.Find(".scene-card-meta").TextContent;
		await Assert.That(meta).Contains("Lower Docks");
		await Assert.That(meta).Contains("12 poses");
		await Assert.That(meta).Contains("2 minutes ago");
		var actions = cut.FindAll(".scene-card-actions a");
		await Assert.That(actions.Count).IsEqualTo(2);
		await Assert.That(actions[0].GetAttribute("href")).IsEqualTo("/scenes/S7");
		await Assert.That(actions[0].TextContent.Trim()).IsEqualTo("Read");
		await Assert.That(actions[1].GetAttribute("href")).IsEqualTo("/scenes/S7/live");
		await Assert.That(actions[1].TextContent.Trim()).IsEqualTo("Join");
		await Assert.That(actions[1].ClassList).Contains("kit-capsule--primary");
	}

	[Test]
	public async Task ALiveScene_OffersWatch_ToAViewerWithNoCharacter()
	{
		var cut = Render<SceneCard>(p => p.Add(x => x.Scene, Scene()));
		var live = cut.FindAll(".scene-card-actions a")[1];
		await Assert.That(live.GetAttribute("href")).IsEqualTo("/scenes/S7/live");
		await Assert.That(live.TextContent.Trim()).IsEqualTo("Watch Live");
	}

	[Test]
	public async Task AFinishedScene_OnlyReads()
	{
		var cut = Render<SceneCard>(p => p.Add(x => x.Scene, Scene("finished")).Add(x => x.CanJoin, true));
		await Assert.That(cut.FindAll(".scene-card-live").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".scene-card-actions a").Count).IsEqualTo(1);
	}

	[Test]
	public async Task AnUntitledScene_IsNamedForItsRoom_AndAPrivateOneSaysSo()
	{
		var cut = Render<SceneCard>(p => p.Add(x => x.Scene, Scene(title: null, isPublic: false)));
		await Assert.That(cut.Find(".scene-card-title").TextContent.Trim()).IsEqualTo("Scene in Lower Docks");
		await Assert.That(cut.Find(".scene-card-private").TextContent.Trim()).IsEqualTo("private");
	}

	[Test]
	public async Task AScheduledScene_SaysWhenItIsDue()
	{
		var due = DateTimeOffset.UtcNow.AddDays(3);
		var cut = Render<SceneCard>(p => p.Add(x => x.Scene, Scene("new", scheduledFor: due.ToUnixTimeMilliseconds())));
		await Assert.That(cut.Find(".scene-card-meta").TextContent).Contains($"Scheduled for {due.ToLocalTime():MMM d, HH:mm}");
	}
}
