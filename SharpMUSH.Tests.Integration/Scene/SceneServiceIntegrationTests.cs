using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models.Scene;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration.Scenes;

/// <summary>
/// Integration tests for the graph-native Scene System against the configured DB backend.
/// <see cref="ISceneService"/> is provided by the Scene plugin's per-provider storage (Phase 8),
/// keyed to the active provider (selected by <c>SHARPMUSH_DATABASE_PROVIDER</c> — arangodb / memgraph /
/// surrealdb) and reaching the provider's connection through a host-shared storage accessor; these
/// tests must pass identically on all three. Object references use <c>#1</c> (the seeded God
/// object) so the resolve → edge → name-snapshot mechanism is exercised against a real vertex.
/// </summary>
[NotInParallel]
public class SceneServiceIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactory { get; init; }

	private ISceneService Scenes => WebAppFactory.Services.GetRequiredService<ISceneService>();

	private const string God = "#1"; // seeded object — used for owner/author/origin in these tests

	private async Task<Scene> NewSceneAsync(string title = "Test Scene")
		=> await Scenes.CreateSceneAsync(roomDbref: "", ownerDbref: God, title: title);

	// ── Scene CRUD + meta ───────────────────────────────────────────────────────

	[Test]
	public async Task CreateScene_AssignsId_AndSnapshotsOwnerName()
	{
		var scene = await NewSceneAsync($"Create {Guid.NewGuid():N}");

		await Assert.That(scene.Id).IsNotNull();
		await Assert.That(scene.Id).IsNotEmpty();
		await Assert.That(scene.OwnerName).IsNotEmpty(); // resolved + snapshotted from #1
	}

	[Test]
	public async Task GetScene_RoundTrips()
	{
		var created = await NewSceneAsync($"RoundTrip {Guid.NewGuid():N}");

		var got = await Scenes.GetSceneAsync(created.Id);

		await Assert.That(got.IsT0).IsTrue();
		await Assert.That(got.AsT0.Id).IsEqualTo(created.Id);
	}

	[Test]
	public async Task GetScene_Missing_ReturnsNotFound()
	{
		var got = await Scenes.GetSceneAsync($"does-not-exist-{Guid.NewGuid():N}");
		await Assert.That(got.IsT1).IsTrue();
	}

	[Test]
	public async Task SetSceneMeta_Status_RoutesToFirstClassField()
	{
		var scene = await NewSceneAsync();

		var updated = await Scenes.SetSceneMetaAsync(scene.Id, "status", "active");

		await Assert.That(updated.IsT0).IsTrue();
		await Assert.That(updated.AsT0.Status).IsEqualTo("active");
	}

	[Test]
	public async Task SetSceneMeta_CustomKey_RoutesToMetaBag()
	{
		var scene = await NewSceneAsync();

		var updated = await Scenes.SetSceneMetaAsync(scene.Id, "genre", "noir");

		await Assert.That(updated.IsT0).IsTrue();
		await Assert.That(updated.AsT0.Meta.ContainsKey("genre")).IsTrue();
		await Assert.That(updated.AsT0.Meta["genre"]).IsEqualTo("noir");
	}

	// ── Poses: order, content, edit/undo ─────────────────────────────────────────

	[Test]
	public async Task AddPoses_AreReturnedInChainOrder()
	{
		var scene = await NewSceneAsync();

		var p1 = await Scenes.AddPoseAsync(scene.Id, God, "", God, "pose", [], "First pose.");
		var p2 = await Scenes.AddPoseAsync(scene.Id, God, "", God, "pose", [], "Second pose.");
		await Assert.That(p1.IsT0).IsTrue();
		await Assert.That(p2.IsT0).IsTrue();

		var poses = await Scenes.GetPosesAsync(scene.Id);
		await Assert.That(poses.IsT0).IsTrue();
		await Assert.That(poses.AsT0.Count).IsEqualTo(2);
		await Assert.That(poses.AsT0[0].Content).Contains("First");
		await Assert.That(poses.AsT0[1].Content).Contains("Second");
	}

	[Test]
	public async Task EditPose_VersionsContent_AndUndoRestores()
	{
		var scene = await NewSceneAsync();
		var added = await Scenes.AddPoseAsync(scene.Id, God, "", God, "pose", [], "Original.");
		await Assert.That(added.IsT0).IsTrue();
		var poseId = added.AsT0.Id;

		var edited = await Scenes.EditPoseAsync(poseId, God, "Edited.");
		await Assert.That(edited.IsT0).IsTrue();
		await Assert.That(edited.AsT0.Content).Contains("Edited");
		await Assert.That(edited.AsT0.EditCount).IsGreaterThan(1);

		var undone = await Scenes.UndoPoseAsync(poseId);
		await Assert.That(undone.IsT0).IsTrue();
		await Assert.That(undone.AsT0.Content).Contains("Original");

		var redone = await Scenes.RedoPoseAsync(poseId);
		await Assert.That(redone.IsT0).IsTrue();
		await Assert.That(redone.AsT0.Content).Contains("Edited");
	}

	[Test]
	public async Task ShowAs_IsSnapshottedOnThePose()
	{
		var scene = await NewSceneAsync();

		var pose = await Scenes.AddPoseAsync(scene.Id, God, "Guard Captain", God, "pose", [], "stands watch.");

		await Assert.That(pose.IsT0).IsTrue();
		await Assert.That(pose.AsT0.ShowAsName).IsEqualTo("Guard Captain");
		await Assert.That(pose.AsT0.AuthorName).IsNotEqualTo("Guard Captain"); // author snapshot is the real player
	}

	[Test]
	public async Task DeletePose_SoftDeletes()
	{
		var scene = await NewSceneAsync();
		var added = await Scenes.AddPoseAsync(scene.Id, God, "", God, "pose", [], "Doomed.");
		await Assert.That(added.IsT0).IsTrue();

		var deleted = await Scenes.DeletePoseAsync(added.AsT0.Id);
		await Assert.That(deleted.IsT0).IsTrue();
		await Assert.That(deleted.AsT0.IsDeleted).IsTrue();
	}

	// ── Membership + focus ───────────────────────────────────────────────────────

	[Test]
	public async Task AddMember_ThenGetMembers_IncludesPlayer()
	{
		var scene = await NewSceneAsync();

		var member = await Scenes.AddMemberAsync(scene.Id, God, "participant");
		await Assert.That(member.IsT0).IsTrue();
		await Assert.That(member.AsT0.Role).IsEqualTo("participant");

		var members = await Scenes.GetMembersAsync(scene.Id);
		await Assert.That(members.IsT0).IsTrue();
		await Assert.That(members.AsT0.Count).IsGreaterThanOrEqualTo(1);
	}

	[Test]
	public async Task SetFocus_ThenGetCurrentScene_ReturnsTheScene()
	{
		var scene = await NewSceneAsync();
		await Scenes.AddMemberAsync(scene.Id, God, "participant");

		var focus = await Scenes.SetFocusAsync(God, scene.Id);
		await Assert.That(focus.IsT0).IsTrue();

		var current = await Scenes.GetCurrentSceneAsync(God);
		await Assert.That(current.IsT0).IsTrue();
		await Assert.That(current.AsT0.Id).IsEqualTo(scene.Id);
	}
}
