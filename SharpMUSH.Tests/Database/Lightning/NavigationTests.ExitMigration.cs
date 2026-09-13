using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

public partial class NavigationTests
{
	private async Task Reopen()
	{
		await _db.DisposeAsync();
		_db = Create(_path);
	}
	private string[] Edges(TableDef table) => _db.Store.Read(tx => tx.Range(table, [])
		.Select(entry => Convert.ToHexString(entry.Key) + ":" + Convert.ToHexString(entry.Value)).ToArray());

	private sealed class CancelAfterDelete(ITx inner, Action cancel) : ITx
	{
		public bool TryGet(TableDef table, ReadOnlySpan<byte> key, out byte[] value) => inner.TryGet(table, key, out value);
		public void Put(TableDef table, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value) => inner.Put(table, key, value);
		public bool Delete(TableDef table, ReadOnlySpan<byte> key) { var result = inner.Delete(table, key); cancel(); return result; }
		public bool Delete(TableDef table, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value) { var result = inner.Delete(table, key, value); cancel(); return result; }
		public long Count(TableDef table) => inner.Count(table);
		public IEnumerable<(byte[] Key, byte[] Value)> Range(TableDef table, byte[] prefix) => inner.Range(table, prefix);
		public IEnumerable<(byte[] Key, byte[] Value)> RangeFrom(TableDef table, byte[] prefix, byte[] afterKey, byte[]? afterValue) => inner.RangeFrom(table, prefix, afterKey, afterValue);
		public IEnumerable<(byte[] Key, byte[] Value)> RangeFromKey(TableDef table, byte[] startKey) => inner.RangeFromKey(table, startKey);
		public IEnumerable<byte[]> Dups(TableDef table, byte[] key) => inner.Dups(table, key);
		public Result<long> CountDups(TableDef table, ReadOnlySpan<byte> key) => inner.CountDups(table, key);
		public int DeletePrefix(TableDef table, byte[] prefix) => throw new InvalidOperationException("Repair must remain bounded.");
	}

	[Test]
	[Arguments("missing")]
	[Arguments("exit")]
	[Arguments("absent")]
	public async Task ExitRepairDoesNotIndexMissingOrNoncontainerSources(string corruption)
	{
		var god = await God();
		var source = await MasterRoom();
		var reference = await _db.CreateExitAsync("Invalid source", [], source, god);
		var other = await _db.CreateExitAsync("Other exit", [], source, god);
		await _db.LinkExitAsync((await Node(reference)).Expect<SharpMUSH.Library.Models.SharpExit>(), source);
		await _db.Store.WriteAsync(tx =>
		{
			tx.Delete(Tables.Meta, Keys.Str("mig:" + LightningDatabase.ExitSourceIndexMigrationId));
			LightningDatabase.SetSingleEdge(tx, Tables.Location, reference.Number,
				corruption == "missing" ? int.MaxValue : corruption == "exit" ? other.Number : null);
		});
		var locations = Edges(Tables.Location.Forward);
		var homes = Edges(Tables.Home.Forward);
		await _db.Migrate();
		await Assert.That(_db.Store.Read(tx => tx.Range(Tables.Exit.Forward, []).Any(entry => Keys.ReadDbref(entry.Value) == reference.Number))).IsFalse();
		await Assert.That(_db.Store.Read(tx => tx.Dups(Tables.Exit.Reverse, Keys.Dbref(reference.Number)).Any())).IsFalse();
		await Assert.That(Edges(Tables.Location.Forward).SequenceEqual(locations)).IsTrue();
		await Assert.That(Edges(Tables.Home.Forward).SequenceEqual(homes)).IsTrue();
	}

	[Test]
	public async Task ExitRepairCancellationRollsBackIndexesAndMarkerThenPersistsOnRetry()
	{
		var god = await God();
		var source = await MasterRoom();
		var reference = await _db.CreateExitAsync("Repair candidate", [], source, god);
		var marker = Keys.Str("mig:" + LightningDatabase.ExitSourceIndexMigrationId);
		await _db.Store.WriteAsync(tx =>
		{
			tx.Delete(Tables.Meta, marker);
			for (var index = 100; index < 800; index++) LightningDatabase.PutEdge(tx, Tables.Exit, index, reference.Number);
		});
		var forward = Edges(Tables.Exit.Forward);
		var reverse = Edges(Tables.Exit.Reverse);
		var locations = Edges(Tables.Location.Forward);
		var homes = Edges(Tables.Home.Forward);
		using var cancellation = new CancellationTokenSource();
		await Assert.That(async () => await _db.Store.WriteAsync(tx => _db.RebuildExitSourceIndex(
			new CancelAfterDelete(tx, cancellation.Cancel), cancellation.Token))).Throws<OperationCanceledException>();
		await Assert.That(Edges(Tables.Exit.Forward).SequenceEqual(forward)).IsTrue();
		await Assert.That(Edges(Tables.Exit.Reverse).SequenceEqual(reverse)).IsTrue();
		await Assert.That(_db.Store.Read(tx => tx.TryGet(Tables.Meta, marker, out _))).IsFalse();
		await _db.Migrate();
		await Assert.That(_db.Store.Read(tx => tx.Dups(Tables.Exit.Reverse, Keys.Dbref(reference.Number))
			.Select(value => Keys.ReadDbref(value)).ToArray())).IsEquivalentTo([(long)source.Object().DBRef.Number]);
		await Assert.That(Edges(Tables.Exit.Forward)).IsEquivalentTo([
			Convert.ToHexString(Keys.Dbref(source.Object().DBRef.Number)) + ":" + Convert.ToHexString(Keys.Dbref(reference.Number))]);
		await Assert.That(Edges(Tables.Location.Forward).SequenceEqual(locations)).IsTrue();
		await Assert.That(Edges(Tables.Home.Forward).SequenceEqual(homes)).IsTrue();
		await Reopen();
		await _db.Migrate();
		await Assert.That((await _db.GetExitsAsync(source.Object().DBRef).ToArrayAsync()).Select(exit => exit.Object.DBRef)).Contains(reference);
		var deleted = false;
		await _db.Store.WriteAsync(tx => _db.RebuildExitSourceIndex(new CancelAfterDelete(tx, () => deleted = true), CancellationToken.None));
		await Assert.That(deleted).IsFalse();
	}
}
