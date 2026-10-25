using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using TUnit.Assertions.Enums;

namespace SharpMUSH.Tests.Database.Lightning;

public partial class NavigationTests
{
	private async Task<AnySharpContainer> NewRoom(string name)
		=> (await Node(await _db.CreateRoomAsync(name, await God()))).AsOptionalContainer.Expect<AnySharpContainer>();

	private async Task<AnySharpContent> NewThing(string name, AnySharpContainer room)
		=> (await Node(await _db.CreateThingAsync(name, room, await God(), room))).AsOptionalContent.Expect<AnySharpContent>();

	private async Task<int[]> ContentNumbers(AnySharpContainer room)
		=> await _db.GetContentRefsAsync(room.Object().DBRef).Select(c => c.Number).ToArrayAsync();

	private static int Number(AnySharpContent content) => content.Object().DBRef.Number;

	[Test]
	public async Task ContentsListTheNewestArrivalFirst()
	{
		var room = await NewRoom("Arrivals");
		var elsewhere = await NewRoom("Elsewhere");
		var a = await NewThing("A", room);
		var b = await NewThing("B", room);
		var c = await NewThing("C", room);

		await Assert.That(await ContentNumbers(room)).IsEquivalentTo([Number(c), Number(b), Number(a)], CollectionOrdering.Matching);

		await _db.MoveObjectAsync(a, elsewhere);
		await _db.MoveObjectAsync(a, room);
		await Assert.That(await ContentNumbers(room)).IsEquivalentTo([Number(a), Number(c), Number(b)], CollectionOrdering.Matching);

		// PennMUSH's moveto takes the object out of its list and pushes it on again, even into the room it was in.
		await _db.MoveObjectAsync(b, room);
		await Assert.That(await ContentNumbers(room)).IsEquivalentTo([Number(b), Number(a), Number(c)], CollectionOrdering.Matching);
		await Assert.That(await ContentNumbers(elsewhere)).IsEmpty();
	}

	[Test]
	public async Task DestroyingContentOrItsContainerLeavesNoLocationEntry()
	{
		var room = await NewRoom("Doomed room");
		var a = await NewThing("A", room);
		var b = await NewThing("B", room);

		await _db.DeleteObjectAsync(b.Object().DBRef);
		await Assert.That(await ContentNumbers(room)).IsEquivalentTo([Number(a)], CollectionOrdering.Matching);

		await _db.DeleteObjectAsync(room.Object().DBRef);
		var left = _db.Store.Read(tx => (
			Forward: tx.Dups(Tables.Location.Forward, Keys.Dbref(Number(a))).Count(),
			Reverse: tx.Dups(Tables.Location.Reverse, Keys.Dbref(room.Object().DBRef.Number)).Count()));
		await Assert.That(left).IsEqualTo((0, 0));
	}

	[Test]
	public async Task AWorldFromBeforeArrivalOrderKeepsTheOrderItShowed()
	{
		var room = await NewRoom("Old world");
		var things = new[] { await NewThing("A", room), await NewThing("B", room), await NewThing("C", room) };
		var container = room.Object().DBRef.Number;

		// The old layout: eight-byte dbrefs both ways, read lowest dbref first.
		await _db.Store.WriteAsync(tx =>
		{
			foreach (var thing in things)
			{
				LightningDatabase.SetLocation(tx, Number(thing), null);
				LightningDatabase.PutEdge(tx, Tables.Location, Number(thing), container);
			}
			tx.Delete(Tables.Meta, Keys.Str("mig:" + LightningDatabase.ArrivalOrderMigrationId));
		});

		await _db.Migrate();

		await Assert.That(await ContentNumbers(room)).IsEquivalentTo(things.Select(Number).Order(), CollectionOrdering.Matching);
		var newcomer = await NewThing("D", room);
		await Assert.That((await ContentNumbers(room))[0]).IsEqualTo(Number(newcomer));
		await Assert.That((await _db.GetLocationRefAsync(things[0].Object().DBRef)).Expect<DBRef>().Number).IsEqualTo(container);
	}
}
