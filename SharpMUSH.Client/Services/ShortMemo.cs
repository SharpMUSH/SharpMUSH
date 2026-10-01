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

	// Bumped by Forget: a read that started before it answers its own callers but is not kept, since
	// what it saw predates the change that made the caller forget.
	private int _generation;

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

	/// <summary>
	/// Drops what is kept, so the next caller reads again (after a write that changes it). A read still
	/// running is not joined by later callers and is not kept when it finishes.
	/// </summary>
	public void Forget()
	{
		_kept = null;
		_inFlight = null;
		_generation++;
	}

	private async Task<T> RunAsync(Func<Task<T>> fetch)
	{
		var generation = _generation;
		var value = await fetch();
		if (generation == _generation)
		{
			_kept = keep(value) ? (value, DateTimeOffset.UtcNow) : null;
		}

		return value;
	}
}
