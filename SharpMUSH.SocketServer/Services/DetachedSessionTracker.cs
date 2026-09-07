namespace SharpMUSH.ConnectionServer.Services;

/// <summary>Schedules a delayed action; abstracted so grace expiry is deterministic in tests.</summary>
public interface IGraceScheduler
{
	IDisposable Schedule(TimeSpan delay, Func<Task> action);
}

/// <summary>Production scheduler backed by a one-shot <see cref="Timer"/>.</summary>
public sealed class TimerGraceScheduler(ILogger<TimerGraceScheduler>? logger = null) : IGraceScheduler
{
	public IDisposable Schedule(TimeSpan delay, Func<Task> action)
		=> new Timer(
			_ => Fire(action, ex => logger?.LogError(ex, "Grace-expiry action faulted")),
			null, delay, Timeout.InfiniteTimeSpan);

	/// <summary>
	/// Fires <paramref name="action"/> and observes its faults — synchronous throws and faulted Tasks
	/// alike — routing any exception to <paramref name="onFault"/>. Firing via a bare <c>_ = action()</c>
	/// would drop an async fault, leaving it to surface later as an unobserved
	/// <see cref="TaskScheduler.UnobservedTaskException"/> (hard to trace, policy-dependent crash).
	/// </summary>
	internal static void Fire(Func<Task> action, Action<Exception>? onFault)
	{
		Task task;
		try
		{
			task = action();
		}
		catch (Exception ex)
		{
			onFault?.Invoke(ex);
			return;
		}

		task.ContinueWith(
			t =>
			{
				// Accessing t.Exception observes the fault; unwrap the AggregateException to the real cause.
				var ex = t.Exception!.InnerException ?? t.Exception!;
				onFault?.Invoke(ex);
			},
			CancellationToken.None,
			TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
			TaskScheduler.Default);
	}
}

/// <summary>
/// Tracks handles whose socket dropped but whose session is held open for a grace window. On
/// expiry the scheduled <c>onGraceExpired</c> (the real disconnect) runs; a reconnect within the
/// window cancels it via <see cref="Reattach"/>.
/// </summary>
public sealed class DetachedSessionTracker(IGraceScheduler scheduler) : IDisposable
{
	private readonly object _gate = new();
	private readonly Dictionary<long, Registration> _pending = new();
	private bool _disposed;

	public void Detach(long handle, Func<Task> onGraceExpired, TimeSpan grace)
	{
		lock (_gate)
		{
			if (_disposed) return;
			if (_pending.Remove(handle, out var previous)) previous.Timer?.Dispose();
			var registration = new Registration();
			_pending[handle] = registration;
			registration.Timer = scheduler.Schedule(grace > TimeSpan.Zero ? grace : TimeSpan.Zero, () =>
			{
				lock (_gate)
				{
					if (_disposed || !_pending.TryGetValue(handle, out var current) || current != registration)
						return Task.CompletedTask;
					_pending.Remove(handle);
					registration.Timer?.Dispose();
					return onGraceExpired();
				}
			});
		}
	}

	public bool Reattach(long handle)
	{
		lock (_gate)
		{
			if (!_pending.Remove(handle, out var registration)) return false;
			registration.Timer?.Dispose();
			return true;
		}
	}

	public bool IsDetached(long handle)
	{
		lock (_gate) return _pending.ContainsKey(handle);
	}

	public void Dispose()
	{
		lock (_gate)
		{
			_disposed = true;
			foreach (var registration in _pending.Values) registration.Timer?.Dispose();
			_pending.Clear();
		}
	}

	private sealed class Registration
	{
		public IDisposable? Timer { get; set; }
	}
}
