namespace SharpMUSH.Client.Services;

/// <summary>
/// One value, shared: concurrent callers wait on the same fetch, and a result worth keeping is served
/// for <c>lifetime</c> before the next caller fetches again. A result <c>keep</c> rejects (a failure)
/// is handed to the callers that waited for it and then forgotten. WASM runs on one thread, so the
/// fields need no locking.
/// </summary>
internal sealed class ShortMemo<T>(TimeSpan lifetime, Func<T, bool> keep)
{
	private Task<T>? _inFlight;
	private (T Value, DateTimeOffset At)? _kept;

	public Task<T> GetAsync(Func<Task<T>> fetch)
	{
		if (_kept is { } kept && DateTimeOffset.UtcNow - kept.At < lifetime)
		{
			return Task.FromResult(kept.Value);
		}

		// Join a read that is still running. A finished one is never reused: a success is in _kept, and
		// a failure is exactly what the next caller must not be handed. (Clearing the slot from the
		// read itself would race a read that completes synchronously, before the slot is assigned.)
		if (_inFlight is { IsCompleted: false } running)
		{
			return running;
		}

		return _inFlight = RunAsync(fetch);
	}

	/// <summary>Drops what is kept, so the next caller reads again (after a write that changes it).</summary>
	public void Forget()
	{
		_kept = null;
		_inFlight = null;
	}

	private async Task<T> RunAsync(Func<Task<T>> fetch)
	{
		var value = await fetch();
		_kept = keep(value) ? (value, DateTimeOffset.UtcNow) : null;
		return value;
	}
}
