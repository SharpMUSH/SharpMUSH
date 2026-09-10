using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Diagnostics;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public interface IQueueDiagnosticsRecorder
{
	QueueObservation Admitted(long pid, DBRef? source, DBRef? owner, string kind, string? sourceAttribute = null);
	void Rejected(DBRef? source, DBRef? owner, string kind, QueueOutcome reason);
}

/// <summary>Bounded process-local metadata; contains no authorization or command execution.</summary>
public sealed partial class QueueDiagnosticsRecorder(TimeProvider? clock = null) : IQueueDiagnosticsRecorder, ITelemetryInvocationObserver
{
	public const int HistoryCapacity = 1024;
	public static readonly TimeSpan Retention = TimeSpan.FromMinutes(15);
	private readonly TimeProvider _clock = clock ?? TimeProvider.System;
	private readonly object _historyGate = new();
	private readonly Queue<(long EndStamp, QueueHistoryRecord Record)> _history = new();
	private long _sequence;
	internal static readonly AsyncLocal<QueueObservation?> CurrentObservation = new();

	public QueueObservation Admitted(long pid, DBRef? source, DBRef? owner, string kind, string? sourceAttribute = null)
		=> new(this, pid, Full(source), Full(owner), Kind(kind), Attribute(sourceAttribute), _clock.GetUtcNow(), _clock.GetTimestamp());

	private static DBRef? Full(DBRef? reference) => reference is { IsObjid: true } ? reference : null;
	private static string Kind(string value) => value switch
	{
		"direct-input" or "enqueue" or "semaphore" or "delay" => value,
		_ => "other"
	};
	private static string? Attribute(string? value)
		=> value is { Length: > 0 and <= 256 } && !value.Any(char.IsControl) ? value : null;

	public void Rejected(DBRef? source, DBRef? owner, string kind, QueueOutcome reason)
	{
		var now = _clock.GetUtcNow();
		Append(new(0, null, Full(source), Full(owner), Kind(kind), null, now, null, now,
			TimeSpan.Zero, null, 0, 0, reason));
	}

	internal TimeProvider Clock => _clock;
	internal void Append(QueueHistoryRecord record)
	{
		lock (_historyGate)
		{
			Prune();
			while (_history.Count >= HistoryCapacity) _history.Dequeue();
			_history.Enqueue((_clock.GetTimestamp(), record with { Sequence = ++_sequence }));
		}
	}
	private void Prune()
	{
		var now = _clock.GetTimestamp();
		while (_history.TryPeek(out var oldest) && _clock.GetElapsedTime(oldest.EndStamp, now) >= Retention) _history.Dequeue();
	}
	public IReadOnlyList<QueueHistoryRecord> Recent()
	{
		lock (_historyGate) { Prune(); return _history.Reverse().Select(item => item.Record).ToArray(); }
	}

	public void RecordInvocation(TelemetryInvocation invocation)
	{
		var observation = CurrentObservation.Value;
		if (observation?.Recorder != this || observation.IsCompleted) observation = null;
		observation?.CountInvocation(invocation.Success);
		RecordProfileSample(observation, invocation);
	}
	partial void RecordProfileSample(QueueObservation? observation, TelemetryInvocation invocation);
}

/// <summary>One admitted entry's timestamps and counters, held by the queue reservation itself.</summary>
public sealed class QueueObservation
{
	private sealed record Start(DateTimeOffset Utc, long Stamp);
	private Start? _start;
	private QueueHistoryRecord? _completed;
	private long _invocations;
	private long _failed;
	private int _ended;
	private readonly long _enqueuedStamp;
	internal QueueDiagnosticsRecorder Recorder { get; }
	public bool IsCompleted => Volatile.Read(ref _ended) != 0;
	public long Pid { get; }
	public DBRef? Source { get; }
	public DBRef? Owner { get; }
	public string Kind { get; }
	public string? SourceAttribute { get; }
	public DateTimeOffset EnqueuedAt { get; }
	public DateTimeOffset? StartedAt => Volatile.Read(ref _start)?.Utc;
	public long InvocationCount => Interlocked.Read(ref _invocations);
	public long FailedInvocations => Interlocked.Read(ref _failed);
	public TimeSpan WaitDuration => Volatile.Read(ref _completed)?.WaitDuration
		?? Recorder.Clock.GetElapsedTime(_enqueuedStamp, Volatile.Read(ref _start)?.Stamp ?? Recorder.Clock.GetTimestamp());
	public TimeSpan? ExecutionDuration => Volatile.Read(ref _completed)?.ExecutionDuration
		?? (Volatile.Read(ref _start) is { } started ? Recorder.Clock.GetElapsedTime(started.Stamp) : null);

	internal QueueObservation(QueueDiagnosticsRecorder recorder, long pid, DBRef? source, DBRef? owner,
		string kind, string? attribute, DateTimeOffset enqueuedAt, long stamp)
	{
		Recorder = recorder; Pid = pid; Source = source; Owner = owner; Kind = kind; SourceAttribute = attribute;
		EnqueuedAt = enqueuedAt; _enqueuedStamp = stamp;
	}
	public IDisposable Enter()
	{
		Interlocked.CompareExchange(ref _start, new(Recorder.Clock.GetUtcNow(), Recorder.Clock.GetTimestamp()), null);
		var previous = QueueDiagnosticsRecorder.CurrentObservation.Value;
		QueueDiagnosticsRecorder.CurrentObservation.Value = this;
		return new Scope(previous);
	}
	private sealed class Scope(QueueObservation? previous) : IDisposable
	{
		public void Dispose() => QueueDiagnosticsRecorder.CurrentObservation.Value = previous;
	}
	internal void CountInvocation(bool success)
	{
		Interlocked.Increment(ref _invocations);
		if (!success) Interlocked.Increment(ref _failed);
	}
	public void Complete(QueueOutcome outcome)
	{
		if (Interlocked.Exchange(ref _ended, 1) != 0) return;
		var start = Volatile.Read(ref _start);
		var now = Recorder.Clock.GetTimestamp();
		var record = new QueueHistoryRecord(0, Pid, Source, Owner, Kind, SourceAttribute, EnqueuedAt,
			start?.Utc, Recorder.Clock.GetUtcNow(), Recorder.Clock.GetElapsedTime(_enqueuedStamp, start?.Stamp ?? now),
			start is null ? null : Recorder.Clock.GetElapsedTime(start.Stamp, now), InvocationCount, FailedInvocations,
			outcome == QueueOutcome.Completed && FailedInvocations > 0 ? QueueOutcome.InvocationFailure : outcome);
		Volatile.Write(ref _completed, record);
		Recorder.Append(record);
	}
}
