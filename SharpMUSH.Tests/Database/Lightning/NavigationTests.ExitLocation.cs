using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

public partial class NavigationTests
{
	[Test]
	[Arguments(false, false)]
	[Arguments(false, true)]
	[Arguments(true, false)]
	[Arguments(true, true)]
	public async Task ExitLocationWritesMoveBothSourceIndexesAndPreserveDestination(bool rawSetter, bool linked)
	{
		var god = await God();
		var original = await MasterRoom();
		var next = (await Node(await _db.CreateRoomAsync("New source", god))).AsContainer;
		var destination = (await Node(await _db.CreateRoomAsync("Destination", god))).AsContainer;
		var reference = await _db.CreateExitAsync("Moving exit", [], original, god);
		var exit = (await _db.GetObjectNodeAsync(reference)).Expect<SharpExit>();
		if (linked) await _db.LinkExitAsync(exit, destination);
		async ValueTask Move(AnySharpContainer location)
		{
			if (rawSetter) await _db.SetContentLocation(new AnySharpContent(exit), location);
			else await _db.MoveObjectAsync(new AnySharpContent(exit), location);
		}
		foreach (var source in new[] { next, next, original, next })
		{
			await Move(source);
			var other = source.Object().DBRef == next.Object().DBRef ? original : next;
			await Assert.That((await _db.GetExitsAsync(source).ToArrayAsync()).Select(item => item.Object.DBRef)).Contains(reference);
			await Assert.That((await _db.GetExitsAsync(source.Object().DBRef).ToArrayAsync()).Select(item => item.Object.DBRef)).Contains(reference);
			await Assert.That((await _db.GetExitsAsync(other.Object().DBRef).ToArrayAsync()).Select(item => item.Object.DBRef)).DoesNotContain(reference);
			await Assert.That((await _db.GetExitsAsync(other).ToArrayAsync()).Select(item => item.Object.DBRef)).DoesNotContain(reference);
			await Assert.That((await _db.GetContentsAsync(source).ToArrayAsync()).Select(item => item.Object().DBRef)).Contains(reference);
			await Assert.That((await _db.GetContentsAsync(other).ToArrayAsync()).Select(item => item.Object().DBRef)).DoesNotContain(reference);
			var reloaded = (await _db.GetObjectNodeAsync(reference)).Expect<SharpExit>();
			await Assert.That((await reloaded.Location.WithCancellation(CancellationToken.None)).Object().DBRef).IsEqualTo(source.Object().DBRef);
			var home = await reloaded.Home.WithCancellation(CancellationToken.None);
			if (linked) await Assert.That(home.Expect<AnySharpContainer>().Object().DBRef).IsEqualTo(destination.Object().DBRef);
			else await Assert.That(home is None).IsTrue();
			await Assert.That((await _db.GetEntrancesAsync(destination.Object().DBRef).ToArrayAsync()).Any(item => item.Object.DBRef == reference)).IsEqualTo(linked);
		}
	}

	[Test]
	public async Task MigrationRebuildsBothExitDirectionsFromStoredTypeAndLocation()
	{
		var god = await God();
		var old = await MasterRoom();
		var source = (await Node(await _db.CreateRoomAsync("Current source", god))).AsContainer;
		var reference = await _db.CreateExitAsync("Repair exit", [], old, god);
		var thing = await _db.CreateThingAsync("Not an exit", old, god, old);
		await _db.Store.WriteAsync(tx =>
		{
			tx.Delete(Tables.Meta, Keys.Str("mig:0003_exit_source_index"));
			LightningDatabase.SetSingleEdge(tx, Tables.Location, reference.Number, source.Object().DBRef.Number);
			// Old forward-only membership, unrelated reverse-only membership and a non-exit index row.
			tx.Delete(Tables.Exit.Reverse, Keys.Dbref(reference.Number));
			tx.Put(Tables.Exit.Reverse, Keys.Dbref(reference.Number), Keys.Dbref(0));
			LightningDatabase.PutEdge(tx, Tables.Exit, old.Object().DBRef.Number, thing.Number);
		});
		await _db.Migrate();
		await Assert.That((await _db.GetExitsAsync(source).ToArrayAsync()).Select(exit => exit.Object.DBRef)).Contains(reference);
		await Assert.That((await _db.GetExitsAsync(old).ToArrayAsync()).Select(exit => exit.Object.DBRef)).DoesNotContain(reference);
		await Assert.That(_db.Store.Read(tx => tx.Dups(Tables.Exit.Reverse, Keys.Dbref(reference.Number)).Select(value => Keys.ReadDbref(value)).ToArray()))
			.IsEquivalentTo([(long)source.Object().DBRef.Number]);
		await Assert.That(_db.Store.Read(tx => tx.Dups(Tables.Exit.Reverse, Keys.Dbref(thing.Number)).Any())).IsFalse();
		await Assert.That(_db.Store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("mig:0003_exit_source_index"), out _))).IsTrue();
	}
}
