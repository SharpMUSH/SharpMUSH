using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services;

/// <summary>
/// The queue entry the current work runs in: its process id, the object that runs it and that object's owner, and
/// the action list it was queued with when the queue keeps one (a <c>@wait</c> or semaphore entry). Set by the scheduler while the entry runs, so everything
/// the entry evaluates, <c>u()</c> and inline <c>@trigger</c> included, sees it; outside a queue entry it is unset.
/// </summary>
public static class RunningQueueEntry
{
	private static readonly AsyncLocal<Entry?> Ambient = new();

	/// <summary>One running entry.</summary>
	/// <param name="Pid">Its process id.</param>
	/// <param name="Executor">The object that runs it, if any.</param>
	/// <param name="Owner">That object's owner, if known.</param>
	/// <param name="Command">Its action list, or null when the queue keeps none for it.</param>
	public sealed record Entry(long Pid, DBRef? Executor, DBRef? Owner, MString? Command);

	/// <summary>The running entry, or null outside a queue entry.</summary>
	public static Entry? Current => Ambient.Value;

	/// <summary>The running entry's process id, or null outside a queue entry.</summary>
	public static long? Pid => Ambient.Value?.Pid;

	/// <summary>The running entry's action list, or null when the queue keeps none for it.</summary>
	public static MString? Command => Ambient.Value?.Command;

	/// <summary>Makes <paramref name="entry"/> the running entry until the returned scope is disposed.</summary>
	public static IDisposable Enter(Entry entry)
	{
		var previous = Ambient.Value;
		Ambient.Value = entry;
		return new Scope(previous);
	}

	private sealed class Scope(Entry? previous) : IDisposable
	{
		public void Dispose() => Ambient.Value = previous;
	}
}
