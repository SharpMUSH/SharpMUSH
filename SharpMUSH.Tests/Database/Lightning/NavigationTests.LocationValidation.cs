using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

public partial class NavigationTests
{
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ExitMovementRemovesEveryHistoricalReverseMembership(bool rawSetter)
	{
		var god = await God();
		var original = await MasterRoom();
		var next = (await Node(await _db.CreateRoomAsync("Next", god))).AsContainer;
		var reference = await _db.CreateExitAsync("Exit", [], original, god);
		var exit = (await Node(reference)).AsContent;
		await _db.Store.WriteAsync(tx =>
		{
			for (var index = 100; index < 800; index++) LightningDatabase.PutEdge(tx, Tables.Exit, index, reference.Number);
		});
		if (rawSetter) await _db.SetContentLocation(exit, next);
		else await _db.MoveObjectAsync(exit, next);
		await Assert.That(_db.Store.Read(tx => tx.Dups(Tables.Exit.Reverse, Keys.Dbref(reference.Number))
			.Select(value => Keys.ReadDbref(value)).ToArray())).IsEquivalentTo([(long)next.Object().DBRef.Number]);
		await Assert.That(_db.Store.Read(tx => tx.Range(Tables.Exit.Forward, [])
			.Where(entry => Keys.ReadDbref(entry.Value) == reference.Number).Select(entry => Keys.ReadDbref(entry.Key)).ToArray()))
			.IsEquivalentTo([(long)next.Object().DBRef.Number]);
	}

	[Test]
	[Arguments(false, "sourceStamp")]
	[Arguments(true, "sourceStamp")]
	[Arguments(false, "destinationStamp")]
	[Arguments(true, "destinationStamp")]
	[Arguments(false, "sourceType")]
	[Arguments(true, "sourceType")]
	[Arguments(false, "destinationType")]
	[Arguments(true, "destinationType")]
	[Arguments(false, "destinationExit")]
	[Arguments(true, "destinationExit")]
	[Arguments(false, "sourceMissing")]
	[Arguments(true, "sourceMissing")]
	[Arguments(false, "destinationMissing")]
	[Arguments(true, "destinationMissing")]
	public async Task LocationMutationRejectsChangedStoredIdentityBeforeAnyEdgeWrite(bool rawSetter, string change)
	{
		var god = await God();
		var original = await MasterRoom();
		var next = (await Node(await _db.CreateRoomAsync("Next", god))).AsContainer;
		var reference = await _db.CreateExitAsync("Exit", [], original, god);
		var exit = (await Node(reference)).AsContent;
		await _db.Store.WriteAsync(tx =>
		{
			var key = Keys.Dbref(change.StartsWith("source", StringComparison.Ordinal) ? reference.Number : next.Object().DBRef.Number);
			if (change.EndsWith("Missing", StringComparison.Ordinal)) { tx.Delete(Tables.Obj, key); return; }
			if (!tx.TryGet(Tables.Obj, key, out var bytes)) throw new InvalidOperationException("Missing fixture object");
			var record = Codec.Deserialize<ObjectRecord>(bytes);
			tx.Put(Tables.Obj, key, Codec.Serialize(change.EndsWith("Stamp", StringComparison.Ordinal)
				? record with { CreationTime = record.CreationTime + 1 } : record with { Type = change == "destinationExit" ? "EXIT" : "THING" }));
		});
		var location = Edges(Tables.Location.Forward);
		var exitIndex = Edges(Tables.Exit.Forward);
		await Assert.That(async () =>
		{
			if (rawSetter) await _db.SetContentLocation(exit, next);
			else await _db.MoveObjectAsync(exit, next);
		}).Throws<InvalidOperationException>();
		await Assert.That(Edges(Tables.Location.Forward).SequenceEqual(location)).IsTrue();
		await Assert.That(Edges(Tables.Exit.Forward).SequenceEqual(exitIndex)).IsTrue();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task CancelledLocationWriteLeavesEveryEdgeUnchanged(bool rawSetter)
	{
		var god = await God();
		var original = await MasterRoom();
		var next = (await Node(await _db.CreateRoomAsync("Next", god))).AsContainer;
		var content = (await Node(await _db.CreateExitAsync("Exit", [], original, god))).AsContent;
		var location = Edges(Tables.Location.Forward);
		var exits = Edges(Tables.Exit.Forward);
		var token = new CancellationToken(true);
		await Assert.That(async () =>
		{
			if (rawSetter) await _db.SetContentLocation(content, next, token);
			else await _db.MoveObjectAsync(content, next, token);
		}).Throws<OperationCanceledException>();
		await Assert.That(Edges(Tables.Location.Forward).SequenceEqual(location)).IsTrue();
		await Assert.That(Edges(Tables.Exit.Forward).SequenceEqual(exits)).IsTrue();
	}

	[Test]
	[Arguments(false, false)]
	[Arguments(false, true)]
	[Arguments(true, false)]
	[Arguments(true, true)]
	public async Task OrdinaryContentMovementPreservesHomeAndDoesNotCreateExitMembership(bool rawSetter, bool player)
	{
		var god = await God();
		var original = await MasterRoom();
		var next = (await Node(await _db.CreateRoomAsync("Next", god))).AsContainer;
		var content = player ? new AnySharpContent(god)
			: (await Node(await _db.CreateThingAsync("Thing", original, god, original))).AsContent;
		var home = Edges(Tables.Home.Forward);
		var exits = Edges(Tables.Exit.Forward);
		if (rawSetter) await _db.SetContentLocation(content, next);
		else await _db.MoveObjectAsync(content, next);
		await Assert.That((await _db.GetLocationAsync(content.Object().DBRef)).Expect<AnySharpContainer>().Object().DBRef).IsEqualTo(next.Object().DBRef);
		await Assert.That(Edges(Tables.Home.Forward).SequenceEqual(home)).IsTrue();
		await Assert.That(Edges(Tables.Exit.Forward).SequenceEqual(exits)).IsTrue();
	}
}
