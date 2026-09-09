using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

public class ObjectsTests
{
	// relations: null — this fixture bypasses the host's Mediator cache, unlike the production wiring.
	private static LightningDatabase Create(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	private LightningDatabase _db = null!;
	private string _path = null!;

	[Before(Test)]
	public async Task Setup()
	{
		_path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		_db = Create(_path);
		await _db.Migrate();
	}

	[After(Test)]
	public async Task Cleanup()
	{
		await _db.DisposeAsync();
		if (Directory.Exists(_path))
		{
			try
			{
				Directory.Delete(_path, recursive: true);
			}
			catch (IOException)
			{
				// Best-effort, same as MigrationTests: a lingering mdb.lck can outlive the writer join.
			}
		}
	}

	[Test]
	public async Task CreateThingWiresNameOwnerLocationAndHome()
	{
		var room = (await _db.GetObjectNodeAsync(new DBRef(2))).Known.AsContainer;
		var god = (await _db.GetObjectNodeAsync(new DBRef(1))).Known.AsPlayer;
		var dbref = await _db.CreateThingAsync("Widget", room, god, room);
		var thing = (await _db.GetObjectNodeAsync(dbref)).Known;
		await Assert.That(thing.Object().Name).IsEqualTo("Widget");
		await Assert.That((await thing.AsContent.Location()).Object().DBRef.Number).IsEqualTo(2);
		await Assert.That((await thing.Object().Owner.WithCancellation(CancellationToken.None)).Object.DBRef.Number).IsEqualTo(1);
		await Assert.That(await _db.GetOwnedObjectCountAsync(god)).IsGreaterThanOrEqualTo(9);
	}

	[Test]
	public async Task OwnedObjectCountFollowsCreationAndDestruction()
	{
		var room = (await _db.GetObjectNodeAsync(new DBRef(2))).Known.AsContainer;
		var god = (await _db.GetObjectNodeAsync(new DBRef(1))).Known.AsPlayer;
		var before = await _db.GetOwnedObjectCountAsync(god);

		var first = await _db.CreateThingAsync("First", room, god, room);
		await _db.CreateThingAsync("Second", room, god, room);
		await Assert.That(await _db.GetOwnedObjectCountAsync(god)).IsEqualTo(before + 2);

		await _db.DeleteObjectAsync(first);
		await Assert.That(await _db.GetOwnedObjectCountAsync(god)).IsEqualTo(before + 1);
	}

	[Test]
	public async Task DeleteObjectRemovesEveryEdgeInBothDirections()
	{
		var room = (await _db.GetObjectNodeAsync(new DBRef(2))).Known.AsContainer;
		var god = (await _db.GetObjectNodeAsync(new DBRef(1))).Known.AsPlayer;
		var dbref = await _db.CreateThingAsync("Doomed", room, god, room);

		var n = dbref.Number;
		await _db.SetAttributeAsync(dbref, ["DESC"], MarkupText.Plain("x"), god);

		await _db.DeleteObjectAsync(dbref);

		await Assert.That((await _db.GetObjectNodeAsync(dbref)).IsNone).IsTrue();
		var leftovers = _db.Store.Read(tx => Tables.EdgePairs.Sum(p =>
			tx.Dups(p.Forward, Keys.Dbref(n)).Count() + tx.Dups(p.Reverse, Keys.Dbref(n)).Count())
			+ tx.Range(Tables.AttrMeta, Keys.AttrPrefix(n)).Count()
			+ tx.Range(Tables.RevLocation, Keys.Dbref(n)).Count());
		await Assert.That(leftovers).IsEqualTo(0);
	}
}
