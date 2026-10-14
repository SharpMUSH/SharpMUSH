using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

public class LightningWriterTests
{
	private readonly List<string> _paths = [];

	private LightningStore Open()
	{
		var path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		_paths.Add(path);
		return new LightningStore(new LightningStoreOptions { Path = path, MapSize = 256L << 20 });
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
				// Best-effort, same as MigrationTests: a lingering mdb.lck can outlive the writer join.
			}
		}

		return Task.CompletedTask;
	}

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

	/// <summary>
	/// Disposing the store while the writer thread is mid-job must not take the process with it. Dispose
	/// completes the queue and signals the resume event; if it then disposed that event while the thread
	/// was still inside a job, the thread's next wait on it throws <see cref="ObjectDisposedException"/> on
	/// a thread with no handler — an unhandled exception, which is process death, not a failed test. The
	/// job below sleeps long enough that Dispose lands while it runs; the assertion is simply that we are
	/// still here afterwards and its task reached a terminal state. Dispose's join outlasts a job this
	/// short, so this guards the contract rather than reproducing the join-timeout path itself — which
	/// cannot be provoked without holding a job past the ten-second join and tearing the environment down
	/// under a live transaction.
	/// </summary>
	[Test]
	public async Task DisposingWhileAJobIsRunningDoesNotKillTheProcess()
	{
		var store = Open();
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var slow = Task.Run(async () => await store.WriteAsync(tx =>
		{
			started.SetResult();
			Thread.Sleep(200);
			tx.Put(Tables.Meta, Keys.Str("slow"), Keys.Str("done"));
			return 1;
		}));

		await started.Task;
		store.Dispose();

		var finished = await Task.WhenAny(slow, Task.Delay(TimeSpan.FromSeconds(10)));
		await Assert.That(ReferenceEquals(finished, slow)).IsTrue();
		await Assert.That(slow.IsCompleted).IsTrue();
	}

	/// <summary>The store is owned by <c>LightningDatabase</c>, which the host's container disposes at
	/// shutdown and a fixture disposes itself — so a second disposal has to be a no-op by construction,
	/// not by luck in which of the three owned handles tolerates being closed twice.</summary>
	[Test]
	public async Task DisposingTwiceIsANoOp()
	{
		var store = Open();
		await store.WriteAsync(tx => tx.Put(Tables.Meta, Keys.Str("k"), Keys.Str("v")));

		store.Dispose();
		await Assert.That(() => store.Dispose()).ThrowsNothing();
	}

	/// <summary>With nothing queued and nothing running, a write runs on the thread that asked for it: the
	/// handoff to the writer thread and back costs several times the transaction itself.</summary>
	[Test]
	public async Task AnUncontendedWriteRunsOnTheCallersThread()
	{
		using var store = Open();
		var caller = Environment.CurrentManagedThreadId;
		var ran = 0;
		var write = store.WriteAsync(tx =>
		{
			ran = Environment.CurrentManagedThreadId;
			tx.Put(Tables.Meta, Keys.Str("k"), Keys.Str("v"));
			return 1;
		});

		await Assert.That(write.IsCompletedSuccessfully).IsTrue();
		await Assert.That(ran).IsEqualTo(caller);
		await Assert.That(store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("k"), out _))).IsTrue();
	}

	/// <summary>A paused writer holds back every write, including one that would otherwise run on its
	/// caller's thread.</summary>
	[Test]
	public async Task APausedWriterHoldsBackAWriteThatWouldRunOnItsCaller()
	{
		using var store = Open();
		store.PauseWriter();
		try
		{
			var write = store.WriteAsync(tx => { tx.Put(Tables.Meta, Keys.Str("held"), Keys.Str("1")); return 1; }).AsTask();
			var wonRace = await Task.WhenAny(write, Task.Delay(TimeSpan.FromMilliseconds(300)));
			await Assert.That(ReferenceEquals(wonRace, write)).IsFalse();

			store.ResumeWriter();
			await Assert.That(await write.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo(1);
		}
		finally
		{
			store.ResumeWriter();
		}
	}

	/// <summary>A write that arrives while another runs on its caller's thread queues behind it and lands
	/// after it, never alongside.</summary>
	[Test]
	public async Task AWriteArrivingWhileACallerRunsItsOwnWaitsItsTurn()
	{
		using var store = Open();
		using var release = new ManualResetEventSlim(false);
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var order = new List<string>();
		var first = Task.Run(async () => await store.WriteAsync(tx =>
		{
			started.SetResult();
			release.Wait();
			lock (order) order.Add("first");
			return 1;
		}));
		await started.Task;

		var second = store.WriteAsync(tx => { lock (order) order.Add("second"); return 2; }).AsTask();
		await Assert.That(second.IsCompleted).IsFalse();

		release.Set();
		await Assert.That(await second.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo(2);
		await first;
		await Assert.That(order).IsEquivalentTo(new[] { "first", "second" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
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
