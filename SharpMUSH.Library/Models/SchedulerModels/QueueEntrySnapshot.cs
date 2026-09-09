namespace SharpMUSH.Library.Models.SchedulerModels;

public enum QueueEntryState { Pending, Paused, Ready, Running }
public enum QueueControlResult { Applied, AlreadyInState, NotFound, NotPending, InvalidReason, InvalidIdentity, ScheduleFailed }
public sealed record QueueEntrySnapshot(long Pid, DBRef? Source, DBRef? Owner, string Kind,
	QueueEntryState State, TimeSpan? RemainingDelay, string PauseReason, bool ReleasePending = false,
	DateTimeOffset? EnqueuedAt = null, DateTimeOffset? StartedAt = null, TimeSpan? WaitDuration = null,
	TimeSpan? ExecutionDuration = null, long? InvocationCount = null, string? SourceAttribute = null);
