using SharpMUSH.ConnectionServer.Services;

namespace SharpMUSH.Tests.ConnectionServer.TestSchedulers;

/// <summary>Deterministic <see cref="IGraceScheduler"/>: captures the scheduled action so a test can
/// fire it manually, and tracks whether the returned registration was disposed (cancelled).</summary>
public sealed class ManualScheduler : IGraceScheduler
{
	public Func<Task>? Captured { get; private set; }
	public bool Disposed { get; private set; }

	public IDisposable Schedule(TimeSpan delay, Func<Task> action)
	{
		Captured = action;
		Disposed = false;
		return new Handle(this);
	}

	private sealed class Handle(ManualScheduler scheduler) : IDisposable
	{
		public void Dispose() => scheduler.Disposed = true;
	}
}
