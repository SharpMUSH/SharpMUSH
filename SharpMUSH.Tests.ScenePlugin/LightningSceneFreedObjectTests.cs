using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Scene.Models;
using SharpMUSH.Plugins.Scene.Storage;

namespace SharpMUSH.Tests.ScenePlugin;

/// <summary>
/// A freed object's number can pass to a new object, so the scene store lets go of it first: the new holder of
/// the number inherits no scene, seat or pose, and the log keeps the name each pose was written under.
/// </summary>
public class LightningSceneFreedObjectTests
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

	[Test]
	public async Task TheNextHolderOfAFreedNumberInheritsNoScene()
	{
		var freed = await _db.CreatePlayerAsync("Leaver", "pw", new DBRef(0), new DBRef(0), 0, creationTime: 1000);
		var leaver = $"#{freed.Number}";
		var scene = await _scenes.CreateSceneAsync("#0", leaver, "Left behind");
		await _scenes.AddMemberAsync(scene.Id, leaver, "owner");
		await _scenes.AddMemberAsync(scene.Id, "#1", "participant");
		var pose = (await _scenes.AddPoseAsync(scene.Id, leaver, "Leaver", leaver, "pose", "game", [], "waves."))
			.Expect<ScenePose>();

		await _db.DeleteObjectAsync(freed);
		await _scenes.ObjectsFreedAsync([freed]);
		await _db.CreatePlayerAsync("Arrival", "pw", new DBRef(0), new DBRef(0), 0, creationTime: 2000,
			requestedDbref: freed.Number);

		await Assert.That((await _scenes.ListScenesAsync("mine", leaver)).Count).IsEqualTo(0);
		await Assert.That(await _scenes.GetMemberAsync(scene.Id, leaver) is SharpMUSH.Library.DiscriminatedUnions.NotFound).IsTrue();
		await Assert.That((await _scenes.GetSceneAsync(scene.Id)).Expect<Scene>().OwnerDbref).IsNull();
		var kept = (await _scenes.GetPoseAsync(pose.Id)).Expect<ScenePose>();
		await Assert.That(kept.AuthorDbref).IsNull();
		await Assert.That(kept.AuthorName).IsEqualTo("Leaver");
	}
}
