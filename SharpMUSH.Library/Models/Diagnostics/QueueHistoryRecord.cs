namespace SharpMUSH.Library.Models.Diagnostics;

public enum QueueOutcome
{
	Completed, InvocationFailure, Failed, Cancelled, ExecutionLimit,
	GlobalLimit, OwnerLimit, InvalidTarget, ShuttingDown, ScheduleFailed
}

/// <summary>Closed, non-executable metadata. A rejected admission has no PID.</summary>
public sealed record QueueHistoryRecord(long Sequence, long? Pid, DBRef? Source, DBRef? Owner,
	string Kind, string? SourceAttribute, DateTimeOffset EnqueuedAt, DateTimeOffset? StartedAt,
	DateTimeOffset EndedAt, TimeSpan WaitDuration, TimeSpan? ExecutionDuration,
	long InvocationCount, long FailedInvocations, QueueOutcome Outcome)
{
	// Cursor identity has the same bounded lifetime as its retained history row.
	public Guid Cursor { get; init; } = Guid.NewGuid();
}
