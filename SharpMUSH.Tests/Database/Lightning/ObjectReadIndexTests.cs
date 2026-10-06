using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using TUnit.Assertions.Enums;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// Filtered searches seeded from the owner, zone, parent and type indexes (#1459): the same matches in
/// the same ascending dbref order, bounds and paging as a scan, with the candidates drawn from the index.
/// </summary>
public class ObjectReadIndexTests : LightningDatabaseFixture
{
	private async Task<AnySharpObject> Node(DBRef dbref) => (await Db.GetObjectNodeAsync(dbref)).Expect<AnySharpObject>();

	private async Task<SharpPlayer> NewPlayer(string name)
		=> (await Db.GetObjectNodeAsync(await Db.CreatePlayerAsync(name, "pw", new DBRef(0), new DBRef(0), 0))).Expect<SharpPlayer>();

	private async Task<int[]> Search(ObjectSearchFilter filter)
		=> (await Db.GetFilteredObjectsAsync(filter).ToListAsync()).Select(o => o.DBRef.Number).ToArray();

	private sealed record World(SharpPlayer A, SharpPlayer B, DBRef Zone, DBRef Parent, DBRef[] Things);

	/// <summary>Ten things, owned alternately by A and B, with a zone on 1, 2, 3, 7 and a parent on 2, 3, 8.</summary>
	private async Task<World> BuildWorld(string tag)
	{
		var a = await NewPlayer($"Owner{tag}A");
		var b = await NewPlayer($"Owner{tag}B");
		var room = (await Node(new DBRef(0))).AsContainer;
		var zone = await Db.CreateThingAsync($"Zone{tag}", room, a, room);
		var parent = await Db.CreateThingAsync($"Parent{tag}", room, a, room);
		var things = new DBRef[10];
		for (var i = 0; i < things.Length; i++)
		{
			things[i] = await Db.CreateThingAsync($"Seed{tag}{i}", room, i % 2 == 0 ? a : b, room);
			var thing = await Node(things[i]);
			if (i is 1 or 2 or 3 or 7) await Db.SetObjectZone(thing, await Node(zone));
			if (i is 2 or 3 or 8) await Db.SetObjectParent(thing, await Node(parent));
		}

		return new World(a, b, zone, parent, things);
	}

	[Test]
	public async Task OwnerSeededSearchesKeepScanSemantics()
	{
		var w = await BuildWorld("Own");
		int[] Things(params int[] i) => i.Select(n => w.Things[n].Number).ToArray();

		// A owns itself, the zone and parent objects and the even things, ascending.
		await Assert.That(await Search(new ObjectSearchFilter { Owner = w.A.Object.DBRef }))
			.IsEquivalentTo([w.A.Object.DBRef.Number, w.Zone.Number, w.Parent.Number, .. Things(0, 2, 4, 6, 8)], CollectionOrdering.Matching);
		await Assert.That(await Search(new ObjectSearchFilter { Owner = w.A.Object.DBRef, Types = [DatabaseConstants.TypeThing], NamePattern = "SeedOwn" }))
			.IsEquivalentTo(Things(0, 2, 4, 6, 8), CollectionOrdering.Matching);
		await Assert.That(await Search(new ObjectSearchFilter { Owner = w.B.Object.DBRef, Zone = w.Zone }))
			.IsEquivalentTo(Things(1, 3, 7), CollectionOrdering.Matching);
		await Assert.That(await Search(new ObjectSearchFilter { Owner = w.A.Object.DBRef, Zone = w.Zone, Parent = w.Parent }))
			.IsEquivalentTo(Things(2), CollectionOrdering.Matching);
		await Assert.That(await Search(new ObjectSearchFilter { Parent = w.Parent, Owner = w.B.Object.DBRef }))
			.IsEquivalentTo(Things(3), CollectionOrdering.Matching);
		await Assert.That(await Search(new ObjectSearchFilter { Owner = w.A.Object.DBRef, MinDbRef = w.Things[2].Number, MaxDbRef = w.Things[6].Number }))
			.IsEquivalentTo(Things(2, 4, 6), CollectionOrdering.Matching);
		await Assert.That(await Search(new ObjectSearchFilter { Owner = w.A.Object.DBRef, Types = [DatabaseConstants.TypeThing], NamePattern = "SeedOwn", Skip = 1, Limit = 2 }))
			.IsEquivalentTo(Things(2, 4), CollectionOrdering.Matching);
		await Assert.That(await Search(new ObjectSearchFilter { Owner = w.B.Object.DBRef, NamePattern = "^SeedOwn[37]$", UseRegex = true }))
			.IsEquivalentTo(Things(3, 7), CollectionOrdering.Matching);
		await Assert.That(await Search(new ObjectSearchFilter { Owner = w.A.Object.DBRef, Types = [DatabaseConstants.TypeRoom] }))
			.IsEmpty();
		await Assert.That(await Search(new ObjectSearchFilter { Owner = w.A.Object.DBRef, MinDbRef = -1 }))
			.IsEmpty();
	}

	[Test]
	public async Task TypeSeededSearchKeepsBoundsAndOrder()
	{
		var w = await BuildWorld("Typ");

		var players = await Search(new ObjectSearchFilter { Types = [DatabaseConstants.TypePlayer], MinDbRef = w.A.Object.DBRef.Number });

		await Assert.That(players).IsEquivalentTo([w.A.Object.DBRef.Number, w.B.Object.DBRef.Number], CollectionOrdering.Matching);
	}

	/// <summary>
	/// Proof the owner index is the candidate source: an object whose reverse owner entry is missing is not
	/// found by owner, while the name scan, which reads the object table, still sees it.
	/// </summary>
	[Test]
	public async Task OwnerSearchReadsTheReverseOwnerIndex()
	{
		var w = await BuildWorld("Proof");
		var hidden = w.Things[4];
		await Db.Store.WriteAsync(tx => tx.Delete(Tables.Owner.Reverse, Keys.Dbref(w.A.Object.DBRef.Number), Keys.Dbref(hidden.Number)));

		await Assert.That(await Search(new ObjectSearchFilter { Owner = w.A.Object.DBRef })).DoesNotContain(hidden.Number);
		await Assert.That(await Search(new ObjectSearchFilter { NamePattern = "SeedProof4" })).IsEquivalentTo([hidden.Number]);
	}

	/// <summary>The seed only narrows: every candidate is still checked against its forward edges.</summary>
	[Test]
	public async Task StaleReverseEntryDoesNotMatch()
	{
		var w = await BuildWorld("Stale");
		var odd = w.Things[1];
		await Db.Store.WriteAsync(tx => tx.Put(Tables.Owner.Reverse, Keys.Dbref(w.A.Object.DBRef.Number), Keys.Dbref(odd.Number)));

		await Assert.That(await Search(new ObjectSearchFilter { Owner = w.A.Object.DBRef })).DoesNotContain(odd.Number);
	}

	/// <summary>Across page boundaries, an object created ahead of the cursor is included and one deleted ahead of it is not.</summary>
	[Test]
	public async Task SeededSearchPagesAndSeesConcurrentMutation()
	{
		var owner = await NewPlayer("PagedOwner");
		var room = (await Node(new DBRef(0))).AsContainer;
		var things = new List<DBRef>();
		for (var i = 0; i < 300; i++)
		{
			things.Add(await Db.CreateThingAsync($"Paged{i}", room, owner, room));
		}

		var seen = new List<int>();
		DBRef? added = null;
		await foreach (var obj in Db.GetFilteredObjectsAsync(new ObjectSearchFilter { Owner = owner.Object.DBRef, Types = [DatabaseConstants.TypeThing] }))
		{
			seen.Add(obj.DBRef.Number);
			if (seen.Count == 10)
			{
				await Db.DeleteObjectAsync(things[290]);
				added = await Db.CreateThingAsync("PagedLate", room, owner, room);
			}
		}

		var expected = things.Where((_, i) => i != 290).Select(t => t.Number).Append(added!.Value.Number).ToArray();
		await Assert.That(seen).IsEquivalentTo(expected, CollectionOrdering.Matching);
	}

	[Test]
	public async Task TypeIndexFollowsCreateAndDelete()
	{
		var player = await NewPlayer("TypedPlayer");
		var room = (await Node(new DBRef(0))).AsContainer;
		var thing = await Db.CreateThingAsync("TypedThing", room, player, room);
		var newRoom = await Db.CreateRoomAsync("TypedRoom", player);
		var exit = await Db.CreateExitAsync("TypedExit", [], room, player);

		int[] OfType(string type) => Db.Store.Read(tx => tx.Range(Tables.ObjType, LightningDatabase.ObjTypeKey(type))
			.Select(e => (int)Keys.ReadDbref(e.Value)).ToArray());

		await Assert.That(OfType(DatabaseConstants.TypePlayer)).Contains(player.Object.DBRef.Number);
		await Assert.That(OfType(DatabaseConstants.TypeThing)).Contains(thing.Number);
		await Assert.That(OfType(DatabaseConstants.TypeRoom)).Contains(newRoom.Number);
		await Assert.That(OfType(DatabaseConstants.TypeExit)).Contains(exit.Number);

		await Db.DeleteObjectAsync(thing);
		await Assert.That(OfType(DatabaseConstants.TypeThing)).DoesNotContain(thing.Number);

		// Every row is indexed under its own type, and nothing else is.
		var all = await Db.GetAllObjectsAsync().ToListAsync();
		var indexed = Db.Store.Read(tx => tx.Range(Tables.ObjType, []).Count());
		await Assert.That(indexed).IsEqualTo(all.Count);
	}

	[Test]
	public async Task AllPlayersComesFromTheTypeIndex()
	{
		var one = await NewPlayer("ListedOne");
		var two = await NewPlayer("ListedTwo");
		var expected = (await Db.GetAllObjectsAsync().ToListAsync())
			.Where(o => o.Type == DatabaseConstants.TypePlayer)
			.Select(o => o.DBRef.Number)
			.ToArray();

		var players = (await Db.GetAllPlayersAsync().ToListAsync()).Select(p => p.Object.DBRef.Number).ToArray();
		await Assert.That(players).IsEquivalentTo(expected, CollectionOrdering.Matching);
		await Assert.That(players).Contains(one.Object.DBRef.Number);

		// Proof the index is what is read: a player missing from it is not listed.
		await Db.Store.WriteAsync(tx => tx.Delete(Tables.ObjType, LightningDatabase.ObjTypeKey(DatabaseConstants.TypePlayer),
			Keys.Dbref(two.Object.DBRef.Number)));
		await Assert.That((await Db.GetAllPlayersAsync().ToListAsync()).Select(p => p.Object.DBRef.Number)).DoesNotContain(two.Object.DBRef.Number);
	}
}
