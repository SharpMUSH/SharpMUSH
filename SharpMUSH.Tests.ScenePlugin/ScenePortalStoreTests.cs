using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Scene.Storage;

namespace SharpMUSH.Tests.ScenePlugin;

/// <summary>
/// Drafts and calendar links on a real Lightning world: a draft belongs to one character and one scene, and a
/// calendar link stops working once it is replaced or turned off.
/// </summary>
public class ScenePortalStoreTests
{
	private string _path = null!;
	private LightningDatabase _db = null!;
	private LightningScenePortalStore _store = null!;

	private const string Aria = "#15:1792044700905";
	private const string Bram = "#16:1792044700906";

	[Before(Test)]
	public async Task Open()
	{
		_path = Path.Join(Path.GetTempPath(), "sharpmush-scene-portal-" + Guid.NewGuid().ToString("N"));
		_db = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = _path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);
		await _db.Migrate();
		_store = new LightningScenePortalStore(_db);
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
	public async Task ADraft_IsKeptForOneCharacterInOneScene()
	{
		var draft = new SceneDraft("emit", "Aria shakes the rain off.", null, 1000);
		await _store.SaveDraftAsync(Aria, "1", draft);

		await Assert.That(_store.GetDraft(Aria, "1").Expect<SceneDraft>()).IsEqualTo(draft);
		await Assert.That(_store.GetDraft(Bram, "1") is SceneDraft).IsFalse();
		await Assert.That(_store.GetDraft(Aria, "2") is SceneDraft).IsFalse();
	}

	[Test]
	public async Task ASavedDraft_ReplacesTheOldOne_AndDeletingForgetsIt()
	{
		await _store.SaveDraftAsync(Aria, "1", new SceneDraft("emit", "first", null, 1000));
		await _store.SaveDraftAsync(Aria, "1", new SceneDraft("ooc", "second", "{}", 2000));

		await Assert.That(_store.GetDraft(Aria, "1").Expect<SceneDraft>()).IsEqualTo(new SceneDraft("ooc", "second", "{}", 2000));

		await _store.DeleteDraftAsync(Aria, "1");
		await Assert.That(_store.GetDraft(Aria, "1") is SceneDraft).IsFalse();
	}

	[Test]
	public async Task ACalendarLink_FindsItsCharacter_UntilItIsReplaced()
	{
		var first = await _store.CreateCalendarLinkAsync(Aria, 1000);
		await Assert.That(_store.FindCalendarOwner(first).Expect<string>()).IsEqualTo(Aria);
		await Assert.That(_store.GetCalendarLink(Aria).Expect<SceneCalendarLink>().CreatedAt).IsEqualTo(1000);

		var second = await _store.CreateCalendarLinkAsync(Aria, 2000);

		await Assert.That(second).IsNotEqualTo(first);
		await Assert.That(_store.FindCalendarOwner(first) is string).IsFalse();
		await Assert.That(_store.FindCalendarOwner(second).Expect<string>()).IsEqualTo(Aria);
		await Assert.That(_store.GetCalendarLink(Aria).Expect<SceneCalendarLink>().CreatedAt).IsEqualTo(2000);
	}

	[Test]
	public async Task ACalendarLinkTurnedOff_FindsNoOne()
	{
		var secret = await _store.CreateCalendarLinkAsync(Aria, 1000);
		await _store.DeleteCalendarLinkAsync(Aria);

		await Assert.That(_store.FindCalendarOwner(secret) is string).IsFalse();
		await Assert.That(_store.GetCalendarLink(Aria) is SceneCalendarLink).IsFalse();
	}

	[Test]
	public async Task AnUnknownSecret_FindsNoOne()
	{
		await _store.CreateCalendarLinkAsync(Aria, 1000);

		await Assert.That(_store.FindCalendarOwner("not-a-link") is string).IsFalse();
	}
}
