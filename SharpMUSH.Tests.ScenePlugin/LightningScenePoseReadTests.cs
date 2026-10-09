using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Scene.Models;
using SharpMUSH.Plugins.Scene.Storage;
using TUnit.Assertions.Enums;

namespace SharpMUSH.Tests.ScenePlugin;

/// <summary>
/// Pose reads against a real LMDB world (#1460): a last-N read takes the scene's tail and stops, and a
/// pose's current version and version count are read by key rather than by walking its edit log — with
/// ordering, author filtering, deletion, moves and undo/redo unchanged.
/// </summary>
public class LightningScenePoseReadTests
{
	private string _path = null!;
	private LightningDatabase _db = null!;
	private LightningSceneStorage _scenes = null!;

	[Before(Test)]
	public async Task Open()
	{
		_path = Path.Join(Path.GetTempPath(), "sharpmush-scene-" + Guid.NewGuid().ToString("N"));
		_db = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = _path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);
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

	private async Task<string> NewPlayer(string name) => $"#{(await _db.CreatePlayerAsync(name, "pw", new DBRef(0), new DBRef(0), 0)).Number}";

	private async Task<string> Pose(string sceneId, string author, string content)
		=> Expect<ScenePose>(await _scenes.AddPoseAsync(sceneId, author, "", "#0", "ic", "pose", [], content)).Id;

	private async Task<IReadOnlyList<ScenePose>> Poses(string sceneId, string? author = null, int? count = null)
		=> Expect<IReadOnlyList<ScenePose>>(await _scenes.GetPosesAsync(sceneId, author, count));

	private async Task<List<ScenePose>> AllPages(string sceneId, int take, List<long?>? cursors = null)
	{
		var all = new List<ScenePose>();
		long? after = null;
		do
		{
			cursors?.Add(after);
			var page = Expect<ScenePosePage>(await _scenes.GetPosePageAsync(sceneId, after, take));
			await Assert.That(page.Poses.Count).IsLessThanOrEqualTo(Math.Max(1, take));
			all.AddRange(page.Poses);
			after = page.Next;
		} while (after is not null);

		return all;
	}

	/// <summary>
	/// Pages, joined, are the full list — order, deleted poses and current versions alike — whatever the page
	/// size, and a page reports a next cursor only when a pose follows it.
	/// </summary>
	[Test]
	public async Task PagesJoinToTheFullList()
	{
		var other = await NewPlayer("PageOther");
		var scene = (await _scenes.CreateSceneAsync("#0", "#1")).Id;
		var ids = new List<string>();
		for (var i = 0; i < 23; i++)
		{
			ids.Add(await Pose(scene, i % 4 == 0 ? other : "#1", $"pose {i}"));
		}

		await _scenes.DeletePoseAsync(ids[5]);
		Expect<ScenePose>(await _scenes.MovePoseAsync(ids[2], ids[17]));
		Expect<ScenePose>(await _scenes.EditPoseAsync(ids[9], "#1", "edited 9"));
		var full = await Poses(scene);

		foreach (var take in new[] { 0, 1, 5, 7, 22, 23, 100 })
		{
			var cursors = new List<long?>();
			var paged = await AllPages(scene, take, cursors);
			await Assert.That(paged).IsEquivalentTo(full, CollectionOrdering.Matching).Because($"page size {take}");
			await Assert.That(cursors.Count).IsEqualTo((full.Count + Math.Max(1, take) - 1) / Math.Max(1, take));
		}

		await Assert.That(await _scenes.GetPosePageAsync("9999", null, 10) is { Value: NotFound }).IsTrue();
	}

	/// <summary>Pages are separate reads: a pose added after one page was read is in a later page.</summary>
	[Test]
	public async Task APoseAddedBetweenPagesIsReached()
	{
		var scene = (await _scenes.CreateSceneAsync("#0", "#1")).Id;
		for (var i = 0; i < 4; i++) await Pose(scene, "#1", $"pose {i}");

		var first = Expect<ScenePosePage>(await _scenes.GetPosePageAsync(scene, null, 2));
		var late = await Pose(scene, "#1", "late");
		var rest = new List<ScenePose>();
		for (var after = first.Next; after is not null;)
		{
			var page = Expect<ScenePosePage>(await _scenes.GetPosePageAsync(scene, after, 2));
			rest.AddRange(page.Poses);
			after = page.Next;
		}

		await Assert.That(rest.Select(p => p.Content)).IsEquivalentTo(["pose 2", "pose 3", "late"], CollectionOrdering.Matching);
		await Assert.That(rest[^1].Id).IsEqualTo(late);
	}

	/// <summary>A pose keeps its type in lower case; an empty one is in character, and one that is not a key is refused.</summary>
	[Test]
	public async Task APoseKeepsItsType()
	{
		var scene = (await _scenes.CreateSceneAsync("#0", "#1")).Id;
		var radio = Expect<ScenePose>(await _scenes.AddPoseAsync(scene, "#1", "", "#0", " Radio ", "emit", [], "Calls in."));
		var plain = Expect<ScenePose>(await _scenes.AddPoseAsync(scene, "#1", "", "#0", "", "pose", [], "waves."));

		await Assert.That(radio.Type).IsEqualTo("radio");
		await Assert.That(plain.Type).IsEqualTo(PoseTypes.InCharacter);
		await Assert.That(Expect<Error<string>>(await _scenes.AddPoseAsync(scene, "#1", "", "#0", "not a key", "pose", [], "x")).Value)
			.IsEqualTo(PoseTypes.InvalidKey);

		Expect<ScenePose>(await _scenes.SetPoseMetaAsync(plain.Id, "type", "OOC"));
		await Assert.That((await Poses(scene)).Select(p => p.Type)).IsEquivalentTo(new[] { "radio", "ooc" }, CollectionOrdering.Matching);
	}

	/// <summary>Tags and cast, read off the pose records, are what the projected live poses give.</summary>
	[Test]
	public async Task TagsAndCastMatchTheLivePoses()
	{
		var other = await NewPlayer("CastOther");
		var scene = (await _scenes.CreateSceneAsync("#0", "#1")).Id;
		var first = Expect<ScenePose>(await _scenes.AddPoseAsync(scene, "#1", "", "#0", "ic", "pose", ["combat", "Combat", " "], "a")).Id;
		Expect<ScenePose>(await _scenes.AddPoseAsync(scene, other, "Masked", "#0", "ic", "pose", ["intrigue"], "b"));
		var gone = Expect<ScenePose>(await _scenes.AddPoseAsync(scene, other, "Ghost", "#0", "ic", "pose", ["deleted-only"], "c")).Id;
		Expect<ScenePose>(await _scenes.AddPoseAsync(scene, "#1", "", "#0", "ic", "pose", ["combat"], "d"));
		await _scenes.DeletePoseAsync(gone);
		Expect<ScenePose>(await _scenes.SetPoseMetaAsync(first, "tags", "renamed combat"));

		var live = (await Poses(scene)).Where(p => !p.IsDeleted).ToList();
		var tags = Expect<IReadOnlyList<string>>(await _scenes.GetTagsAsync(scene));
		var cast = Expect<IReadOnlyList<string>>(await _scenes.GetCastAsync(scene));

		await Assert.That(tags).IsEquivalentTo(live.SelectMany(p => p.Tags).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.Ordinal),
			CollectionOrdering.Matching);
		await Assert.That(tags).DoesNotContain("deleted-only");
		await Assert.That(cast).IsEquivalentTo(live.Select(p => string.IsNullOrEmpty(p.ShowAsName) ? p.AuthorName : p.ShowAsName)
			.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal), CollectionOrdering.Matching);
		await Assert.That(cast).Contains("Masked");
		await Assert.That(cast).DoesNotContain("Ghost");
		await Assert.That(await _scenes.GetTagsAsync("9999") is { Value: NotFound }).IsTrue();
	}

	[Test]
	public async Task TailIsTheSuffixOfTheFullList()
	{
		var other = await NewPlayer("PoseOther");
		var scene = (await _scenes.CreateSceneAsync("#0", "#1")).Id;
		var ids = new List<string>();
		for (var i = 0; i < 12; i++)
		{
			ids.Add(await Pose(scene, i % 3 == 0 ? other : "#1", $"pose {i}"));
		}

		await _scenes.DeletePoseAsync(ids[5]);
		Expect<ScenePose>(await _scenes.MovePoseAsync(ids[2], ids[9]));

		foreach (var author in new string?[] { null, "#1", other, "#999" })
		{
			var full = await Poses(scene, author);
			foreach (var count in new[] { 0, 1, 3, 8, 12, 50 })
			{
				var tail = await Poses(scene, author, count);
				await Assert.That(tail.Select(p => p.Id)).IsEquivalentTo(full.TakeLast(count).Select(p => p.Id), CollectionOrdering.Matching);
				await Assert.That(tail.Select(p => p.Content)).IsEquivalentTo(full.TakeLast(count).Select(p => p.Content), CollectionOrdering.Matching);
			}

			// A negative count is no limit, as before.
			await Assert.That((await Poses(scene, author, -1)).Select(p => p.Id)).IsEquivalentTo(full.Select(p => p.Id), CollectionOrdering.Matching);
		}

		var all = await Poses(scene);
		await Assert.That(all.Count).IsEqualTo(12);
		await Assert.That(all.Single(p => p.Id == ids[5]).IsDeleted).IsTrue();
		// Moved to just after pose 9: the tenth of twelve.
		await Assert.That(all[9].Id).IsEqualTo(ids[2]);
		await Assert.That(all[8].Id).IsEqualTo(ids[9]);
	}

	[Test]
	public async Task AuthorFilterResolvesTheLiveAuthor()
	{
		var gone = await NewPlayer("PoseGone");
		var scene = (await _scenes.CreateSceneAsync("#0", "#1")).Id;
		await Pose(scene, gone, "theirs");
		await Pose(scene, "#1", "mine");
		await _db.DeleteObjectAsync(DBRef.Parse(gone));

		await Assert.That(await Poses(scene, gone, 5)).IsEmpty();
		await Assert.That(await Poses(scene, gone)).IsEmpty();
		var tail = await Poses(scene, count: 5);
		await Assert.That(tail[0].AuthorDbref).IsNull();
		await Assert.That(tail[1].AuthorDbref).IsEqualTo("#1");
	}

	[Test]
	public async Task VersionsSurviveUndoRedoAndEditAfterUndo()
	{
		var scene = (await _scenes.CreateSceneAsync("#0", "#1")).Id;
		var pose = await Pose(scene, "#1", "v1");
		Expect<ScenePose>(await _scenes.EditPoseAsync(pose, "#1", "v2"));
		var three = Expect<ScenePose>(await _scenes.EditPoseAsync(pose, "#1", "v3"));
		await Assert.That(three.EditCount).IsEqualTo(3);

		var undone = Expect<ScenePose>(await _scenes.UndoPoseAsync(pose));
		await Assert.That((undone.Content, undone.EditCount)).IsEqualTo(("v2", 3));
		undone = Expect<ScenePose>(await _scenes.UndoPoseAsync(pose));
		await Assert.That((undone.Content, undone.EditCount)).IsEqualTo(("v1", 3));
		await Assert.That(await _scenes.UndoPoseAsync(pose) is Error<string>).IsTrue();
		var redone = Expect<ScenePose>(await _scenes.RedoPoseAsync(pose));
		await Assert.That((redone.Content, redone.EditCount)).IsEqualTo(("v2", 3));

		// An edit after an undo drops the versions past the pointer: v3 is gone, v4 is the newest.
		var edited = Expect<ScenePose>(await _scenes.EditPoseAsync(pose, "#1", "v4"));
		await Assert.That((edited.Content, edited.EditCount)).IsEqualTo(("v4", 3));
		await Assert.That(await _scenes.RedoPoseAsync(pose) is Error<string>).IsTrue();
		var history = Expect<IReadOnlyList<ScenePoseEdit>>(await _scenes.GetPoseEditsAsync(pose));
		await Assert.That(history.Select(e => e.Content)).IsEquivalentTo(["v1", "v2", "v4"], CollectionOrdering.Matching);

		var read = (await Poses(scene, count: 1)).Single();
		await Assert.That((read.Content, read.EditCount, read.LastEditorDbref)).IsEqualTo(("v4", 3, "#1"));

		var unedited = await Pose(scene, "#1", "once");
		var single = (await Poses(scene, count: 1)).Single();
		await Assert.That(single.Id).IsEqualTo(unedited);
		await Assert.That((single.EditCount, single.LastEditedAt, single.LastEditorDbref)).IsEqualTo((1, (long?)null, (string?)null));
	}

	/// <summary>
	/// Proof the tail and the projection read only what they need: the scene's oldest pose and a pose's
	/// superseded versions can be unreadable, and a last-N read still answers.
	/// </summary>
	[Test]
	public async Task TailAndProjectionReadOnlyWhatTheyNeed()
	{
		var scene = (await _scenes.CreateSceneAsync("#0", "#1")).Id;
		await Pose(scene, "#1", "oldest");
		var edited = await Pose(scene, "#1", "e1");
		Expect<ScenePose>(await _scenes.EditPoseAsync(edited, "#1", "e2"));
		Expect<ScenePose>(await _scenes.EditPoseAsync(edited, "#1", "e3"));
		await Pose(scene, "#1", "newest");

		var poses = _db.OpenTable("scene.pose", duplicates: false);
		var log = _db.OpenTable("scene.log", duplicates: false);
		await _db.WriteAsync(tx =>
		{
			tx.Put(poses, Keys.Composite(scene, "", 1u), "not json"u8);
			tx.Put(log, Keys.Composite(edited, "", 1u), "not json"u8);
			tx.Put(log, Keys.Composite(edited, "", 2u), "not json"u8);
			return 0;
		});

		var tail = await Poses(scene, count: 2);
		await Assert.That(tail.Select(p => p.Content)).IsEquivalentTo(["e3", "newest"], CollectionOrdering.Matching);
		await Assert.That(tail[0].EditCount).IsEqualTo(3);
	}
}
