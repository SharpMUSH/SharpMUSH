using System.Threading.Channels;
using LightningDB;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning.Store;

/// <summary>
/// The one thread allowed to begin LMDB write transactions. Jobs run to completion in submission
/// order; each job is its own transaction and its own fsynced commit. A job that throws is aborted and
/// its exception is delivered to its caller only. Accepts two shapes of job: an <see cref="ITx"/> job
/// (<see cref="EnqueueAsync{T}"/>, the store's own commit-on-return transaction) and a raw
/// <see cref="LightningTransaction"/> job (<see cref="EnqueueRawAsync{T}"/>, for callers that need the
/// untyped transaction itself — e.g. <c>LightningStore.OpenTable</c> opening a sub-database — and commit
/// on their own). Both run on this same thread through the same queue, so they still serialize against
/// each other.
/// </summary>
internal sealed class LightningWriter : IDisposable
{
	private sealed record Job(Func<object?> Work, TaskCompletionSource<object?> Completion, CancellationToken Token);

	private readonly Channel<Job> _queue = Channel.CreateBounded<Job>(new BoundedChannelOptions(10_000)
	{
		SingleReader = true,
		FullMode = BoundedChannelFullMode.Wait
	});
	private readonly Func<Func<ITx, object?>, object?> _execute;
	private readonly Func<Func<LightningTransaction, object?>, object?> _executeRaw;
	private readonly Thread _thread;
	private readonly ManualResetEventSlim _resume = new(true);

	public LightningWriter(Func<Func<ITx, object?>, object?> execute, Func<Func<LightningTransaction, object?>, object?> executeRaw)
	{
		_execute = execute;
		_executeRaw = executeRaw;
		_thread = new Thread(Run) { IsBackground = true, Name = "lightning-writer" };
		_thread.Start();
	}

	public async ValueTask<T> EnqueueAsync<T>(Func<ITx, T> job, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		await _queue.Writer.WriteAsync(new Job(() => _execute(tx => job(tx)), completion, ct), ct).ConfigureAwait(false);
		return (T)(await completion.Task.ConfigureAwait(false))!;
	}

	/// <summary>Same queue, same thread, but the job gets the raw <see cref="LightningTransaction"/> instead of
	/// an <see cref="ITx"/> and is responsible for its own commit — for callers that need LMDB APIs <see cref="ITx"/>
	/// does not expose, such as opening a sub-database.</summary>
	public async ValueTask<T> EnqueueRawAsync<T>(Func<LightningTransaction, T> job, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		await _queue.Writer.WriteAsync(new Job(() => _executeRaw(tx => job(tx)), completion, ct), ct).ConfigureAwait(false);
		return (T)(await completion.Task.ConfigureAwait(false))!;
	}

	/// <summary>Completes once every job queued before the call has finished.</summary>
	public Task DrainAsync() => EnqueueAsync(_ => 0, CancellationToken.None).AsTask();

	public void Pause() => _resume.Reset();
	public void Resume() => _resume.Set();

	private void Run()
	{
		var reader = _queue.Reader;
		while (true)
		{
			Job job;
			try
			{
				if (!reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult()) return;
				if (!reader.TryRead(out job!)) continue;
			}
			catch (ChannelClosedException) { return; }

			if (job.Token.IsCancellationRequested)
			{
				job.Completion.TrySetCanceled(job.Token);
				continue;
			}

			_resume.Wait();
			try
			{
				job.Completion.TrySetResult(job.Work());
			}
			catch (Exception ex)
			{
				job.Completion.TrySetException(ex);
			}
		}
	}

	public void Dispose()
	{
		_queue.Writer.TryComplete();
		_resume.Set();
		if (Thread.CurrentThread != _thread) _thread.Join(TimeSpan.FromSeconds(10));
		_resume.Dispose();
	}
}
