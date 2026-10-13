using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using LightningDB;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning.Store;

/// <summary>
/// Serializes LMDB write transactions: one at a time, to completion, in submission order. Typed jobs
/// (<see cref="EnqueueAsync{T}"/>, the store's own commit-on-return transaction) are group-committed:
/// whatever has queued up while the previous commit was in flight is taken together, up to
/// <see cref="LightningStoreOptions.MaxBatch"/>, and run inside one transaction with one commit and one
/// sync. Each job in a batch runs in its own nested transaction, so a job that throws is aborted
/// alone and its exception is delivered to its caller only; the others in the batch still land. A lone
/// job commits on its own, immediately — batching never waits for company.
///
/// A typed job that finds nothing ahead of it — the queue empty, no job running, the writer not paused —
/// runs on the caller's own thread instead of being handed to the writer thread. That handoff was most of
/// an uncontended write's cost: waking the writer thread, then a pool thread to resume the caller, several
/// times what the transaction itself takes. The job is the same one-job batch with the same commit and
/// sync; only the thread differs. Whatever arrives while it runs queues as before, and the writer thread
/// takes it together once the caller is done, so group commit under load is unchanged. Only one job runs
/// at a time either way: <see cref="_turn"/> is held by whichever thread is running one, and the writer
/// thread takes jobs off the queue only while holding it, so an empty queue under it means nothing
/// submitted earlier is still waiting.
///
/// Raw jobs (<see cref="EnqueueRawAsync{T}"/>, for callers that need the untyped
/// <see cref="LightningTransaction"/> itself — e.g. <c>LightningStore.OpenTable</c> opening a sub-database —
/// and commit on their own) always run alone, and always on the writer thread: they own their top-level
/// transaction and cannot nest. They take the same turn as typed jobs, so the two still serialize.
/// </summary>
internal sealed class LightningWriter : IDisposable
{
	/// <summary>One typed job inside a batch. The store fills exactly one of <see cref="Result"/> or
	/// <see cref="Error"/> per item; the writer completes the caller from whichever it finds.</summary>
	internal sealed class BatchItem(Func<ITx, object?> work)
	{
		public Func<ITx, object?> Work { get; } = work;
		public object? Result { get; set; }
		public Exception? Error { get; set; }
	}

	private abstract record Job(TaskCompletionSource<object?> Completion, CancellationToken Token);
	private sealed record TypedJob(Func<ITx, object?> Work, TaskCompletionSource<object?> Completion, CancellationToken Token) : Job(Completion, Token);
	/// <summary>Runs alone, outside any batch: a raw transaction job or the park job.</summary>
	private sealed record SoloJob(Func<object?> Work, TaskCompletionSource<object?> Completion, CancellationToken Token) : Job(Completion, Token);

	private readonly Channel<Job> _queue = Channel.CreateBounded<Job>(new BoundedChannelOptions(10_000)
	{
		SingleReader = true,
		FullMode = BoundedChannelFullMode.Wait
	});
	private readonly Action<IReadOnlyList<BatchItem>> _executeBatch;
	private readonly Func<Func<LightningTransaction, object?>, object?> _executeRaw;
	private readonly int _maxBatch;
	private readonly Thread _thread;
	private readonly ManualResetEventSlim _resume = new(true);

	/// <summary>Held by the thread running a job: the writer thread, or a caller running its own.</summary>
	private readonly SemaphoreSlim _turn = new(1, 1);

	/// <summary>Whether the calling thread may run a job itself. The store says no while that thread is
	/// inside one of its own read sections, whose lock it could not take again.</summary>
	private readonly Func<bool> _mayRunInline;

	private volatile bool _disposed;

	public LightningWriter(Action<IReadOnlyList<BatchItem>> executeBatch, Func<Func<LightningTransaction, object?>, object?> executeRaw, int maxBatch,
		Func<bool>? mayRunInline = null)
	{
		if (maxBatch < 1) throw new ArgumentOutOfRangeException(nameof(maxBatch), maxBatch, "A batch holds at least one job.");
		_executeBatch = executeBatch;
		_executeRaw = executeRaw;
		_mayRunInline = mayRunInline ?? (() => true);
		_maxBatch = maxBatch;
		_thread = new Thread(Run) { IsBackground = true, Name = "lightning-writer" };
		_thread.Start();
	}

	public async ValueTask<T> EnqueueAsync<T>(Func<ITx, T> job, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		if (TryRunInline(job, out var result))
		{
			return result;
		}

		var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		await _queue.Writer.WriteAsync(new TypedJob(tx => job(tx), completion, ct), ct).ConfigureAwait(false);
		return (T)(await completion.Task.ConfigureAwait(false))!;
	}

	/// <summary>Same queue, same thread, but the job gets the raw <see cref="LightningTransaction"/> instead of
	/// an <see cref="ITx"/> and is responsible for its own commit — for callers that need LMDB APIs <see cref="ITx"/>
	/// does not expose, such as opening a sub-database.</summary>
	public async ValueTask<T> EnqueueRawAsync<T>(Func<LightningTransaction, T> job, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		await _queue.Writer.WriteAsync(new SoloJob(() => _executeRaw(tx => job(tx)), completion, ct), ct).ConfigureAwait(false);
		return (T)(await completion.Task.ConfigureAwait(false))!;
	}

	/// <summary>
	/// Runs <paramref name="job"/> on the calling thread when nothing is ahead of it; see the class remarks.
	/// False, with nothing run, when the queue holds a job, another job is running, the writer is paused or
	/// disposed, or the store refuses; the caller then queues as usual. A job that throws, or whose commit
	/// fails, throws here, as it would from the queue.
	/// </summary>
	private bool TryRunInline<T>(Func<ITx, T> job, out T result)
	{
		result = default!;
		if (_disposed || !_mayRunInline() || !_turn.Wait(0))
		{
			return false;
		}

		var item = new BatchItem(tx => job(tx));
		try
		{
			// Checked under the turn: the writer thread only dequeues while holding it, so an empty queue
			// here means every job submitted before this one has finished.
			if (_disposed || !_resume.IsSet || _queue.Reader.Count != 0)
			{
				return false;
			}

			try
			{
				_executeBatch([item]);
			}
			catch (Exception ex)
			{
				// The commit itself failed, as in RunBatch.
				item.Error ??= ex;
			}
		}
		finally
		{
			_turn.Release();
		}

		if (item.Error is { } error)
		{
			ExceptionDispatchInfo.Throw(error);
		}

		result = (T)item.Result!;
		return true;
	}

	public void Pause() => _resume.Reset();
	public void Resume() => _resume.Set();

	/// <summary>
	/// Drains and pauses in one step: completes once every job queued before this call has finished and
	/// the writer thread is parked, with nothing queued afterwards started until <see cref="Resume"/>.
	/// Draining and pausing as two calls cannot do this — pausing first would park the writer before it
	/// reaches the drain job, and draining first leaves a window in which a newly queued job starts.
	/// The park is itself a queued job, so the queue's own order is what makes the pair atomic; it runs
	/// outside any transaction, so a parked writer holds no store lock and a directory swap can take the
	/// store's write lock while it waits here.
	/// </summary>
	public async Task PauseAndDrainAsync(CancellationToken ct = default)
	{
		var parked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var park = new SoloJob(() =>
		{
			_resume.Reset();
			parked.SetResult();
			_resume.Wait();
			return null;
		}, completion, CancellationToken.None);

		await _queue.Writer.WriteAsync(park, ct).ConfigureAwait(false);
		await parked.Task.ConfigureAwait(false);
	}

	private void Run()
	{
		var reader = _queue.Reader;
		var batch = new List<TypedJob>(_maxBatch);
		while (true)
		{
			try
			{
				if (!reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult()) return;
			}
			catch (ChannelClosedException) { return; }

			// Neither wait can throw: Dispose disposes the resume event only once this thread has exited, and
			// never the turn.
			_resume.Wait();
			_turn.Wait();
			try
			{
				// Dequeued only under the turn, so a caller running its own job never overtakes one submitted
				// before it.
				if (!reader.TryRead(out var first)) continue;

				if (first is SoloJob solo)
				{
					RunSolo(solo);
					continue;
				}

				// Take everything typed that is already waiting, in order, up to the cap. TryPeek keeps a
				// solo job at the head of the queue for the next turn rather than pulling it into a batch.
				batch.Clear();
				batch.Add((TypedJob)first);
				while (batch.Count < _maxBatch && reader.TryPeek(out var next) && next is TypedJob && reader.TryRead(out var taken))
				{
					batch.Add((TypedJob)taken);
				}

				RunBatch(batch);
			}
			finally
			{
				_turn.Release();
			}
		}
	}

	private static void RunSolo(SoloJob job)
	{
		if (job.Token.IsCancellationRequested)
		{
			job.Completion.TrySetCanceled(job.Token);
			return;
		}

		try
		{
			job.Completion.TrySetResult(job.Work());
		}
		catch (Exception ex)
		{
			job.Completion.TrySetException(ex);
		}
	}

	private void RunBatch(List<TypedJob> jobs)
	{
		var items = new List<(TypedJob Job, BatchItem Item)>(jobs.Count);
		foreach (var job in jobs)
		{
			if (job.Token.IsCancellationRequested)
			{
				job.Completion.TrySetCanceled(job.Token);
				continue;
			}

			items.Add((job, new BatchItem(job.Work)));
		}

		if (items.Count == 0) return;

		try
		{
			_executeBatch(items.Select(i => i.Item).ToList());
		}
		catch (Exception ex)
		{
			// The batch's own commit failed: every job that had not already failed on its own goes down
			// with it, because nothing in the batch reached the disk.
			foreach (var (_, item) in items) item.Error ??= ex;
		}

		foreach (var (job, item) in items)
		{
			if (item.Error is { } error) job.Completion.TrySetException(error);
			else job.Completion.TrySetResult(item.Result);
		}
	}

	/// <summary>Closes the queue, wakes a parked writer and waits for the thread to finish the job it is on,
	/// then for a caller's own job still running. The resume event is disposed only once that thread has
	/// actually exited — a job still running would otherwise wait on a disposed event. The turn is never
	/// disposed: it holds no wait handle, and a caller that read <see cref="_disposed"/> just before it was
	/// set must still be able to try it.</summary>
	public void Dispose()
	{
		_queue.Writer.TryComplete();
		_resume.Set();
		if (Thread.CurrentThread == _thread) return;
		var exited = _thread.Join(TimeSpan.FromSeconds(10));

		// Once this holds the turn, no caller's own job is running, and none starts after: they see
		// _disposed and queue instead, which the closed queue refuses.
		var holding = _turn.Wait(TimeSpan.FromSeconds(10));
		_disposed = true;
		if (holding) _turn.Release();

		if (exited) _resume.Dispose();
	}
}
