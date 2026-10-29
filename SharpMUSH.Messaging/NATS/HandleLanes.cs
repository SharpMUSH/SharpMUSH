using System.Runtime.ExceptionServices;

namespace SharpMUSH.Messaging.NATS;

/// <summary>
/// Runs work for many connection handles at once, one handle's work one item after another in the order
/// it was dispatched. At most <c>capacity</c> items are dispatched and unfinished; a dispatch past that
/// waits for a slot. The first failure cancels <see cref="Faulted"/>, and work not yet started is
/// skipped from then on, so it is never acknowledged and the broker delivers it again in order.
/// </summary>
internal sealed class HandleLanes(int capacity) : IDisposable
{
	private readonly SemaphoreSlim _slots = new(capacity, capacity);
	private readonly Dictionary<long, Task> _tails = [];
	private readonly CancellationTokenSource _faulted = new();
	private ExceptionDispatchInfo? _failure;

	/// <summary>Cancelled when a dispatched item throws.</summary>
	public CancellationToken Faulted => _faulted.Token;

	public async ValueTask DispatchAsync(long handle, Func<Task> work, CancellationToken ct)
	{
		await _slots.WaitAsync(ct);
		var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		Task previous;
		lock (_tails)
		{
			previous = _tails.GetValueOrDefault(handle) ?? Task.CompletedTask;
			_tails[handle] = done.Task;
		}
		_ = Task.Run(() => RunAsync(handle, previous, done, work), CancellationToken.None);
	}

	/// <summary>Waits for every dispatched item, then rethrows the first failure, if any.</summary>
	public async Task DrainAsync()
	{
		Task[] tails;
		lock (_tails) tails = [.. _tails.Values];
		await Task.WhenAll(tails);
		_failure?.Throw();
	}

	private async Task RunAsync(long handle, Task previous, TaskCompletionSource done, Func<Task> work)
	{
		try
		{
			await previous;
			if (!_faulted.IsCancellationRequested) await work();
		}
		catch (Exception ex)
		{
			if (Interlocked.CompareExchange(ref _failure, ExceptionDispatchInfo.Capture(ex), null) is null)
				await _faulted.CancelAsync();
		}
		finally
		{
			lock (_tails)
			{
				if (_tails.TryGetValue(handle, out var tail) && tail == done.Task) _tails.Remove(handle);
			}
			_slots.Release();
			done.SetResult();
		}
	}

	public void Dispose()
	{
		_slots.Dispose();
		_faulted.Dispose();
	}
}
