using System.Threading.Channels;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning.Store;

/// <summary>
/// The one thread allowed to begin LMDB write transactions. Jobs run to completion in submission
/// order; each job is its own transaction and its own fsynced commit. A job that throws is aborted and
/// its exception is delivered to its caller only.
/// </summary>
internal sealed class LightningWriter : IDisposable
{
	private sealed record Job(Func<ITx, object?> Work, TaskCompletionSource<object?> Completion, CancellationToken Token);

	private readonly Channel<Job> _queue = Channel.CreateBounded<Job>(new BoundedChannelOptions(10_000)
	{
		SingleReader = true,
		FullMode = BoundedChannelFullMode.Wait
	});
	private readonly Func<Func<ITx, object?>, object?> _execute;
	private readonly Thread _thread;
	private readonly ManualResetEventSlim _resume = new(true);

	public LightningWriter(Func<Func<ITx, object?>, object?> execute)
	{
		_execute = execute;
		_thread = new Thread(Run) { IsBackground = true, Name = "lightning-writer" };
		_thread.Start();
	}

	public async ValueTask<T> EnqueueAsync<T>(Func<ITx, T> job, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		await _queue.Writer.WriteAsync(new Job(tx => job(tx), completion, ct), ct).ConfigureAwait(false);
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
				job.Completion.TrySetResult(_execute(job.Work));
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
