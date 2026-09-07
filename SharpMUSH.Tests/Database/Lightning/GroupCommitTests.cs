using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// The writer thread coalesces every job that queued up while it was busy into one LMDB transaction —
/// one commit, one fsync — without any caller presenting a batch. Each test parks the writer inside a
/// first job, queues a burst behind it, releases, and counts commits.
/// </summary>
public class GroupCommitTests
{
	private readonly List<string> _paths = [];

	private LightningStore Open(int maxBatch = 64)
	{
		var path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		_paths.Add(path);
		return new LightningStore(new LightningStoreOptions { Path = path, MapSize = 256L << 20, MaxBatch = maxBatch });
	}

	[After(Test)]
	public Task Cleanup()
	{
		foreach (var path in _paths.Where(Directory.Exists))
		{
			try
			{
				Directory.Delete(path, recursive: true);
			}
			catch (IOException)
			{
				// Best-effort: a lingering mdb.lck can outlive the writer join.
			}
		}

		return Task.CompletedTask;
	}

	/// <summary>Parks the writer thread inside a job until <paramref name="release"/> is set, so everything
	/// queued meanwhile is guaranteed to be waiting in the channel together.</summary>
	private static (Task<int> Blocker, Task Started) Block(LightningStore store, ManualResetEventSlim release)
	{
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var blocker = store.WriteAsync(tx =>
		{
			started.SetResult();
			release.Wait();
			tx.Put(Tables.Meta, Keys.Str("blocker"), Keys.Str("1"));
			return 0;
		}).AsTask();
		return (blocker, started.Task);
	}

	[Test]
	public async Task JobsQueuedBehindARunningJobCommitTogether()
	{
		using var store = Open();
		using var release = new ManualResetEventSlim(false);
		var (blocker, started) = Block(store, release);
		await started;

		var burst = Enumerable.Range(0, 20).Select(i => store.WriteAsync(tx =>
		{
			tx.Put(Tables.Meta, Keys.Str("k" + i), Keys.Str(i.ToString()));
			return i;
		}).AsTask()).ToArray();

		release.Set();
		var results = await Task.WhenAll(burst);
		await blocker;

		await Assert.That(results).IsEquivalentTo(Enumerable.Range(0, 20));
		await Assert.That(store.Count(Tables.Meta)).IsEqualTo(21);
		await Assert.That(store.CommitCount).IsEqualTo(2);
	}

	[Test]
	public async Task AThrowingJobInsideABatchFaultsOnlyItselfAndKeepsTheRest()
	{
		using var store = Open();
		using var release = new ManualResetEventSlim(false);
		var (blocker, started) = Block(store, release);
		await started;

		var burst = Enumerable.Range(0, 20).Select(i => store.WriteAsync(tx =>
		{
			tx.Put(Tables.Meta, Keys.Str(i == 7 ? "ghost" : "k" + i), Keys.Str(i.ToString()));
			if (i == 7) throw new InvalidOperationException("boom");
			return i;
		}).AsTask()).ToArray();

		release.Set();
		await Assert.That(async () => await burst[7]).Throws<InvalidOperationException>();
		var survivors = await Task.WhenAll(burst.Where((_, i) => i != 7));
		await blocker;

		await Assert.That(survivors).IsEquivalentTo(Enumerable.Range(0, 20).Where(i => i != 7));
		await Assert.That(store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("ghost"), out _))).IsFalse();
		await Assert.That(store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("k19"), out _))).IsTrue();
		await Assert.That(store.Count(Tables.Meta)).IsEqualTo(20);
		await Assert.That(store.CommitCount).IsEqualTo(2);
	}

	[Test]
	public async Task ABatchNeverExceedsMaxBatch()
	{
		using var store = Open(maxBatch: 4);
		using var release = new ManualResetEventSlim(false);
		var (blocker, started) = Block(store, release);
		await started;

		var burst = Enumerable.Range(0, 10).Select(i => store.WriteAsync(tx =>
		{
			tx.Put(Tables.Meta, Keys.Str("k" + i), Keys.Str(i.ToString()));
			return i;
		}).AsTask()).ToArray();

		release.Set();
		await Task.WhenAll(burst);
		await blocker;

		// blocker alone, then 4 + 4 + 2.
		await Assert.That(store.CommitCount).IsEqualTo(4);
		await Assert.That(store.Count(Tables.Meta)).IsEqualTo(11);
	}

	[Test]
	public async Task ACancelledJobInsideABatchIsSkippedNotRun()
	{
		using var store = Open();
		using var release = new ManualResetEventSlim(false);
		var (blocker, started) = Block(store, release);
		await started;
		using var cts = new CancellationTokenSource();

		var live = store.WriteAsync(tx => { tx.Put(Tables.Meta, Keys.Str("live"), Keys.Str("1")); return 1; }).AsTask();
		var doomed = store.WriteAsync(tx => { tx.Put(Tables.Meta, Keys.Str("doomed"), Keys.Str("1")); return 2; }, cts.Token).AsTask();
		cts.Cancel();

		release.Set();
		await Assert.That(async () => await doomed).Throws<OperationCanceledException>();
		await Assert.That(await live).IsEqualTo(1);
		await blocker;

		await Assert.That(store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("doomed"), out _))).IsFalse();
		await Assert.That(store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("live"), out _))).IsTrue();
	}

	/// <summary>A plugin opening its table in the middle of a burst needs the raw top-level transaction and
	/// commits it itself, so it cannot ride inside a batch. It runs alone; the typed jobs around it still
	/// coalesce and everything lands.</summary>
	[Test]
	public async Task OpeningATableDuringABurstRunsAloneAndEverythingLands()
	{
		using var store = Open();
		using var release = new ManualResetEventSlim(false);
		var (blocker, started) = Block(store, release);
		await started;

		var before = Enumerable.Range(0, 5).Select(i => store.WriteAsync(tx => { tx.Put(Tables.Meta, Keys.Str("a" + i), Keys.Str("1")); return i; }).AsTask()).ToArray();
		var open = Task.Run(() => store.OpenTable("plugin_burst", duplicates: false));
		var after = Enumerable.Range(0, 5).Select(i => store.WriteAsync(tx => { tx.Put(Tables.Meta, Keys.Str("b" + i), Keys.Str("1")); return i; }).AsTask()).ToArray();

		release.Set();
		await Task.WhenAll(before.Concat(after));
		var def = await open;
		await blocker;

		await Assert.That(def.Name).IsEqualTo("plugin_burst");
		await Assert.That(store.Count(Tables.Meta)).IsEqualTo(11);
		await Assert.That(store.Count(def)).IsEqualTo(0);
	}
}
