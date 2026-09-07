using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

public class LightningWriterTests
{
	private static LightningStore Open() => new(new LightningStoreOptions
	{
		Path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N")),
		MapSize = 256L << 20
	});

	[Test]
	public async Task JobsRunInSubmissionOrderOnOneThread()
	{
		using var store = Open();
		var threads = new List<int>();
		var tasks = Enumerable.Range(0, 50).Select(i => store.WriteAsync(tx =>
		{
			lock (threads) threads.Add(Environment.CurrentManagedThreadId);
			tx.Put(Tables.Meta, Keys.Str("k" + i), Keys.Str(i.ToString()));
			return i;
		}).AsTask()).ToArray();
		var results = await Task.WhenAll(tasks);
		await Assert.That(results).IsEquivalentTo(Enumerable.Range(0, 50));
		await Assert.That(threads.Distinct().Count()).IsEqualTo(1);
		await Assert.That(store.Count(Tables.Meta)).IsEqualTo(50);
	}

	[Test]
	public async Task AThrowingJobFaultsOnlyItsOwnTaskAndRollsBack()
	{
		using var store = Open();
		var bad = store.WriteAsync<int>(tx =>
		{
			tx.Put(Tables.Meta, Keys.Str("ghost"), Keys.Str("x"));
			throw new InvalidOperationException("boom");
		}).AsTask();
		var good = store.WriteAsync(tx => { tx.Put(Tables.Meta, Keys.Str("real"), Keys.Str("y")); return 1; }).AsTask();
		await Assert.That(async () => await bad).Throws<InvalidOperationException>();
		await Assert.That(await good).IsEqualTo(1);
		var hasGhost = store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("ghost"), out _));
		await Assert.That(hasGhost).IsFalse();
	}

	[Test]
	public async Task CancellationBeforeDequeueSkipsTheJob()
	{
		using var store = Open();
		using var cts = new CancellationTokenSource();
		cts.Cancel();
		var task = store.WriteAsync(tx => { tx.Put(Tables.Meta, Keys.Str("never"), Keys.Str("z")); return 0; }, cts.Token).AsTask();
		await Assert.That(async () => await task).Throws<OperationCanceledException>();
		await Assert.That(store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("never"), out _))).IsFalse();
	}
}
