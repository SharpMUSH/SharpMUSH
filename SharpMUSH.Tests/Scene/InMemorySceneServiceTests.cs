using OneOf.Types;
using SharpMUSH.Library.Models.Scene;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Scene;

/// <summary>
/// Unit tests for <see cref="InMemorySceneService"/>.
/// All tests run against a fresh service instance to ensure isolation.
/// </summary>
public class InMemorySceneServiceTests
{
	// ── Helpers ──────────────────────────────────────────────────────────────

	private static ISceneService BuildService() => new InMemorySceneService();

	/// <summary>
	/// Opens a scene and asserts success, returning the <see cref="SceneArchive"/>.
	/// </summary>
	private static async Task<SceneArchive> OpenSceneAsync(
		ISceneService svc,
		string roomDbref = "#100",
		string roomName  = "The Town Square",
		string title     = "Test Scene",
		bool   isPublic  = true)
	{
		var scene = await svc.OpenSceneAsync(roomDbref, roomName, title, isPublic);
		await Assert.That(scene.Id).IsNotEmpty();
		return scene;
	}

	/// <summary>
	/// Posts a message and asserts success, returning the <see cref="SceneMessage"/>.
	/// </summary>
	private static async Task<SceneMessage> PostMessageAsync(
		ISceneService svc,
		string sceneId,
		string authorDbref = "#1",
		string authorName  = "Alice",
		string content     = "waves cheerfully.",
		string html        = "<span class=\"pose\">Alice waves cheerfully.</span>",
		SceneMessageType type = SceneMessageType.Pose)
	{
		var result = await svc.PostMessageAsync(sceneId, authorDbref, authorName, content, html, type);
		await Assert.That(result.IsT0).IsTrue();
		return result.AsT0;
	}

	// ── OpenSceneAsync ────────────────────────────────────────────────────────

	[Test]
	public async Task OpenSceneAsync_ReturnsSceneWithAssignedId()
	{
		var svc   = BuildService();
		var scene = await OpenSceneAsync(svc);

		await Assert.That(scene.Id).IsNotNull();
		await Assert.That(scene.Id).IsNotEmpty();
	}

	[Test]
	public async Task OpenSceneAsync_StoresRoomDbrefAndName()
	{
		var svc   = BuildService();
		var scene = await svc.OpenSceneAsync("#42", "The Grand Ballroom", "Ball Scene");

		await Assert.That(scene.RoomDbref).IsEqualTo("#42");
		await Assert.That(scene.RoomName).IsEqualTo("The Grand Ballroom");
	}

	[Test]
	public async Task OpenSceneAsync_ClosedAtIsNull_WhenJustOpened()
	{
		var svc   = BuildService();
		var scene = await OpenSceneAsync(svc);

		await Assert.That(scene.ClosedAt).IsNull();
	}

	[Test]
	public async Task OpenSceneAsync_StartsWithEmptyParticipants()
	{
		var svc   = BuildService();
		var scene = await OpenSceneAsync(svc);

		await Assert.That(scene.ParticipantDbrefs.Count).IsEqualTo(0);
	}

	[Test]
	public async Task OpenSceneAsync_MultipleScenes_HaveDistinctIds()
	{
		var svc = BuildService();
		var s1  = await OpenSceneAsync(svc, title: "Scene A");
		var s2  = await OpenSceneAsync(svc, title: "Scene B");

		await Assert.That(s1.Id).IsNotEqualTo(s2.Id);
	}

	// ── CloseSceneAsync ───────────────────────────────────────────────────────

	[Test]
	public async Task CloseSceneAsync_SetsClosedAt()
	{
		var svc    = BuildService();
		var scene  = await OpenSceneAsync(svc);
		var before = DateTimeOffset.UtcNow;

		var result = await svc.CloseSceneAsync(scene.Id);

		await Assert.That(result.IsT0).IsTrue();
		var closed = result.AsT0;
		await Assert.That(closed.ClosedAt).IsNotNull();
		await Assert.That(closed.ClosedAt!.Value).IsGreaterThanOrEqualTo(before);
	}

	[Test]
	public async Task CloseSceneAsync_UnknownId_ReturnsNotFound()
	{
		var svc    = BuildService();
		var result = await svc.CloseSceneAsync("scene_does_not_exist");

		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(result.Value).IsTypeOf<NotFound>();
	}

	// ── GetSceneAsync ─────────────────────────────────────────────────────────

	[Test]
	public async Task GetSceneAsync_ReturnsExistingScene()
	{
		var svc   = BuildService();
		var scene = await OpenSceneAsync(svc, title: "Stored Scene");

		var result = await svc.GetSceneAsync(scene.Id);

		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(result.AsT0.Title).IsEqualTo("Stored Scene");
	}

	[Test]
	public async Task GetSceneAsync_UnknownId_ReturnsNotFound()
	{
		var svc    = BuildService();
		var result = await svc.GetSceneAsync("nonexistent");

		await Assert.That(result.IsT1).IsTrue();
	}

	// ── UpdateSceneMetaAsync ──────────────────────────────────────────────────

	[Test]
	public async Task UpdateSceneMetaAsync_ChangesTitle()
	{
		var svc   = BuildService();
		var scene = await OpenSceneAsync(svc, title: "Old Title");

		var result = await svc.UpdateSceneMetaAsync(scene.Id, title: "New Title");

		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(result.AsT0.Title).IsEqualTo("New Title");
	}

	[Test]
	public async Task UpdateSceneMetaAsync_ChangesDescription()
	{
		var svc   = BuildService();
		var scene = await OpenSceneAsync(svc);

		var result = await svc.UpdateSceneMetaAsync(scene.Id, description: "A tense confrontation.");

		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(result.AsT0.Description).IsEqualTo("A tense confrontation.");
	}

	[Test]
	public async Task UpdateSceneMetaAsync_NullTitle_PreservesExistingTitle()
	{
		var svc   = BuildService();
		var scene = await OpenSceneAsync(svc, title: "Keep This");

		var result = await svc.UpdateSceneMetaAsync(scene.Id, title: null, description: "New desc");

		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(result.AsT0.Title).IsEqualTo("Keep This");
	}

	[Test]
	public async Task UpdateSceneMetaAsync_UnknownId_ReturnsNotFound()
	{
		var svc    = BuildService();
		var result = await svc.UpdateSceneMetaAsync("ghost_id", title: "X");

		await Assert.That(result.IsT1).IsTrue();
	}

	// ── GetRecentScenesAsync ──────────────────────────────────────────────────

	[Test]
	public async Task GetRecentScenesAsync_ReturnsOnlyClosedScenes()
	{
		var svc    = BuildService();
		var open   = await OpenSceneAsync(svc, title: "Active");
		var toClose = await OpenSceneAsync(svc, title: "Closed");
		await svc.CloseSceneAsync(toClose.Id);

		var result = await svc.GetRecentScenesAsync(10);

		await Assert.That(result.Count).IsEqualTo(1);
		await Assert.That(result[0].Title).IsEqualTo("Closed");

		// Silence "open" unreferenced warning — it is intentionally not closed
		_ = open;
	}

	[Test]
	public async Task GetRecentScenesAsync_OrdersByClosedAtDescending()
	{
		var svc = BuildService();
		var s1  = await OpenSceneAsync(svc, title: "Scene 1");
		var s2  = await OpenSceneAsync(svc, title: "Scene 2");

		await svc.CloseSceneAsync(s1.Id);
		await Task.Delay(5); // ensure distinct timestamps
		await svc.CloseSceneAsync(s2.Id);

		var result = await svc.GetRecentScenesAsync(10);

		// s2 closed last → should be first (most recent)
		await Assert.That(result[0].Title).IsEqualTo("Scene 2");
		await Assert.That(result[1].Title).IsEqualTo("Scene 1");
	}

	// ── GetActiveScenesAsync ──────────────────────────────────────────────────

	[Test]
	public async Task GetActiveScenesAsync_ReturnsOnlyOpenScenes()
	{
		var svc    = BuildService();
		var active = await OpenSceneAsync(svc, title: "Running");
		var closed = await OpenSceneAsync(svc, title: "Finished");
		await svc.CloseSceneAsync(closed.Id);

		var result = await svc.GetActiveScenesAsync();

		await Assert.That(result.Count).IsEqualTo(1);
		await Assert.That(result[0].Id).IsEqualTo(active.Id);
	}

	// ── PostMessageAsync ──────────────────────────────────────────────────────

	[Test]
	public async Task PostMessageAsync_ReturnsMessageWithAssignedId()
	{
		var svc   = BuildService();
		var scene = await OpenSceneAsync(svc);
		var msg   = await PostMessageAsync(svc, scene.Id);

		await Assert.That(msg.Id).IsNotEmpty();
		await Assert.That(msg.SceneId).IsEqualTo(scene.Id);
	}

	[Test]
	public async Task PostMessageAsync_StoresAuthorAndContent()
	{
		var svc   = BuildService();
		var scene = await OpenSceneAsync(svc);

		var msg = await PostMessageAsync(svc, scene.Id,
			authorDbref: "#5",
			authorName:  "Bob",
			content:     "grins wickedly.",
			html:        "<em>Bob grins wickedly.</em>",
			type:        SceneMessageType.Pose);

		await Assert.That(msg.AuthorDbref).IsEqualTo("#5");
		await Assert.That(msg.AuthorName).IsEqualTo("Bob");
		await Assert.That(msg.Content).IsEqualTo("grins wickedly.");
		await Assert.That(msg.RenderedHtml).IsEqualTo("<em>Bob grins wickedly.</em>");
		await Assert.That(msg.MessageType).IsEqualTo(SceneMessageType.Pose);
	}

	[Test]
	public async Task PostMessageAsync_AddsAuthorToParticipants()
	{
		var svc   = BuildService();
		var scene = await OpenSceneAsync(svc);
		await PostMessageAsync(svc, scene.Id, authorDbref: "#7");

		var updated = (await svc.GetSceneAsync(scene.Id)).AsT0;

		await Assert.That(updated.ParticipantDbrefs).Contains("#7");
	}

	[Test]
	public async Task PostMessageAsync_SameAuthorTwice_OnlyAddedOnceToParticipants()
	{
		var svc   = BuildService();
		var scene = await OpenSceneAsync(svc);
		await PostMessageAsync(svc, scene.Id, authorDbref: "#3");
		await PostMessageAsync(svc, scene.Id, authorDbref: "#3");

		var updated = (await svc.GetSceneAsync(scene.Id)).AsT0;

		await Assert.That(updated.ParticipantDbrefs.Count(d => d == "#3")).IsEqualTo(1);
	}

	[Test]
	public async Task PostMessageAsync_ToClosedScene_ReturnsError()
	{
		var svc   = BuildService();
		var scene = await OpenSceneAsync(svc);
		await svc.CloseSceneAsync(scene.Id);

		var result = await svc.PostMessageAsync(scene.Id, "#1", "Alice",
			"arrives late.", "<p>Alice arrives late.</p>", SceneMessageType.System);

		await Assert.That(result.IsT2).IsTrue();
		await Assert.That(result.AsT2.Value).Contains(scene.Id);
	}

	[Test]
	public async Task PostMessageAsync_ToUnknownScene_ReturnsNotFound()
	{
		var svc    = BuildService();
		var result = await svc.PostMessageAsync("ghost_scene", "#1", "Alice",
			"text", "<p>text</p>");

		await Assert.That(result.IsT1).IsTrue();
	}

	// ── GetMessagesAsync ──────────────────────────────────────────────────────

	[Test]
	public async Task GetMessagesAsync_ReturnsMessagesInChronologicalOrder()
	{
		var svc   = BuildService();
		var scene = await OpenSceneAsync(svc);

		await PostMessageAsync(svc, scene.Id, authorName: "Alice", content: "first");
		await Task.Delay(5);
		await PostMessageAsync(svc, scene.Id, authorName: "Bob",   content: "second");

		var result = await svc.GetMessagesAsync(scene.Id);

		await Assert.That(result.IsT0).IsTrue();
		var msgs = result.AsT0;
		await Assert.That(msgs.Count).IsEqualTo(2);
		await Assert.That(msgs[0].Content).IsEqualTo("first");
		await Assert.That(msgs[1].Content).IsEqualTo("second");
	}

	[Test]
	public async Task GetMessagesAsync_EmptyScene_ReturnsEmptyList()
	{
		var svc   = BuildService();
		var scene = await OpenSceneAsync(svc);

		var result = await svc.GetMessagesAsync(scene.Id);

		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(result.AsT0.Count).IsEqualTo(0);
	}

	[Test]
	public async Task GetMessagesAsync_UnknownScene_ReturnsNotFound()
	{
		var svc    = BuildService();
		var result = await svc.GetMessagesAsync("nonexistent_scene");

		await Assert.That(result.IsT1).IsTrue();
	}

	// ── GetRecentMessagesAsync ────────────────────────────────────────────────

	[Test]
	public async Task GetRecentMessagesAsync_ReturnsAtMostCount()
	{
		var svc   = BuildService();
		var scene = await OpenSceneAsync(svc);

		for (var i = 0; i < 10; i++)
			await PostMessageAsync(svc, scene.Id, content: $"line {i}");

		var result = await svc.GetRecentMessagesAsync(scene.Id, count: 5);

		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(result.AsT0.Count).IsEqualTo(5);
	}

	[Test]
	public async Task GetRecentMessagesAsync_ReturnsLastNMessages_OldestFirst()
	{
		var svc   = BuildService();
		var scene = await OpenSceneAsync(svc);

		for (var i = 0; i < 8; i++)
		{
			await PostMessageAsync(svc, scene.Id, content: $"msg {i}");
			await Task.Delay(2);
		}

		var result = await svc.GetRecentMessagesAsync(scene.Id, count: 3);

		await Assert.That(result.IsT0).IsTrue();
		var msgs = result.AsT0;
		await Assert.That(msgs.Count).IsEqualTo(3);
		// Should be the last 3 messages (5, 6, 7) in ascending order
		await Assert.That(msgs[0].Content).IsEqualTo("msg 5");
		await Assert.That(msgs[1].Content).IsEqualTo("msg 6");
		await Assert.That(msgs[2].Content).IsEqualTo("msg 7");
	}

	[Test]
	public async Task GetRecentMessagesAsync_UnknownScene_ReturnsNotFound()
	{
		var svc    = BuildService();
		var result = await svc.GetRecentMessagesAsync("ghost_scene");

		await Assert.That(result.IsT1).IsTrue();
	}
}
