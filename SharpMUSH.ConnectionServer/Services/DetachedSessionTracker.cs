using System.Collections.Concurrent;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>Schedules a delayed action; abstracted so grace expiry is deterministic in tests.</summary>
public interface IGraceScheduler
{
	IDisposable Schedule(TimeSpan delay, Func<Task> action);
}

/// <summary>Production scheduler backed by a one-shot <see cref="Timer"/>.</summary>
public sealed class TimerGraceScheduler : IGraceScheduler
{
	public IDisposable Schedule(TimeSpan delay, Func<Task> action)
		=> new Timer(_ => _ = action(), null, delay, Timeout.InfiniteTimeSpan);
}

/// <summary>
/// Tracks handles whose socket dropped but whose session is held open for a grace window. On
/// expiry the scheduled <c>onGraceExpired</c> (the real disconnect) runs; a reconnect within the
/// window cancels it via <see cref="Reattach"/>.
/// </summary>
public sealed class DetachedSessionTracker(IGraceScheduler scheduler)
{
	private readonly ConcurrentDictionary<long, IDisposable> _pending = new();

	public void Detach(long handle, Func<Task> onGraceExpired, TimeSpan grace)
	{
		var registration = scheduler.Schedule(grace, async () =>
		{
			if (_pending.TryRemove(handle, out _))
				await onGraceExpired();
		});

		// Replace any prior pending timer for this handle.
		if (_pending.TryRemove(handle, out var previous))
			previous.Dispose();
		_pending[handle] = registration;
	}

	public bool Reattach(long handle)
	{
		if (!_pending.TryRemove(handle, out var registration))
			return false;
		registration.Dispose();
		return true;
	}

	public bool IsDetached(long handle) => _pending.ContainsKey(handle);
}
