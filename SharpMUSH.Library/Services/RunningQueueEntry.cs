namespace SharpMUSH.Library.Services;

/// <summary>
/// The queue entry the current work runs in: its process id, and the action list it was queued with when the
/// queue keeps one (a <c>@wait</c> or semaphore entry). Set by the scheduler while the entry runs, so everything
/// the entry evaluates, <c>u()</c> and inline <c>@trigger</c> included, sees it; outside a queue entry it is unset.
/// </summary>
public static class RunningQueueEntry
{
	private static readonly AsyncLocal<(long Pid, MString? Command)?> Ambient = new();

	/// <summary>The running entry's process id, or null outside a queue entry.</summary>
	public static long? Pid => Ambient.Value?.Pid;

	/// <summary>The running entry's action list, or null when the queue keeps none for it.</summary>
	public static MString? Command => Ambient.Value?.Command;

	/// <summary>Makes <paramref name="pid"/> the running entry until the returned scope is disposed.</summary>
	public static IDisposable Enter(long pid, MString? command)
	{
		var previous = Ambient.Value;
		Ambient.Value = (pid, command);
		return new Scope(previous);
	}

	private sealed class Scope((long Pid, MString? Command)? previous) : IDisposable
	{
		public void Dispose() => Ambient.Value = previous;
	}
}
