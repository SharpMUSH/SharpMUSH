using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

public partial class LightningWriterTests
{
	[Test]
	public async Task TheWriterCountsQueuedJobsAndTimesTheirWaitsAndHolds()
	{
		using var store = Open();
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var release = new ManualResetEventSlim(false);
		var before = store.WriterStatistics;
		// The first job runs on this thread's behalf and holds the writer; the second must queue behind it.
		var blocker = Task.Run(async () => await store.WriteAsync(tx =>
		{
			started.SetResult();
			release.Wait(TimeSpan.FromSeconds(30));
			tx.Put(Tables.Meta, Keys.Str("statistics-first"), [1]);
		}));
		await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
		var queued = store.WriteAsync(tx => tx.Put(Tables.Meta, Keys.Str("statistics-second"), [2])).AsTask();
		while (store.WriterStatistics.Queued == before.Queued) await Task.Yield();
		await Assert.That(store.WriterStatistics.Queued).IsEqualTo(before.Queued + 1);
		release.Set();
		await blocker;
		await queued;
		// A batch's jobs complete inside it, so the writer may record the second hold just after the await returns.
		SpinWait.SpinUntil(() => store.WriterStatistics.Holds == before.Holds + 2, TimeSpan.FromSeconds(30));
		var after = store.WriterStatistics;
		await Assert.That(after.Queued).IsEqualTo(before.Queued);
		await Assert.That(after.Waits).IsEqualTo(before.Waits + 2);
		await Assert.That(after.Holds).IsEqualTo(before.Holds + 2);
		await Assert.That(after.MaxWaitMs).IsGreaterThan(0d);
		await Assert.That(after.TotalHoldMs).IsGreaterThan(before.TotalHoldMs);
	}

	[Test]
	public async Task TheStoreCountsReadTransactionsAndTheMostOpenAtOnce()
	{
		using var store = Open();
		var before = store.ReaderStatistics;
		var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var release = new ManualResetEventSlim(false);
		// One read stays open on another thread while this one reads the statistics inside a second.
		var held = Task.Run(() => store.Read(_ => { opened.SetResult(); release.Wait(TimeSpan.FromSeconds(30)); return 0; }));
		await opened.Task.WaitAsync(TimeSpan.FromSeconds(30));
		var during = store.Read(_ => store.ReaderStatistics);
		release.Set();
		await held;
		await Assert.That(during.Active).IsEqualTo(before.Active + 2);
		var after = store.ReaderStatistics;
		await Assert.That(after.Active).IsEqualTo(before.Active);
		await Assert.That(after.Reads).IsEqualTo(before.Reads + 2);
		await Assert.That(after.PeakActive).IsGreaterThanOrEqualTo(2);
		await Assert.That(after.MaxMs).IsGreaterThan(0d);
	}
}
