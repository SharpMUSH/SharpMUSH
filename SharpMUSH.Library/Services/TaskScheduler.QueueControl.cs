using Quartz;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.Queries.Database;
using Microsoft.Extensions.Logging;

namespace SharpMUSH.Library.Services;

public partial class TaskScheduler
{
	private sealed record DeferredSchedule(DateTimeOffset Due, Func<DateTimeOffset, long, ValueTask> Schedule, DbRefAttribute? Semaphore, MString Command, ParserState State)
	{
		public bool Paused { get; init; }
		public TimeSpan Remaining { get; init; }
		public string Reason { get; init; } = "";
		public long Generation { get; init; }
		public bool ReleasePending { get; init; }
		public JobKey? CleanupJob { get; init; }
	}
	private readonly HashSet<long> _running = [];
	private readonly SemaphoreSlim _deferredChanges = new(1, 1);
	private readonly struct DeferredLease(SemaphoreSlim gate) : IDisposable
	{
		public void Dispose() => gate.Release();
	}
	private async ValueTask<DeferredLease> LockDeferred()
	{
		await _deferredChanges.WaitAsync(ExecutionBudget.CurrentToken);
		return new(_deferredChanges);
	}

	/// <summary>Live metadata traversal with no full-ledger copy and no lock held across caller work.</summary>
	public IEnumerable<QueueEntrySnapshot> EnumerateQueueEntries()
	{
		const int pageSize = 128;
		long upperPid;
		lock (_admissionLock) upperPid = _nextPid;
		var afterPid = 0L;
		// Select only the next page: a full key copy would grow with the entire ledger.
		// The greatest retained PID is at the heap root, so smaller candidates replace it.
		var next = new PriorityQueue<long, long>(pageSize, Comparer<long>.Create((left, right) => right.CompareTo(left)));
		var pids = new long[pageSize];
		while (afterPid < upperPid)
		{
			foreach (var entry in _pendingEntries)
			{
				ExecutionBudget.Current?.ThrowIfExceeded();
				var pid = entry.Key;
				if (pid <= afterPid || pid > upperPid) continue;
				if (next.Count < pageSize) next.Enqueue(pid, pid);
				else if (pid < next.Peek()) next.DequeueEnqueue(pid, pid);
			}
			var count = next.Count;
			if (count == 0) yield break;
			for (var index = count - 1; index >= 0; index--) pids[index] = next.Dequeue();
			for (var index = 0; index < count; index++)
			{
				ExecutionBudget.Current?.ThrowIfExceeded();
				afterPid = pids[index];
				// Re-read current metadata after caller work; released entries disappear.
				if (GetQueueEntry(afterPid) is { } snapshot) yield return snapshot;
			}
		}
	}

	public IReadOnlyList<QueueEntrySnapshot> GetQueueEntries()
	{
		lock (_admissionLock)
		{
			var now = DateTimeOffset.UtcNow;
			return _pendingEntries.Values.OrderBy(e => e.Pid).Select(e => Snapshot(e, now)).ToArray();
		}
	}
	public QueueEntrySnapshot? GetQueueEntry(long pid)
	{
		lock (_admissionLock) return _pendingEntries.TryGetValue(pid, out var entry) ? Snapshot(entry, DateTimeOffset.UtcNow) : null;
	}
	private QueueEntrySnapshot Snapshot(QueueEntry entry, DateTimeOffset now)
	{
		var state = _running.Contains(entry.Pid) ? QueueEntryState.Running
			: _ready.Contains(entry.Pid) ? QueueEntryState.Ready
			: entry.Deferred?.Paused == true ? QueueEntryState.Paused : QueueEntryState.Pending;
		var delay = entry.Deferred is null || _ready.Contains(entry.Pid) ? (TimeSpan?)null
			: entry.Deferred.Paused ? entry.Deferred.Remaining : Nonnegative(entry.Deferred.Due - now);
		DBRef.TryParse(entry.Owner, out var owner);
		return new(entry.Pid, entry.Executor, owner,
			entry.Deferred?.Semaphore is not null ? "semaphore" : entry.Deferred is not null ? "delay"
				: entry.Group is DirectInputGroup or EnqueueGroup ? entry.Group : "other",
			state, delay, entry.Deferred?.Reason ?? "", entry.Deferred?.ReleasePending ?? false)
		{
			EnqueuedAt = entry.Observation?.EnqueuedAt,
			StartedAt = entry.Observation?.StartedAt,
			WaitDuration = entry.Observation?.WaitDuration,
			ExecutionDuration = entry.Observation?.ExecutionDuration,
			InvocationCount = entry.Observation?.InvocationCount,
			SourceAttribute = entry.Observation?.SourceAttribute
		};
	}

	// Caller owns the admission lock. Every timer control uses the same repair exclusions.
	private bool HasPendingCleanup(long pid)
		=> _delayedRepairs.Contains(pid) || _semaphoreRepairs.ContainsKey(pid)
			|| _semaphoreCommandReservations.Contains(pid)
			|| (_pendingEntries.TryGetValue(pid, out var entry) && entry.Deferred?.CleanupJob is not null);

	public async ValueTask<QueueControlResult> PausePending(long pid, string reason)
	{
		if (reason is null || reason.Length > 160 || reason.Any(char.IsControl)) return QueueControlResult.InvalidReason;
		using var lease = await LockDeferred();
		QueueEntry entry;
		lock (_admissionLock)
		{
			if (!_pendingEntries.TryGetValue(pid, out entry!)) return QueueControlResult.NotFound;
			if (_ready.Contains(pid) || HasPendingCleanup(pid) || entry.Deferred is null) return QueueControlResult.NotPending;
			if (entry.Deferred.Paused) return QueueControlResult.AlreadyInState;
			entry = entry with
			{
				Deferred = entry.Deferred with
				{
					Paused = true,
					Reason = reason,
					Remaining = Nonnegative(entry.Deferred.Due - DateTimeOffset.UtcNow),
					Generation = entry.Deferred.Generation + 1
				}
			};
			_pendingEntries[pid] = entry;
		}
		try { await _scheduler.PauseTrigger(new TriggerKey(entry.TriggerName, entry.Group), ExecutionBudget.CurrentToken); }
		catch (Exception ex)
		{
			// The ledger and generation already prevent execution, even if Quartz is unavailable.
			logger.LogWarning(ex, "Quartz pause failed for PID {Pid}; its reservation remains paused", pid);
		}
		return QueueControlResult.Applied;
	}

	public async ValueTask<QueueControlResult> ResumePending(long pid)
	{
		using var lease = await LockDeferred();
		QueueEntry entry;
		lock (_admissionLock)
		{
			if (!_pendingEntries.TryGetValue(pid, out entry!)) return QueueControlResult.NotFound;
			if (entry.Deferred is null || _ready.Contains(pid) || HasPendingCleanup(pid)) return QueueControlResult.NotPending;
			if (!entry.Deferred.Paused) return QueueControlResult.AlreadyInState;
		}
		if (!await ValidQueuedIdentity(entry)) return QueueControlResult.InvalidIdentity;
		if (entry.Cts.IsCancellationRequested) return QueueControlResult.NotFound;
		var deferred = entry.Deferred!;
		if (!deferred.ReleasePending)
		{
			try { await ReplaceDeferredSchedule(entry, deferred.Remaining); }
			catch (Exception ex)
			{
				logger.LogWarning(ex, "Could not resume PID {Pid}; its reservation remains paused", pid);
				return QueueControlResult.ScheduleFailed;
			}
		}
		lock (_admissionLock)
		{
			if (!_pendingEntries.TryGetValue(pid, out entry!)) return QueueControlResult.NotFound;
			deferred = entry.Deferred!;
			_pendingEntries[pid] = entry with { Deferred = deferred with { Paused = false, Reason = "" } };
		}
		if (deferred.ReleasePending) await Activate(pid);
		return QueueControlResult.Applied;
	}

	private async ValueTask<bool> ValidQueuedIdentity(QueueEntry entry)
	{
		if (entry.SemaphoreTarget is { } semaphoreTarget)
		{
			var semaphore = await mediator.Send(new GetObjectNodeQuery(semaphoreTarget), ExecutionBudget.CurrentToken);
			if (semaphore.IsNone || semaphore.Known().Object().DBRef != semaphoreTarget) return false;
		}
		if (entry.Executor is not { } executor) return true;
		var target = await mediator.Send(new GetObjectNodeQuery(executor), ExecutionBudget.CurrentToken);
		if (target.IsNone || target.Known().Object().DBRef != executor) return false;
		return (await target.Known().Object().Owner.WithCancellation(ExecutionBudget.CurrentToken)).Object.DBRef.ToString() == entry.Owner;
	}

	// Caller owns the deferred mutation lease. Each replacement invalidates callbacks already
	// acquired by Quartz, before removing their trigger or publishing another with the same PID.
	private async ValueTask ReplaceDeferredSchedule(QueueEntry entry, TimeSpan delay)
	{
		DeferredSchedule deferred;
		lock (_admissionLock)
		{
			entry = _pendingEntries[entry.Pid];
			deferred = entry.Deferred! with { Generation = entry.Deferred!.Generation + 1, Due = DateTimeOffset.UtcNow + delay };
			_pendingEntries[entry.Pid] = entry with { Deferred = deferred };
		}
		await RemoveDeferredTrigger(entry);
		await deferred.Schedule(deferred.Due, deferred.Generation);
	}
	private async ValueTask RemoveDeferredTrigger(QueueEntry entry)
	{
		var key = new TriggerKey(entry.TriggerName, entry.Group);
		var trigger = await _scheduler.GetTrigger(key, ExecutionBudget.CurrentToken);
		JobKey? job;
		lock (_admissionLock)
		{
			if (!_pendingEntries.TryGetValue(entry.Pid, out var current)) return;
			job = current.Deferred?.CleanupJob ?? trigger?.JobKey;
			if (current.Deferred is { } deferred)
				_pendingEntries[entry.Pid] = current with { Deferred = deferred with { CleanupJob = job } };
		}
		await _scheduler.UnscheduleJob(key, ExecutionBudget.CurrentToken);
		if (job is not null) await _scheduler.DeleteJob(job, ExecutionBudget.CurrentToken);
		lock (_admissionLock)
			if (_pendingEntries.TryGetValue(entry.Pid, out var current) && current.Deferred is { } deferred)
				_pendingEntries[entry.Pid] = current with { Deferred = deferred with { CleanupJob = null } };
	}
	private static TimeSpan Nonnegative(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;
}
