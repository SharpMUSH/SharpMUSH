namespace SharpMUSH.Library.Models.Diagnostics;

public enum DiagnosticsError { PermissionDenied, InvalidRequest, InvalidDuration, CapacityExceeded, NotFound }

public sealed record DiagnosticQueueRow(long? Pid, long? Sequence, string? Source, string? Owner,
	string Kind, string Status, string? SourceAttribute, DateTimeOffset? EnqueuedAt,
	DateTimeOffset? StartedAt, DateTimeOffset? EndedAt, TimeSpan? WaitDuration,
	TimeSpan? ExecutionDuration, long? InvocationCount, long? FailedInvocations);
public sealed record DiagnosticProfileRow(string? Source, string? Owner, string? SourceAttribute,
	string Kind, string Name, long Count, long Failures, double InclusiveMilliseconds, double MaximumMilliseconds);
public sealed record DiagnosticProfileReport(DateTimeOffset StartedAt, DateTimeOffset ExpiresAt, bool Recording,
	IReadOnlyList<DiagnosticProfileRow> Rows);
public sealed record QueueDiagnosticsReport(IReadOnlyList<DiagnosticQueueRow> Active,
	IReadOnlyList<DiagnosticQueueRow> Recent, DiagnosticProfileReport? Profile, bool CanProfile,
	long? NextHistoryCursor, bool ActiveTruncated);
