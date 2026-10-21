namespace SharpMUSH.Library.Models.SchedulerModels;

public enum QueueEntryState { Pending, Paused, Ready, Running }
public enum QueueControlResult { Applied, AlreadyInState, NotFound, NotPending, InvalidReason, InvalidIdentity, ScheduleFailed }
public sealed record QueueEntrySnapshot(long Pid, DBRef? Source, DBRef? Owner, string Kind,
	QueueEntryState State, TimeSpan? RemainingDelay, string PauseReason, bool ReleasePending = false)
{
	public DateTimeOffset? EnqueuedAt { get; init; }
	public DateTimeOffset? StartedAt { get; init; }
	public TimeSpan? WaitDuration { get; init; }
	public TimeSpan? ExecutionDuration { get; init; }
	public long? InvocationCount { get; init; }
	public string? SourceAttribute { get; init; }

	/// <summary>The action list a <c>@wait</c> runs, for a delayed or semaphore entry.</summary>
	public MString? Command { get; init; }

	/// <summary>The object and attribute a semaphore entry waits on.</summary>
	public DbRefAttribute? Semaphore { get; init; }

	/// <summary>Whether the entry runs at a time of its own: a delay, or a semaphore with a timeout.</summary>
	public bool Timed { get; init; }
}

/// <summary>The timeout of a semaphore <c>@wait</c> that has none: it waits until notified.</summary>
public static class SemaphoreTimeout
{
	public static readonly TimeSpan None = TimeSpan.FromDays(36500);
}
