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
}
