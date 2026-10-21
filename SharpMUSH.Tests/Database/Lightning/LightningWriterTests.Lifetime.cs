using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

public partial class LightningWriterTests
{
	/// <summary>
	/// Runs the real writer/turn/queue with a managed backend executor. No native environment exists
	/// to unmap during RED; completion still comes from actual writer execution, not a proxy task.
	/// </summary>
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task DisposalWaitsForActualExecutionBeyondOldTimeout(bool inline)
	{
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var closingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var release = new ManualResetEventSlim(false);
		Thread? executionThread = null;
		var finished = false;
		var writer = new LightningWriter(batch =>
		{
			executionThread = Thread.CurrentThread;
			started.TrySetResult();
			if (!release.Wait(TimeSpan.FromSeconds(45))) throw new TimeoutException("Managed writer ownership barrier");
			foreach (var item in batch) item.Result = 1;
			finished = true;
		}, _ => throw new InvalidOperationException("No native raw executor in this test"), 1, () => inline);
		var job = Task.Run(async () => await writer.EnqueueAsync(_ => 1, CancellationToken.None));
		Task? close = null;
		Exception? primary = null;
		try
		{
			await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
			close = Task.Run(() => { closingStarted.TrySetResult(); writer.Dispose(); });
			await closingStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
			// Old disposal returned after at most two ten-second waits even with execution blocked.
			await Task.Delay(TimeSpan.FromSeconds(22));
			await Assert.That(close.IsCompleted).IsFalse();
			await Assert.That(finished).IsFalse();
		}
		catch (Exception error) { primary = error; throw; }
		finally
		{
			release.Set();
			var errors = new List<Exception>();
			await ((Task)job).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
			if (job.Exception is { } failed) errors.AddRange(failed.InnerExceptions);
			var closing = close ?? Task.Run(writer.Dispose);
			await closing.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
			if (closing.Exception is { } closeFailed) errors.AddRange(closeFailed.InnerExceptions);
			else if (closing.IsCanceled) errors.Add(new TaskCanceledException(closing));
			// Join actual execution even if either public task faulted. Cleanup cannot mask the
			// primary assertion, and every independently owned operation is still joined.
			if (!inline && executionThread is { } thread && !thread.Join(TimeSpan.FromSeconds(10)))
				errors.Add(new TimeoutException("Managed writer did not terminate after barrier release"));
			if (errors.Count > 0)
			{
				if (primary is not null) errors.Insert(0, primary);
				throw new AggregateException(errors);
			}
		}
		await Assert.That(finished).IsTrue();
	}
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ExecutionCannotDisposeItsOwnWriter(bool inline)
	{
		using var writer = ManagedWriter(inline);
		Exception? refused = null;
		await Task.Run(async () => await writer.EnqueueAsync(_ =>
		{
			try { writer.Dispose(); }
			catch (InvalidOperationException error) { refused = error; }
			return 1;
		}, CancellationToken.None));
		await Assert.That(refused is InvalidOperationException).IsTrue();
		// Refusal must precede queue closure, so the owner can continue and close externally.
		await Assert.That(await writer.EnqueueAsync(_ => 2, CancellationToken.None)).IsEqualTo(2);
	}

	[Test]
	public async Task NestedExecutionCannotDisposeAnAncestorWriter()
	{
		using var outer = ManagedWriter(true);
		using var inner = ManagedWriter(true);
		Exception? refused = null;
		await outer.EnqueueAsync(_ => inner.EnqueueAsync(_ =>
		{
			try { outer.Dispose(); }
			catch (InvalidOperationException error) { refused = error; }
			return 1;
		}, CancellationToken.None).GetAwaiter().GetResult(), CancellationToken.None);
		await Assert.That(refused is InvalidOperationException).IsTrue();
		await Assert.That(await outer.EnqueueAsync(_ => 2, CancellationToken.None)).IsEqualTo(2);
	}

	[Test]
	public async Task DisposalDrainsAParkQueuedBehindActualWork()
	{
		using var writer = ManagedWriter(false);
		using var release = new ManualResetEventSlim(false);
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var work = writer.EnqueueAsync(_ =>
		{
			started.SetResult();
			if (!release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("Managed park barrier");
			return 1;
		}, CancellationToken.None).AsTask();
		await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
		var park = writer.PauseAndDrainAsync();
		var close = Task.Run(writer.Dispose);
		var probes = new List<Task<int>>();
		try
		{
			// Observe actual closed channel admission before allowing the queued park to execute.
			while (true)
			{
				var probe = writer.EnqueueRawAsync(_ => 0, CancellationToken.None).AsTask();
				if (probe.IsFaulted)
				{
					await Assert.That(async () => await probe).Throws<System.Threading.Channels.ChannelClosedException>();
					break;
				}
				probes.Add(probe);
				await Task.Yield();
			}
			release.Set();
			await work;
			await park.WaitAsync(TimeSpan.FromSeconds(10));
			await close.WaitAsync(TimeSpan.FromSeconds(3));
		}
		finally
		{
			release.Set();
			// On RED the old park lost Dispose's wake-up. Resume after it actually parked.
			await park;
			writer.Resume();
			await Task.WhenAll(probes);
			await work;
			await close;
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task NativeCallbackRefusesStoreCloseWithoutStoppingItsOwner(bool inline)
	{
		using var store = Open();
		Exception? refused = null;
		void Close()
		{
			try { store.Dispose(); }
			catch (InvalidOperationException error) { refused = error; }
		}
		if (inline)
			await store.WriteAsync(tx => { Close(); tx.Put(Tables.Meta, Keys.Str("after-refusal"), Keys.Str("kept")); });
		else
			store.WriteRaw(tx => { Close(); tx.Commit(); return 0; });
		await Assert.That(refused is InvalidOperationException).IsTrue();
		await store.WriteAsync(tx => tx.Put(Tables.Meta, Keys.Str("after-refusal"), Keys.Str("kept")));
		await Assert.That(store.Read(tx => tx.TryGet(Tables.Meta, Keys.Str("after-refusal"), out _))).IsTrue();
	}

	private static LightningWriter ManagedWriter(bool inline) => new(batch =>
	{
		foreach (var item in batch)
		{
			try { item.Result = item.Work(null!); }
			catch (Exception error) { item.Error = error; }
		}
	}, _ => 0, 1, () => inline);

}
