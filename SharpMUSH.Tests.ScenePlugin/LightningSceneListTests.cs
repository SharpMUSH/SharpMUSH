using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Scene.Storage;

namespace SharpMUSH.Tests.ScenePlugin;

/// <summary>
/// A cancelled scene (+scene/cancel) never ran: the scene lists leave it out, and only its own page still
/// opens it.
/// </summary>
public class LightningSceneListTests
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
	[Arguments("recent")]
	[Arguments("mine")]
	public async Task ACancelledScene_IsLeftOutOfTheLists(string filter)
	{
		var owner = $"#{(await _db.CreatePlayerAsync("Ines", "pw", new DBRef(0), new DBRef(0), 0)).Number}";
		var kept = await _scenes.CreateSceneAsync("#0", owner, "Kept");
		var cancelled = await _scenes.CreateSceneAsync("#0", owner, "Called off");
		await _scenes.AddMemberAsync(kept.Id, owner, "owner");
		await _scenes.AddMemberAsync(cancelled.Id, owner, "owner");
		await _scenes.SetSceneMetaAsync(cancelled.Id, "status", "cancelled");

		var listed = await _scenes.ListScenesAsync(filter, owner);

		await Assert.That(listed.Select(s => s.Id)).Contains(kept.Id);
		await Assert.That(listed.Select(s => s.Id)).DoesNotContain(cancelled.Id);
	}
}
