using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

public class SnapshotReadCapabilityTests
{
	private sealed record Sample(ulong Id, byte Value);
	private sealed record Capabilities(IReadSnapshotTx Snapshot, IBoundedDuplicateReadTx Duplicates);
	private sealed class CallbackFailure : Exception { }

	[Test]
	public async Task HeldReadRetainsItsIdAndDataAcrossARealPublishedWrite()
	{
		using var world = new OwnedWorld();
		var store = world.Store;
		await store.WriteAsync(tx => tx.Put(Tables.Meta, [1], [10]));
		var held = store.Read(tx =>
		{
			var before = Read(tx);
			Require(Snapshot(tx).ReadTransactionId == before.Id, "ID changed within one active callback.");
			// A different thread avoids gate recursion/inline-writer behavior on the reader thread.
			// GetResult blocks this synchronous callback; no native transaction crosses an await.
			Task.Run(() => store.WriteAsync(writer => writer.Put(Tables.Meta, [1], [20])).AsTask())
				.GetAwaiter().GetResult();
			var after = Read(tx);
			Require(after == before, "Held read changed its snapshot ID or value after publication.");
			return before;
		});
		var fresh = store.Read(Read);
		await Assert.That(held.Value).IsEqualTo((byte)10);
		await Assert.That(fresh.Value).IsEqualTo((byte)20);
		await Assert.That(fresh.Id == held.Id).IsFalse();
	}

	[Test]
	public async Task WriteCallbackRejectsReadSnapshotIdWithoutBreakingTheWrite()
	{
		using var world = new OwnedWorld();
		Exception? failure = null;
		await world.Store.WriteAsync(tx =>
		{
			var capability = Snapshot(tx);
			failure = Capture(() => { _ = capability.ReadTransactionId; });
			tx.Put(Tables.Meta, [1], [30]);
		});
		await Assert.That(failure is NotSupportedException).IsTrue();
		await Assert.That(world.Store.Read(Read).Value).IsEqualTo((byte)30);
	}

	[Test]
	public async Task EscapedCapabilitiesStayEndedAfterNormalReturnAndReaderRenewal()
	{
		using var world = new OwnedWorld();
		var store = world.Store;
		var duplicates = store.OpenTable("snapshot.capability.duplicates", true);
		await store.WriteAsync(tx => { tx.Put(Tables.Meta, [1], [40]); tx.Put(duplicates, [1], [2]); });
		var escaped = store.Read(tx =>
		{
			var capability = Checked(tx);
			_ = capability.Snapshot.ReadTransactionId;
			return capability;
		});
		Ended(escaped, duplicates);
		await store.WriteAsync(tx => tx.Put(Tables.Meta, [1], [41]));
		// This isolated store has one returned reader and no competing reader callbacks. Its next
		// successful read follows RentReader's existing Reset/Renew pool path, without a test hook.
		var fresh = store.Read(tx =>
		{
			var current = Checked(tx);
			Require(!ReferenceEquals(current.Snapshot, escaped.Snapshot), "Callback wrapper was reused.");
			Ended(escaped, duplicates);
			var sample = Read(tx);
			using var batch = current.Duplicates.ReadDuplicates(duplicates, [1], null, Budget());
			Require(batch.Exhausted && batch.Entries.Length == 1, "Fresh duplicate capability did not work.");
			return sample;
		});
		Ended(escaped, duplicates);
		await Assert.That(fresh.Value).IsEqualTo((byte)41);
	}

	[Test]
	public async Task ThrowingCallbackEndsBothCapabilitiesAndFreshReadsStillWork()
	{
		using var world = new OwnedWorld();
		var store = world.Store;
		var duplicates = store.OpenTable("snapshot.capability.exception", true);
		await store.WriteAsync(tx => tx.Put(Tables.Meta, [1], [50]));
		Capabilities? escaped = null;
		var expected = new CallbackFailure();
		var actual = Capture(() => store.Read<int>(tx =>
		{
			escaped = Checked(tx);
			_ = escaped.Snapshot.ReadTransactionId;
			throw expected;
		}));
		Require(ReferenceEquals(actual, expected), "Callback exception was replaced or swallowed.");
		var ended = escaped ?? throw new InvalidOperationException("Callback never captured capabilities.");
		Ended(ended, duplicates);
		var fresh = store.Read(tx =>
		{
			Ended(ended, duplicates);
			var sample = Read(tx);
			using var batch = Checked(tx).Duplicates.ReadDuplicates(duplicates, [1], null, Budget());
			Require(batch.Exhausted && batch.Entries.IsEmpty, "Fresh absent duplicate read failed.");
			return sample;
		});
		await Assert.That(fresh.Value).IsEqualTo((byte)50);
	}

	private static Sample Read(ITx tx)
	{
		var id = Snapshot(tx).ReadTransactionId;
		Require(tx.TryGet(Tables.Meta, [1], out var value) && value.Length == 1, "Controlled point value missing.");
		return new(id, value[0]);
	}

	private static IReadSnapshotTx Snapshot(ITx tx) => tx as IReadSnapshotTx
		?? throw new InvalidOperationException("Actual native transaction lacks IReadSnapshotTx.");
	private static Capabilities Checked(ITx tx) => new(Snapshot(tx), tx as IBoundedDuplicateReadTx
		?? throw new InvalidOperationException("Actual native transaction lacks IBoundedDuplicateReadTx."));
	private static NativeReadBudget Budget() => new(16, 1024, 64 * 1024, CancellationToken.None);
	private static void Ended(Capabilities capability, TableDef table)
	{
		Require(Capture(() => { _ = capability.Snapshot.ReadTransactionId; }) is ObjectDisposedException,
			"Escaped snapshot capability did not fail closed.");
		var budget = Budget();
		Require(Capture(() =>
		{
			using var batch = capability.Duplicates.ReadDuplicates(table, [1], null, budget);
		}) is ObjectDisposedException, "Escaped duplicate capability did not fail closed.");
		Require(budget.Probes == 0 && budget.Bytes == 0 && budget.Records == 0 && budget.WorkingBytes == 0,
			"Ended capability charged work before rejecting its lifetime.");
	}
	private static Exception? Capture(Action action)
	{
		try { action(); return null; }
		catch (Exception error) { return error; }
	}
	private static void Require(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}

	private sealed class OwnedWorld : IDisposable
	{
		private readonly string _path;
		public LightningStore Store { get; }
		public OwnedWorld()
		{
			var configured = Environment.GetEnvironmentVariable("TMPDIR");
			var scratch = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
				? Path.Join(Environment.CurrentDirectory, "work/snapshot-read-capability") : configured);
			Require(scratch != "/tmp" && !scratch.StartsWith("/tmp/", StringComparison.Ordinal)
				&& scratch != "/var/tmp" && !scratch.StartsWith("/var/tmp/", StringComparison.Ordinal), "Disk-backed scratch required.");
			for (string? ancestor = scratch; ancestor is not null; ancestor = Path.GetDirectoryName(ancestor))
				Require((!Directory.Exists(ancestor) && !File.Exists(ancestor))
					|| (File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) == 0, "Linked scratch ancestor refused.");
			Directory.CreateDirectory(scratch);
			_path = Path.Join(scratch, "sharpmush-snapshot-capability-" + Guid.NewGuid().ToString("N"));
			Require(!Directory.Exists(_path) && !File.Exists(_path), "Owned world already exists.");
			Store = new LightningStore(new LightningStoreOptions
			{
				Path = _path,
				MapSize = 64L << 20,
				ReaderCheckInterval = TimeSpan.Zero
			});
		}
		public void Dispose()
		{
			// Delete only after confirmed synchronous close. A close failure leaves evidence intact.
			Store.Dispose();
			if (Directory.Exists(_path)) Directory.Delete(_path, recursive: true);
		}
	}
}
