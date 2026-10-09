using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Scene.Models;
using SharpMUSH.Plugins.Scene.Storage;
using Scene = SharpMUSH.Plugins.Scene.Models.Scene;

namespace SharpMUSH.Tests.ScenePlugin;

/// <summary>
/// Retention over the Scene System's histories (#1464), against a real LMDB world: pose edit logs are
/// bounded without ever losing the version a pose shows or one a redo can reach, and a soft-deleted pose
/// is hard-purged — pose row, id index and edit log together — once it has been deleted long enough.
/// </summary>
public class LightningSceneHistoryRetentionTests
{
	private string _path = null!;
	private LightningDatabase _db = null!;
	private LightningSceneStorage _scenes = null!;

	[Before(Test)]
	public async Task Open()
	{
		_path = Path.Join(Path.GetTempPath(), "sharpmush-scene-" + Guid.NewGuid().ToString("N"));
		_db = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = _path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(),
			relations: null);
		await _db.Migrate();
		_scenes = new LightningSceneStorage(_db);
	}

	[After(Test)]
	public async Task Close()
	{
		await _db.DisposeAsync();
		try
		{
			if (Directory.Exists(_path)) Directory.Delete(_path, recursive: true);
		}
		catch (IOException)
		{
			// Best-effort: a lingering mdb.lck can outlive the writer join.
		}
	}

	/// <summary>The case a test expects, read the way Tests.Infrastructure's <c>Expect</c> does.</summary>
	private static T Expect<T>(System.Runtime.CompilerServices.IUnion union) => union.Value switch
	{
		T value => value,
		var other => throw new InvalidOperationException($"Expected a {typeof(T).Name}, but the result was {other}.")
	};

	private sealed class FrozenClock(DateTimeOffset now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => now;
	}

	private HistoryRetentionService Retention(DateTimeOffset now, HistoryRetentionRule edits, HistoryRetentionRule deleted,
		int batch = 256)
		=> new([_scenes.EditHistory, _scenes.DeletedPoseHistory],
			new HistoryRetentionOptions
			{
				Rules = new Dictionary<string, HistoryRetentionRule> { ["scene.edits"] = edits, ["scene.deleted"] = deleted },
				BatchSize = batch
			},
			NullLogger<HistoryRetentionService>.Instance,
			new FrozenClock(now));

	/// <summary>A pose edited until it has <paramref name="versions"/> versions, v1 to vN.</summary>
	private async Task<(string Scene, string Pose)> PoseWithVersionsAsync(int versions, string? sceneId = null)
	{
		sceneId ??= (await _scenes.CreateSceneAsync("", "#1", "History")).Id;
		var pose = Expect<ScenePose>(await _scenes.AddPoseAsync(sceneId, "#1", "", "", "ic", "pose", [], "v1"));
		for (var n = 2; n <= versions; n++)
		{
			Expect<ScenePose>(await _scenes.EditPoseAsync(pose.Id, "#1", $"v{n}"));
		}

		return (sceneId, pose.Id);
	}

	private async Task<IReadOnlyList<string>> VersionsAsync(string poseId)
		=> Expect<IReadOnlyList<ScenePoseEdit>>(await _scenes.GetPoseEditsAsync(poseId)).Select(e => e.Content).ToList();

	/// <summary>Raw rows under a pose's id in a plugin table: what is physically left in the live world.</summary>
	private int RowsFor(string table, string poseId)
	{
		var def = _db.OpenTable(table, duplicates: false);
		return _db.Read(tx => tx.Range(def, Keys.Concat(Keys.Str(poseId), Keys.Sep)).Count());
	}

	[Test]
	public async Task TheDefaultPolicyKeepsEveryVersion()
	{
		var (_, pose) = await PoseWithVersionsAsync(5);

		var outcomes = await Retention(DateTimeOffset.UtcNow.AddYears(10), HistoryRetentionRule.KeepEverything,
			HistoryRetentionRule.KeepEverything).PurgeAsync();

		await Assert.That(outcomes.All(o => o is HistoryKeptEverything)).IsTrue();
		await Assert.That(await VersionsAsync(pose)).IsEquivalentTo(["v1", "v2", "v3", "v4", "v5"]);
	}

	/// <summary>
	/// Keep the newest two: the older versions leave the live table, the pose still shows its text and
	/// still reports itself edited, and undo walks back only as far as what survives.
	/// </summary>
	[Test]
	public async Task KeepNewestBoundsAPosesLogAndUndoStopsAtWhatSurvives()
	{
		var (_, pose) = await PoseWithVersionsAsync(5);

		var outcome = (await Retention(DateTimeOffset.UtcNow, new HistoryRetentionRule { KeepNewest = 2 },
			HistoryRetentionRule.KeepEverything).PurgeAsync()).First();

		await Assert.That(Expect<HistoryPurged>(outcome).Records).IsEqualTo(3);
		await Assert.That(await VersionsAsync(pose)).IsEquivalentTo(["v4", "v5"]);
		await Assert.That(RowsFor("scene.log", pose)).IsEqualTo(2);

		var shown = Expect<ScenePose>(await _scenes.GetPoseAsync(pose));
		await Assert.That(shown.Content).IsEqualTo("v5");
		await Assert.That(shown.LastEditorDbref).IsEqualTo("#1")
			.Because("a pose showing a version past its first was edited, however much history survives");

		await Assert.That(Expect<ScenePose>(await _scenes.UndoPoseAsync(pose)).Content).IsEqualTo("v4");
		await Assert.That(Expect<Error<string>>(await _scenes.UndoPoseAsync(pose)).Value).Contains("oldest");

		// Editing after the purge numbers on from the pointer, never into a purged slot.
		Expect<ScenePose>(await _scenes.EditPoseAsync(pose, "#1", "v6"));
		await Assert.That(await VersionsAsync(pose)).IsEquivalentTo(["v4", "v6"]);
	}

	/// <summary>
	/// The version a pose shows and everything after it are pinned: after an undo, the redo-forward
	/// versions survive even a rule that would purge every version, and redo still reaches them.
	/// </summary>
	[Test]
	public async Task TheCurrentVersionAndRedoForwardVersionsArePinned()
	{
		var (_, pose) = await PoseWithVersionsAsync(5);
		Expect<ScenePose>(await _scenes.UndoPoseAsync(pose));
		Expect<ScenePose>(await _scenes.UndoPoseAsync(pose));

		await Retention(DateTimeOffset.UtcNow.AddDays(30), new HistoryRetentionRule { MaxAge = TimeSpan.FromDays(1) },
			HistoryRetentionRule.KeepEverything).PurgeAsync();

		await Assert.That(await VersionsAsync(pose)).IsEquivalentTo(["v3", "v4", "v5"]);
		await Assert.That(Expect<ScenePose>(await _scenes.GetPoseAsync(pose)).Content).IsEqualTo("v3");
		await Assert.That(Expect<ScenePose>(await _scenes.RedoPoseAsync(pose)).Content).IsEqualTo("v4");
		await Assert.That(Expect<ScenePose>(await _scenes.RedoPoseAsync(pose)).Content).IsEqualTo("v5");
	}

	/// <summary>
	/// A soft-deleted pose is hard-purged once it has been deleted longer than the bound: the pose row, its
	/// id index entry and its whole log go together, the poses around it keep their order and the scene's
	/// count, and a pose deleted more recently — or never deleted — is untouched.
	/// </summary>
	[Test]
	public async Task ADeletedPoseIsPurgedWithItsIndexAndLogOnceOldEnough()
	{
		var (scene, first) = await PoseWithVersionsAsync(1);
		var (_, doomed) = await PoseWithVersionsAsync(3, scene);
		var (_, last) = await PoseWithVersionsAsync(1, scene);
		Expect<ScenePose>(await _scenes.DeletePoseAsync(doomed));
		var poseCount = Expect<Scene>(await _scenes.GetSceneAsync(scene)).PoseCount;

		// A day later the deletion is too young for a two-day bound; three days later it is not.
		var rule = new HistoryRetentionRule { MaxAge = TimeSpan.FromDays(2) };
		await Retention(DateTimeOffset.UtcNow.AddDays(1), HistoryRetentionRule.KeepEverything, rule).PurgeAsync();
		await Assert.That((await _scenes.GetPoseAsync(doomed)).Value).IsTypeOf<ScenePose>();

		var outcome = (await Retention(DateTimeOffset.UtcNow.AddDays(3), HistoryRetentionRule.KeepEverything, rule)
			.PurgeAsync()).Last();

		await Assert.That(Expect<HistoryPurged>(outcome).Records).IsEqualTo(4).Because("the pose and its three versions");
		await Assert.That((await _scenes.GetPoseAsync(doomed)).Value).IsTypeOf<NotFound>();
		await Assert.That((await _scenes.GetPoseEditsAsync(doomed)).Value).IsTypeOf<NotFound>();
		await Assert.That(RowsFor("scene.log", doomed)).IsEqualTo(0);
		await Assert.That(_db.Read(tx => tx.TryGet(_db.OpenTable("scene.pose.idx", false), Keys.Str(doomed), out _)))
			.IsFalse();

		var remaining = Expect<IReadOnlyList<ScenePose>>(await _scenes.GetPosesAsync(scene)).Select(p => p.Id).ToList();
		await Assert.That(remaining).IsEquivalentTo([first, last]);
		await Assert.That(Expect<Scene>(await _scenes.GetSceneAsync(scene)).PoseCount).IsEqualTo(poseCount);
	}

	/// <summary>
	/// A move between a pass's read and its write renumbers the scene's poses, so the key a candidate was
	/// read under can hold another deleted pose. That pose is not the one archived, and it stays.
	/// </summary>
	[Test]
	public async Task APoseMovedIntoAPurgedSlotIsNotPurgedInsteadOfIt()
	{
		var (scene, archived) = await PoseWithVersionsAsync(1);
		var (_, other) = await PoseWithVersionsAsync(1, scene);
		Expect<ScenePose>(await _scenes.DeletePoseAsync(archived));
		Expect<ScenePose>(await _scenes.DeletePoseAsync(other));
		var rule = new HistoryRetentionRule { MaxAge = TimeSpan.FromDays(1) };
		var now = DateTimeOffset.UtcNow.AddDays(2);

		var batch = await _scenes.DeletedPoseHistory.FindPurgeableAsync(rule, now, [], limit: 1);
		await Assert.That(batch.Candidates.Count).IsEqualTo(1);
		await Assert.That(System.Text.Encoding.UTF8.GetString(batch.Candidates[0].Archive)).Contains(archived);
		Expect<ScenePose>(await _scenes.MovePoseAsync(other, ""));

		var (records, _) = await _scenes.DeletedPoseHistory.PurgeAsync(batch, rule, now);

		await Assert.That(records).IsEqualTo(0);
		await Assert.That(RowsFor("scene.log", other)).IsEqualTo(1);
		await Assert.That(_db.Read(tx => tx.TryGet(_db.OpenTable("scene.pose.idx", false), Keys.Str(other), out _)))
			.IsTrue();
	}

	[Test]
	public async Task UsageCountsEditsAndDeletedPoses()
	{
		var (scene, _) = await PoseWithVersionsAsync(3);
		var (_, deleted) = await PoseWithVersionsAsync(2, scene);
		Expect<ScenePose>(await _scenes.DeletePoseAsync(deleted));

		var edits = await _scenes.EditHistory.MeasureAsync();
		var soft = await _scenes.DeletedPoseHistory.MeasureAsync();

		await Assert.That((edits.Kind, edits.Holders, edits.Records)).IsEqualTo(("scene.edits", 2L, 5L));
		await Assert.That((soft.Kind, soft.Holders, soft.Records)).IsEqualTo(("scene.deleted", 1L, 3L));
		await Assert.That(soft.Bytes).IsGreaterThan(0);
	}

	/// <summary>A long log is purged across many small write transactions, and ends where one big pass would.</summary>
	[Test]
	public async Task ALongLogIsPurgedInBoundedBatches()
	{
		var (_, pose) = await PoseWithVersionsAsync(12);

		var purged = Expect<HistoryPurged>((await Retention(DateTimeOffset.UtcNow,
			new HistoryRetentionRule { KeepNewest = 1 }, HistoryRetentionRule.KeepEverything, batch: 3).PurgeAsync()).First());

		await Assert.That(purged.Records).IsEqualTo(11);
		await Assert.That(purged.Batches).IsGreaterThanOrEqualTo(4);
		await Assert.That(await VersionsAsync(pose)).IsEquivalentTo(["v12"]);

		// One version left, and it is not the first: the pose was still edited, and still says by whom.
		var shown = Expect<ScenePose>(await _scenes.GetPoseAsync(pose));
		await Assert.That(shown.Content).IsEqualTo("v12");
		await Assert.That(shown.LastEditorDbref).IsEqualTo("#1");
	}
}
