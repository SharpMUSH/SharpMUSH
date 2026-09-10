using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Extensions;
using OneOf;
using Quartz;
using Quartz.Impl.Matchers;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.InputSessions;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Models.Diagnostics;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Task scheduler, schedules items onto the queue.
///
/// Internally, it uses the 'Group' section to store what type of queue it is, and optionally, DB/Attr Semaphore data.
/// Internally, it uses the Trigger Key section to store the Executor's Objid and a PID number.
/// </summary>
/// <example>
/// <code>
/// #5> @wait #7/SEMAPHORE=think 5;
/// </code>
/// <code>
/// Group -- semaphore:#7:1744849096000/SEMAPHORE -- semaphore:objid/attribute
/// Trigger Key -- dbref:#5:1744849081000-16 -- dbref:objid-pid
/// </code>
/// </example>
/// <param name="parser"></param>
/// <param name="schedulerFactory"></param>
public partial class TaskScheduler(
	IMUSHCodeParser parser,
	IConnectionService connectionService,
	ISchedulerFactory schedulerFactory,
	IAttributeService attributeService,
	IMediator mediator,
	ILogger<TaskScheduler> logger,
	IOptionsWrapper<SharpMUSHOptions>? configuration = null,
	INotifyService? notifyService = null,
	IInputSessionService? inputSessions = null,
	IQueueDiagnosticsRecorder? diagnostics = null) : ITaskScheduler, IAsyncDisposable
{
	private long _nextPid = 0;
	private long NextPid() => Interlocked.Increment(ref _nextPid);

	/// <summary>
	/// Represents a queued command entry for the FIFO immediate-execution queue.
	/// </summary>
	private sealed record QueueEntry(
		long Pid,
		string TriggerName,
		string Group,
		Func<ValueTask<CallState?>> Action,
		CancellationTokenSource Cts,
		string Owner,
		DBRef? Executor,
		DBRef? SemaphoreTarget = null,
		Action? OnReleased = null,
		bool ManagesSemaphoreCount = false
	)
	{
		public DeferredSchedule? Deferred { get; init; }
		public QueueObservation? Observation { get; init; }
		public QueueOutcome? DeferredReleaseOutcome { get; init; }
		public bool HaltAccountingSettled { get; init; }
		public PendingInputCommand? PendingInput { get; init; }
	}

	// Stored only on admitted entries. An escape cannot retain a future-start tombstone.
	private sealed class PendingInputCommand(long handle, IConnectionService.ConnectionData? connection,
		string? transport, InputCaptureTicket? ticket)
	{
		public long Handle { get; } = handle;
		public IConnectionService.ConnectionData? Connection { get; } = connection;
		public string? Transport { get; } = transport;
		public InputCaptureTicket? Ticket { get; } = ticket;
		// Both flags are protected by _admissionLock.
		public bool Completed { get; set; }
		public bool EscapeRequested { get; set; }
	}

	private sealed record SemaphoreRepairIdentity(string Id, string Key, string Name,
		string LongName, int? CommandListIndex, DBRef? Owner, string Flags);

	private static async ValueTask<SemaphoreRepairIdentity> CaptureRepairIdentity(SharpAttribute attribute)
	{
		// Wipe removes the whole subtree. Never remove children created after this counter.
		if (attribute.Leaves is not null
			&& await (await attribute.Leaves.WithCancellation(ExecutionBudget.CurrentToken)).AnyAsync(ExecutionBudget.CurrentToken))
			throw new InvalidOperationException("Created semaphore has child attributes; refusing admission cleanup of the subtree.");
		var owner = attribute.Owner is null ? null : await attribute.Owner.WithCancellation(ExecutionBudget.CurrentToken);
		return new(attribute.Id, attribute.Key, attribute.Name, attribute.LongName, attribute.CommandListIndex,
			owner?.Object.DBRef, string.Join('\0', attribute.Flags.Select(flag => flag.Name).Order(StringComparer.Ordinal)));
	}

	// The reservation ledger bounds this channel, including cancelled entries until consumed.
	private readonly Channel<QueueEntry> _immediateQueue = Channel.CreateUnbounded<QueueEntry>(
	 new UnboundedChannelOptions { SingleReader = true });
	private readonly object _admissionLock = new();
	private readonly Dictionary<QueueRejectionReason, long> _rejections = new();
	private readonly HashSet<long> _ready = new();
	// Updated with the reservation ledger under _admissionLock.
	private readonly SortedSet<long> _orderedPids = new();
	private bool _stopping;
	public QueueUsage GetQueueUsage()
	{
		lock (_admissionLock) return new(_pendingEntries.Count,
		 _pendingEntries.Values.GroupBy(e => e.Owner).ToDictionary(g => g.Key, g => g.Count()),
		 new Dictionary<QueueRejectionReason, long>(_rejections));
	}
	public bool HasPendingWork(string triggerName, string group)
	{
		lock (_admissionLock) return _pendingEntries.Values.Any(entry => entry.TriggerName == $"{triggerName}-{entry.Pid}" && entry.Group == group);
	}
	private QueueAdmissionResult Reject(QueueRejectionReason reason)
	{
		lock (_admissionLock) _rejections[reason] = _rejections.GetValueOrDefault(reason) + 1;
		logger.LogWarning("Queue admission rejected: {Reason}", reason);
		return new(null, reason);
	}
	private QueueEntry? RemoveEntry(long pid)
	{
		if (_semaphoreRepairs.ContainsKey(pid) || _semaphoreCommandReservations.Contains(pid) || _delayedRepairs.Contains(pid)) return null;
		_ready.Remove(pid);
		_running.Remove(pid);
		if (!_pendingEntries.TryRemove(pid, out var entry)) return null;
		_orderedPids.Remove(pid);
		return entry;
	}
	private void Release(long pid, QueueOutcome outcome = QueueOutcome.Cancelled)
	{
		QueueEntry? entry;
		lock (_admissionLock) entry = RemoveEntry(pid);
		if (entry is not null) DisposeEntry(entry, outcome);
	}
	private static void DisposeEntry(QueueEntry entry, QueueOutcome outcome = QueueOutcome.Cancelled)
	{
		entry.Observation?.Complete(entry.DeferredReleaseOutcome ?? outcome);
		try { entry.OnReleased?.Invoke(); }
		finally { entry.Cts.Dispose(); }
	}
	private bool ReleasePending(long pid)
	{
		QueueEntry? entry;
		lock (_admissionLock)
			// Reservation disposal cannot reclaim a body already published to the consumer.
			entry = _ready.Contains(pid) || _running.Contains(pid) ? null : RemoveEntry(pid);
		if (entry is not null) DisposeEntry(entry);
		return entry is not null;
	}

	private void CancelEntry(QueueEntry entry)
	{
		try { entry.Cts.Cancel(); }
		catch (ObjectDisposedException) { /* The consumer already completed and released this entry. */ }
		catch (AggregateException ex) { logger.LogWarning(ex, "Cancellation callback failed for PID {Pid}", entry.Pid); }
	}

	private async ValueTask<QueueAdmissionResult> Admit(Func<ValueTask<CallState?>> action,
	 string identity, string group, DBRef? executor, long? handle = null, bool ready = true, DBRef? semaphoreTarget = null,
	 Action? onReleased = null, string? sourceAttribute = null, bool managesSemaphoreCount = false, bool notifyOnRejection = true, PendingInputCommand? pendingInput = null)
	{
		// Actorless host callbacks share a bounded system bucket; they do not bypass fairness.
		string owner = handle is null ? "system" : $"handle:{handle}";
		long ownerLimit = configuration?.CurrentValue.Limit.PlayerQueueLimit ?? 100;
		if (executor is not null)
		{
			var target = await mediator.Send(new GetObjectNodeQuery(executor.Value), ExecutionBudget.CurrentToken);
			if (target.IsNone)
			{
				diagnostics?.Rejected(executor, null, DiagnosticKind(group), QueueOutcome.InvalidTarget);
				return Reject(QueueRejectionReason.InvalidTarget);
			}
			executor = target.Known().Object().DBRef;
			if (await target.Known().IsWizard(ExecutionBudget.CurrentToken) || await target.Known().HasPower("Queue", ExecutionBudget.CurrentToken))
				ownerLimit += Math.Max(0, await mediator.Send(new GetObjectCountQuery(), ExecutionBudget.CurrentToken));
			owner = (await target.Known().Object().Owner.WithCancellation(ExecutionBudget.CurrentToken)).Object.DBRef.ToString();
		}
		QueueAdmissionResult result;
		lock (_admissionLock)
		{
			if (_stopping) result = Reject(QueueRejectionReason.ShuttingDown);
			else if (_pendingEntries.Count >= (configuration?.CurrentValue.Limit.GlobalQueueLimit ?? 10000)) result = Reject(QueueRejectionReason.GlobalLimit);
			else if (_pendingEntries.Values.Count(e => e.Owner == owner) >= ownerLimit) result = Reject(QueueRejectionReason.OwnerLimit);
			else
			{
				var pid = NextPid();
				DBRef.TryParse(owner, out var diagnosticOwner);
				var entry = new QueueEntry(pid, $"{identity}-{pid}", group, action, new CancellationTokenSource(), owner, executor, SemaphoreTarget: semaphoreTarget, OnReleased: onReleased, ManagesSemaphoreCount: managesSemaphoreCount)
				{
					Observation = diagnostics?.Admitted(pid, executor, diagnosticOwner, DiagnosticKind(group), sourceAttribute),
					PendingInput = pendingInput
				};
				_pendingEntries[pid] = entry;
				_orderedPids.Add(pid);
				if (ready) { _ready.Add(pid); _immediateQueue.Writer.TryWrite(entry); }
				result = new(pid, QueueRejectionReason.None);
			}
		}
		if (result.Accepted && ready) EnsureConsumerStarted();
		if (!result.Accepted)
		{
			DBRef.TryParse(owner, out var diagnosticOwner);
			diagnostics?.Rejected(executor, diagnosticOwner, DiagnosticKind(group), result.Reason switch
			{
				QueueRejectionReason.GlobalLimit => QueueOutcome.GlobalLimit,
				QueueRejectionReason.OwnerLimit => QueueOutcome.OwnerLimit,
				QueueRejectionReason.ShuttingDown => QueueOutcome.ShuttingDown,
				_ => QueueOutcome.InvalidTarget
			});
		}
		if (!result.Accepted && notifyOnRejection && notifyService is not null)
		{
			if (handle is not null) await notifyService.NotifyLocalized(handle.Value, "QueueRejected", result.Reason);
			else if (DBRef.TryParse(owner, out var player))
				await foreach (var connection in connectionService.Get(player!.Value))
					await notifyService.NotifyLocalized(connection.Handle, "QueueRejected", result.Reason);
		}
		return result;
	}
	private static string? SourceAttribute(ParserState state) => state.CurrentEvaluation is { } current
		&& current.DB == state.Executor ? current.Name : null;
	private async ValueTask<QueueAdmissionResult> RejectInvalidTarget(DBRef? executor, string kind)
	{
		if (diagnostics is not null)
		{
			DBRef? owner = null;
			if (executor is { } reference)
			{
				try
				{
					var source = await mediator.Send(new GetObjectNodeQuery(reference), ExecutionBudget.CurrentToken);
					if (!source.IsNone)
					{
						executor = source.Known().Object().DBRef;
						owner = (await source.Known().Object().Owner.WithCancellation(ExecutionBudget.CurrentToken)).Object.DBRef;
					}
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					logger.LogDebug(ex, "Could not attribute an invalid-target rejection");
				}
			}
			diagnostics.Rejected(executor, owner, kind, QueueOutcome.InvalidTarget);
		}
		return Reject(QueueRejectionReason.InvalidTarget);
	}

	private static string DiagnosticKind(string group) => group.StartsWith(SemaphoreGroup + ":", StringComparison.Ordinal)
		? "semaphore" : group.StartsWith(DelayGroup + ":", StringComparison.Ordinal) ? "delay"
		: group == DirectInputGroup ? "direct-input" : group == EnqueueGroup ? "enqueue" : "other";
	private readonly SemaphoreSlim _semaphoreMutations = new(1, 1);
	private readonly HashSet<long> _delayedRepairs = [];
	private readonly HashSet<long> _semaphorePublications = [];

	// Failed admission repairs retain their PID/quota. No later semaphore transaction
	// may pass this gate until the uncertain write has been restored.
	private readonly ConcurrentDictionary<long, Func<ValueTask>> _semaphoreRepairs = new();

	/// <summary>Serialize semaphore counter transactions. Acquire before any deferred queue lease.</summary>
	public async ValueTask<IDisposable> EnterSemaphoreMutationAsync()
	{
		await _semaphoreMutations.WaitAsync(ExecutionBudget.CurrentToken);
		try
		{
			if (!_semaphoreRepairs.IsEmpty || _semaphoreCommandRepair is not null)
			{
				using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ExecutionBudget.CurrentToken, _shutdownCts.Token);
				using var budget = ExecutionBudget.FromMilliseconds(1000, cancellation.Token);
				using var scope = budget.Enter();
				if (_semaphoreCommandRepair is { } commandRepair)
				{
					await commandRepair();
					_semaphoreCommandRepair = null;
				}
				foreach (var (pid, repair) in _semaphoreRepairs)
				{
					await repair();
					_semaphoreRepairs.TryRemove(pid, out _);
					Release(pid, QueueOutcome.ScheduleFailed);
				}
			}
			return new SemaphoreMutationLease(_semaphoreMutations);
		}
		catch { _semaphoreMutations.Release(); throw; }
	}

	private sealed class SemaphoreMutationLease(SemaphoreSlim gate) : IDisposable
	{
		private SemaphoreSlim? _gate = gate;
		public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
	}

	// Caller holds the semaphore mutation lease. Keep an uncertain Halt write behind
	// the existing command-repair barrier until its before/after value is reconciled.
	private async ValueTask AdjustHaltSemaphoreCountCore(QueueEntry entry)
	{
		var semaphore = DbRefAttribute.Parse(entry.Group[(SemaphoreGroup.Length + 1)..]);
		if (entry.SemaphoreTarget is { } target) semaphore = new(target, semaphore.Attribute);
		async ValueTask<SharpAttribute?> Read() => await mediator.CreateStream(
			new GetAttributeQuery(semaphore.DbRef, semaphore.Attribute), ExecutionBudget.CurrentToken)
			.LastOrDefaultAsync(ExecutionBudget.CurrentToken);
		var attribute = await Read();
		if (attribute is null || !int.TryParse(attribute.Value.ToPlainText(), out var original)) return;
		var expected = original > 0 ? original - 1 : 0;
		var god = await mediator.Send(new GetObjectNodeQuery(new DBRef(1)), ExecutionBudget.CurrentToken);
		if (!god.IsPlayer) return;
		async ValueTask Write()
		{
			if (!await mediator.Send(new SetAttributeCommand(semaphore.DbRef, semaphore.Attribute,
				MarkupString.MarkupText.Plain(expected.ToString()), god.AsPlayer), ExecutionBudget.CurrentToken))
				throw new InvalidOperationException("Semaphore count update failed.");
			ExecutionBudget.Current?.ThrowIfExceeded();
		}
		try { await Write(); }
		catch
		{
			lock (_admissionLock)
			{
				_ready.Remove(entry.Pid);
				_semaphoreCommandReservations.Add(entry.Pid);
			}
			_semaphoreCommandRepair = async () =>
			{
				using var deferred = await LockDeferred();
				var observed = await Read();
				if (observed is null || !int.TryParse(observed.Value.ToPlainText(), out var current)
					|| current != expected && current != original)
					throw new InvalidOperationException("Semaphore changed during uncertain accounting.");
				if (current != expected) await Write();
				ExecutionBudget.Current?.ThrowIfExceeded();
				lock (_admissionLock)
				{
					_semaphoreCommandReservations.Remove(entry.Pid);
					// Halt settled transport before attempting the counter write. This
					// cancelled entry now needs consumer disposal only, not another halt
					// decrement or retained-notification cleanup attempt.
					if (_pendingEntries.TryGetValue(entry.Pid, out var pending))
						_pendingEntries[entry.Pid] = pending with { Deferred = null, HaltAccountingSettled = true };
				}
				await Activate(entry.Pid);
			};
			throw;
		}
	}

	// Accounting remains queue wait; only the consumer starts body observation timing.
	// Caller owns both transition leases. Counter confirmation and transport cleanup
	// form one settlement; the existing command barrier retains failed settlements.
	private async ValueTask<QueueAdmissionResult> SettleTimeout(QueueEntry entry)
	{
		var semaphore = DbRefAttribute.Parse(entry.Group[(SemaphoreGroup.Length + 1)..]);
		if (entry.SemaphoreTarget is { } target) semaphore = new(target, semaphore.Attribute);
		async ValueTask<SharpAttribute?> Read() => await mediator.CreateStream(
			new GetAttributeQuery(semaphore.DbRef, semaphore.Attribute), ExecutionBudget.CurrentToken)
			.LastOrDefaultAsync(ExecutionBudget.CurrentToken);
		var attribute = await Read();
		var original = 0;
		var accounted = attribute is null || !int.TryParse(attribute.Value.ToPlainText(), out original);
		var expected = original > 0 ? original - 1 : 0;
		var god = accounted ? default : await mediator.Send(new GetObjectNodeQuery(new DBRef(1)), ExecutionBudget.CurrentToken);
		accounted = accounted || god?.IsPlayer != true;
		JobKey? transportJob = null;
		async ValueTask<QueueAdmissionResult> Complete(bool retry)
		{
			if (!accounted)
			{
				var current = original;
				if (retry)
				{
					var observed = await Read();
					if (observed is null || !int.TryParse(observed.Value.ToPlainText(), out current)
						|| current != expected && current != original)
						throw new InvalidOperationException("Semaphore changed during uncertain timeout accounting.");
				}
				if ((!retry || current != expected) && !await mediator.Send(new SetAttributeCommand(semaphore.DbRef, semaphore.Attribute,
					MarkupString.MarkupText.Plain(expected.ToString()), god!.AsPlayer), ExecutionBudget.CurrentToken))
					throw new InvalidOperationException("Semaphore timeout count update failed.");
				accounted = true;
			}
			ExecutionBudget.Current?.ThrowIfExceeded();
			// Normal settlement already holds the deferred lease; repair enters from the semaphore gate.
			using DeferredLease? lease = retry ? await LockDeferred() : null;
			var key = new TriggerKey(entry.TriggerName, entry.Group);
			var trigger = await _scheduler.GetTrigger(key, ExecutionBudget.CurrentToken);
			transportJob ??= trigger?.JobKey;
			await _scheduler.UnscheduleJob(key, ExecutionBudget.CurrentToken);
			if (transportJob is not null) await _scheduler.DeleteJob(transportJob, ExecutionBudget.CurrentToken);
			lock (_admissionLock) _semaphoreCommandReservations.Remove(entry.Pid);
			return await Activate(entry.Pid);
		}
		try { return await Complete(retry: false); }
		catch
		{
			lock (_admissionLock) _semaphoreCommandReservations.Add(entry.Pid);
			_semaphoreCommandRepair = async () =>
			{
				await Complete(retry: true);
			};
			throw;
		}
	}

	private ValueTask<QueueAdmissionResult> Activate(long pid)
	{
		lock (_admissionLock)
		{
			if (_stopping) return ValueTask.FromResult(Reject(QueueRejectionReason.ShuttingDown));
			if (!_pendingEntries.TryGetValue(pid, out var entry) || _semaphoreRepairs.ContainsKey(pid) || _semaphoreCommandReservations.Contains(pid) || _delayedRepairs.Contains(pid)) return ValueTask.FromResult(new QueueAdmissionResult(null, QueueRejectionReason.AlreadyReleased));
			if (entry.Deferred?.Paused == true)
			{
				if (entry.Deferred.ReleasePending) return ValueTask.FromResult(new QueueAdmissionResult(null, QueueRejectionReason.AlreadyReleased));
				_pendingEntries[pid] = entry with { Deferred = entry.Deferred with { ReleasePending = true } };
				return ValueTask.FromResult(new QueueAdmissionResult(pid, QueueRejectionReason.None));
			}
			if (!_ready.Add(pid)) return ValueTask.FromResult(new QueueAdmissionResult(null, QueueRejectionReason.AlreadyReleased));
			entry = entry with { Group = EnqueueGroup };
			_pendingEntries[pid] = entry;
			_immediateQueue.Writer.TryWrite(entry);
		}
		EnsureConsumerStarted();
		return ValueTask.FromResult(new QueueAdmissionResult(pid, QueueRejectionReason.None));
	}
	private async ValueTask<CallState?> ExecuteList(MString command, ParserState state)
	{
		if (state.Executor is not null && (await mediator.Send(new GetObjectNodeQuery(state.Executor.Value), ExecutionBudget.CurrentToken)).IsNone) return null;
		// Deferred bodies cannot consume the submitting command list's break/include state.
		return await parser.FromState(state with { ExecutionStack = [], BreakPropagation = null }).CommandListParse(command);
	}
	private readonly ConcurrentDictionary<long, QueueEntry> _pendingEntries = new();
	private readonly CancellationTokenSource _shutdownCts = new();
	private Task? _consumerTask;

	private void EnsureConsumerStarted()
	{
		lock (_admissionLock)
		{
			if (_consumerTask is not null) return;
			// The consumer outlives its submitter. Each entry creates its own budget;
			// release callbacks must not restore a disposed submitting context afterward.
			using var flow = ExecutionContext.IsFlowSuppressed() ? default : ExecutionContext.SuppressFlow();
			_consumerTask = Task.Run(() => ProcessQueueAsync(_shutdownCts.Token));
		}
	}

	private async Task ProcessQueueAsync(CancellationToken shutdownToken)
	{
		try
		{
			await foreach (var entry in _immediateQueue.Reader.ReadAllAsync(shutdownToken))
			{
				try
				{
					lock (_admissionLock) _running.Add(entry.Pid);
					var milliseconds = configuration?.CurrentValue.Limit.QueueEntryCpuTime ?? 1000;
					lock (_admissionLock)
					{
						if (_stopping || entry.Cts.IsCancellationRequested) continue;
					}
					using var budget = ExecutionBudget.FromMilliseconds(milliseconds, entry.Cts.Token);
					using var scope = budget.Enter();
					using var observation = entry.Observation?.Enter();
					try
					{
						budget.ThrowIfExceeded();
						await entry.Action();
						var outcome = budget.IsExpired ? QueueOutcome.ExecutionLimit
							: budget.IsCancelled ? QueueOutcome.Cancelled : QueueOutcome.Completed;
						entry.Observation?.Complete(outcome);
						if (outcome == QueueOutcome.ExecutionLimit) await NotifyExpired(entry);
					}
					catch (OperationCanceledException) when (budget.IsExpired) { entry.Observation?.Complete(QueueOutcome.ExecutionLimit); await NotifyExpired(entry); }
					catch (OperationCanceledException) when (budget.IsCancelled) { entry.Observation?.Complete(QueueOutcome.Cancelled); logger.LogDebug("Queued command {Pid} cancelled", entry.Pid); }
				}
				catch (Exception ex) { entry.Observation?.Complete(QueueOutcome.Failed); logger.LogError(ex, "Error executing queued command (PID {Pid}, Group {Group})", entry.Pid, entry.Group); }
				finally { Release(entry.Pid); }
			}
		}
		catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested) { }
	}

	private async ValueTask NotifyExpired(QueueEntry entry)
	{
		logger.LogWarning("Execution budget exhausted (PID {Pid})", entry.Pid);
		if (notifyService is null || entry.Cts.IsCancellationRequested || _shutdownCts.IsCancellationRequested) return;
		// Reporting has its own bounded I/O lifetime after the user execution deadline.
		using var reportBudget = ExecutionBudget.FromMilliseconds(1000, _shutdownCts.Token);
		using var reportScope = reportBudget.Enter();
		try
		{
			if (DBRef.TryParse(entry.Owner, out var owner))
				await foreach (var connection in connectionService.Get(owner!.Value)) await notifyService.Notify(connection.Handle, ExecutionBudget.Error);
			else if (entry.Owner.StartsWith("handle:") && long.TryParse(entry.Owner[7..], out var handle))
				await notifyService.Notify(handle, ExecutionBudget.Error);
		}
		catch (Exception ex) { logger.LogWarning(ex, "Could not report execution limit for PID {Pid}", entry.Pid); }
	}

	public ValueTask<QueueAdmissionResult> AdmitWork(Func<ValueTask<CallState?>> action, string triggerName, string group)
	 => Admit(action, triggerName, group, null);

	public ValueTask<QueueAdmissionResult> AdmitWork(Func<ValueTask<CallState?>> action, string triggerName, string group, DBRef executor, bool notifyOnRejection = true)
	 => Admit(action, triggerName, group, executor, notifyOnRejection: notifyOnRejection);

	public ValueTask<QueueAdmissionResult> ReleaseScheduledWork(long pid, bool semaphoreTimeout = false)
		=> ReleaseScheduledWork(pid, semaphoreTimeout, null);

	public async ValueTask<QueueAdmissionResult> ReleaseScheduledWork(long pid, bool semaphoreTimeout, long? generation)
	{
		QueueEntry entry;
		bool recoveringTimeout;
		lock (_admissionLock)
		{
			if (_stopping) return Reject(QueueRejectionReason.ShuttingDown);
			if (!_pendingEntries.TryGetValue(pid, out entry!) || _ready.Contains(pid)
				|| _semaphoreRepairs.ContainsKey(pid) || (!semaphoreTimeout && _semaphoreCommandReservations.Contains(pid)) || _delayedRepairs.Contains(pid))
				return new(null, QueueRejectionReason.AlreadyReleased);
			recoveringTimeout = semaphoreTimeout && _semaphoreCommandReservations.Contains(pid);
		}
		using var releaseCancellation = CancellationTokenSource.CreateLinkedTokenSource(ExecutionBudget.CurrentToken, _shutdownCts.Token);
		var milliseconds = configuration?.CurrentValue.Limit.QueueEntryCpuTime ?? 1000;
		using var releaseBudget = ExecutionBudget.FromMilliseconds(milliseconds == 0 ? 1000 : milliseconds, releaseCancellation.Token);
		using var releaseScope = releaseBudget.Enter();
		releaseBudget.ThrowIfExceeded();
		// Counter writes precede deferred transitions; failed persistence retains the PID.
		using var mutation = entry.Group.StartsWith(SemaphoreGroup + ":", StringComparison.Ordinal)
			? await EnterSemaphoreMutationAsync() : null;
		if (recoveringTimeout) return new(pid, QueueRejectionReason.None);
		using var lease = await LockDeferred();
		lock (_admissionLock)
		{
			if (_stopping) return Reject(QueueRejectionReason.ShuttingDown);
			if (!_pendingEntries.TryGetValue(pid, out entry!) || _ready.Contains(pid)
				|| entry.Deferred?.ReleasePending == true || (semaphoreTimeout && entry.Deferred?.CleanupJob is not null) || _delayedRepairs.Contains(pid)
				|| generation is not null && entry.Deferred?.Generation != generation)
				return new(null, QueueRejectionReason.AlreadyReleased);
		}
		if (semaphoreTimeout && entry.Group.StartsWith(SemaphoreGroup + ":", StringComparison.Ordinal))
			return await SettleTimeout(entry);
		await RemoveDeferredTrigger(entry);
		return await Activate(pid);
	}


	private readonly IScheduler _scheduler = schedulerFactory.GetScheduler().GetAwaiter().GetResult();
	public const string DirectInputGroup = "direct-input";
	public const string EnqueueGroup = "enqueue";
	public const string SemaphoreGroup = "semaphore";
	public const string DelayGroup = "delay";

	public async ValueTask<QueueAdmissionResult> AdmitUserCommand(long handle, MString command, ParserState state)
	{
		var snapshot = inputSessions?.CapturePendingInput(handle) ?? default;
		var generation = snapshot.Ticket?.InitialGeneration ?? inputSessions?.GetCaptureGeneration(handle);
		if (inputSessions is not null && await inputSessions.TryEscapeAsync(handle, state.ConnectionSessionId, command, snapshot.Session?.Id))
			return new QueueAdmissionResult(0, QueueRejectionReason.None);
		var connection = connectionService.Get(handle);
		if (inputSessions is not null && snapshot.Ticket is not null
			&& command.Text.Equals("@input/cancel", StringComparison.OrdinalIgnoreCase)
			&& connection is not null
			&& (connection.Metadata.GetValueOrDefault("SessionId") ?? "") == (state.ConnectionSessionId ?? ""))
		{
			var marked = false;
			lock (_admissionLock)
			{
				foreach (var pair in _pendingEntries)
				{
					if (pair.Value.PendingInput is not { Completed: false } pending || pending.Handle != handle
						|| !ReferenceEquals(pending.Connection, connection)
						|| !ReferenceEquals(pending.Ticket, snapshot.Ticket)
						|| (pending.Transport ?? "") != (state.ConnectionSessionId ?? "")) continue;
					pending.EscapeRequested = true;
					marked = true;
				}
			}
			if (marked) return new QueueAdmissionResult(0, QueueRejectionReason.None);
			// A start may have finished between the first escape check and the ledger scan.
			if (await inputSessions.TryEscapeAsync(handle, state.ConnectionSessionId, command, snapshot.Ticket.ExpectedGeneration))
				return new QueueAdmissionResult(0, QueueRejectionReason.None);
		}
		var pendingCommand = new PendingInputCommand(handle, connection, state.ConnectionSessionId, snapshot.Ticket);
		var capture = snapshot.Session;
		return await Admit(async () =>
		{
			try
			{
				if (!string.IsNullOrEmpty(state.ConnectionSessionId) && connectionService.Get(handle)?.Metadata.GetValueOrDefault("SessionId") != state.ConnectionSessionId) return null;
				// Replies admitted before startup belong to the first capture that follows admission.
				var session = capture ?? inputSessions?.GetCapturing(handle);
				if (capture is null && session is not null)
				{
					if (snapshot.Ticket?.ExpectedGeneration != session.Id) return null;
					if (await inputSessions!.TryEscapeAsync(handle, state.ConnectionSessionId, command, session.Id)) return null;
				}
				// A captured generation never becomes ordinary command text, even if cancelled while queued.
				if (session is not null)
				{
					if ((session.TransportSessionId ?? "") != (state.ConnectionSessionId ?? "")) return null;
					return await inputSessions!.DeliverAsync(parser, session, command);
				}
				// A capture that opened after admission still owns this reply after it ends.
				if (inputSessions?.GetCaptureGeneration(handle) != generation) return null;
				if (string.IsNullOrWhiteSpace(command.Text)) return null;
				return await parser.FromState(state).CommandParse(handle, connectionService, command);
			}
			finally
			{
				bool escape;
				lock (_admissionLock)
				{
					pendingCommand.Completed = true;
					escape = pendingCommand.EscapeRequested;
				}
				if (escape && inputSessions is not null && pendingCommand.Ticket is { } ticket
					&& ReferenceEquals(connectionService.Get(handle), pendingCommand.Connection))
					await inputSessions.TryEscapeAsync(handle, pendingCommand.Transport, MString.Plain("@input/cancel"), ticket.ExpectedGeneration);
			}
		}, $"handle:{handle}", DirectInputGroup, connectionService.Get(handle)?.Ref, handle, pendingInput: pendingCommand);
	}

	public ValueTask<QueueAdmissionResult> WriteInputSessionTimeout(InputSession session)
		=> inputSessions is null
			? ValueTask.FromResult(new QueueAdmissionResult(null, QueueRejectionReason.InvalidTarget))
			: Admit(() => inputSessions.DeliverAsync(parser, session, MString.Empty, timeout: true),
				$"input-session:{session.Id}", EnqueueGroup, session.Executor, onReleased: () => inputSessions.Discard(session), notifyOnRejection: false);

	private async ValueTask<ParserState> CaptureExecutor(ParserState state)
	{
		if (state.Executor is not { } executor) return state;
		var target = await mediator.Send(new GetObjectNodeQuery(executor), ExecutionBudget.CurrentToken);
		return target.IsNone ? state : state with { Executor = target.Known().Object().DBRef };
	}
	public async ValueTask<QueueAdmissionResult> AdmitCommandList(MString command, ParserState state)
	{
		state = await CaptureExecutor(state);
		return await Admit(() => ExecuteList(command, state), $"dbref:{state.Executor}", EnqueueGroup, state.Executor, sourceAttribute: SourceAttribute(state));
	}

	public async ValueTask<QueueCommandReservation> ReserveCommandList(MString command, ParserState state)
	{
		state = await CaptureExecutor(state);
		var admission = await Admit(() => ExecuteList(command, state), $"dbref:{state.Executor}", EnqueueGroup, state.Executor, ready: false);
		if (!admission.Accepted) return QueueCommandReservation.Rejected(admission.Reason);
		var pid = admission.Pid!.Value;
		return new QueueCommandReservation(admission, () => Activate(pid), () => ReleasePending(pid));
	}

	public ValueTask<QueueAdmissionResult> AdmitCommandList(MString command, ParserState state, DbRefAttribute dbRefAttribute, int oldValue, bool manageSemaphoreCount = false)
	 => AdmitCommandList(command, state, dbRefAttribute, oldValue, TimeSpan.FromDays(36500), manageSemaphoreCount);

	public async ValueTask<QueueAdmissionResult> AdmitAsyncAttribute(Func<ValueTask<ParserState>> function, DbRefAttribute dbAttribute, DBRef? executor = null)
	{
		var target = await mediator.Send(new GetObjectNodeQuery(dbAttribute.DbRef), ExecutionBudget.CurrentToken);
		if (target.IsNone) return await RejectInvalidTarget(executor ?? dbAttribute.DbRef, EnqueueGroup);
		dbAttribute = new DbRefAttribute(target.Known().Object().DBRef, dbAttribute.Attribute);
		executor = (await CaptureExecutor(ParserState.Empty with { Executor = executor ?? dbAttribute.DbRef })).Executor;
		return await Admit(async () =>
		{
			if (executor is not null && (await mediator.Send(new GetObjectNodeQuery(executor.Value), ExecutionBudget.CurrentToken)).IsNone) return null;
			var obj = await mediator.Send(new GetObjectNodeQuery(dbAttribute.DbRef), ExecutionBudget.CurrentToken);
			if (obj.IsNone) return null;
			var parserState = await function();
			ExecutionBudget.Current?.ThrowIfExceeded();
			var actor = await parserState.KnownExecutorObject(mediator);
			var attr = await attributeService.GetAttributeAsync(actor, obj.Known, string.Join('`', dbAttribute.Attribute), IAttributeService.AttributeMode.Execute);
			if (!attr.IsAttribute) return new CallState("#-1");
			return await ExecuteList(attr.AsAttribute.Last().Value, parserState);
		}, $"async:{dbAttribute}", EnqueueGroup, executor, sourceAttribute: dbAttribute.DbRef == executor ? string.Join('`', dbAttribute.Attribute) : null);
	}

	public async ValueTask<QueueAdmissionResult> AdmitCommandList(MString command, ParserState state,
	 DbRefAttribute dbRefAttribute, int oldValue, TimeSpan timeout, bool manageSemaphoreCount = false)
	{
		// Direct callers have no queue-entry budget; shutdown must still interrupt
		// every provider operation while this transaction owns the semaphore gate.
		using var transactionCancellation = CancellationTokenSource.CreateLinkedTokenSource(ExecutionBudget.CurrentToken, _shutdownCts.Token);
		var remaining = ExecutionBudget.Current?.Remaining ?? TimeSpan.FromSeconds(1);
		using var transactionBudget = new ExecutionBudget(remaining == TimeSpan.MaxValue ? TimeSpan.FromSeconds(1) : remaining,
			transactionCancellation.Token);
		using var transactionScope = transactionBudget.Enter();
		transactionBudget.ThrowIfExceeded();
		if (!manageSemaphoreCount && oldValue < 0) return await AdmitCommandList(command, state);
		// Serialize counter mutation before exposing a reservation or taking the deferred lease.
		using var mutation = await EnterSemaphoreMutationAsync();
		using var lease = await LockDeferred();
		state = await CaptureExecutor(state);
		var target = await mediator.Send(new GetObjectNodeQuery(dbRefAttribute.DbRef), ExecutionBudget.CurrentToken);
		if (target.IsNone) return await RejectInvalidTarget(state.Executor, SemaphoreGroup);
		var group = $"{SemaphoreGroup}:{dbRefAttribute}";
		var admission = await Admit(() => ExecuteList(command, state), $"dbref:{state.Executor}", group, state.Executor, ready: false, semaphoreTarget: target.Known().Object().DBRef, sourceAttribute: SourceAttribute(state), managesSemaphoreCount: manageSemaphoreCount);
		if (!admission.Accepted) return admission;
		var pid = admission.Pid!.Value;
		lock (_admissionLock) _semaphorePublications.Add(pid);
		var scheduleWriteAttempted = false;
		var triggerKey = new TriggerKey($"dbref:{state.Executor}-{pid}", group);
		async ValueTask Schedule(DateTimeOffset due, long generation)
		{
			QueueEntry publicationEntry;
			lock (_admissionLock) publicationEntry = _pendingEntries[pid];
			using var publication = CancellationTokenSource.CreateLinkedTokenSource(ExecutionBudget.CurrentToken, publicationEntry.Cts.Token);
			await _scheduler.ScheduleJob(JobBuilder.Create<SemaphoreTask>()
				.SetJobData(new JobDataMap((IDictionary<string, object>)new Dictionary<string, object>
				{ { "Command", command }, { "State", state }, { "Generation", generation } })).Build(),
				TriggerBuilder.Create().WithSimpleSchedule(x => x.WithRepeatCount(0)).StartAt(due)
					.WithIdentity(triggerKey).Build(), publication.Token);
			publication.Token.ThrowIfCancellationRequested();
			ExecutionBudget.Current?.ThrowIfExceeded();
		}
		var counterWriteAttempted = false;
		var counterCreated = false;
		var currentCount = oldValue;
		SharpPlayer? god = null;
		var fullTarget = target.Known().Object().DBRef;
		try
		{
			if (manageSemaphoreCount)
			{
				bool cancelled;
				lock (_admissionLock)
					cancelled = !_pendingEntries.TryGetValue(admission.Pid!.Value, out var entry) || entry.Cts.IsCancellationRequested;
				if (cancelled)
				{
					Release(admission.Pid!.Value);
					return new(null, QueueRejectionReason.AlreadyReleased);
				}
				var attribute = await mediator.CreateStream(new GetAttributeQuery(fullTarget, dbRefAttribute.Attribute), ExecutionBudget.CurrentToken).LastOrDefaultAsync(ExecutionBudget.CurrentToken);
				counterCreated = attribute is null;
				if (attribute is null || attribute.Value.Length == 0) currentCount = 0;
				else if (!int.TryParse(attribute.Value.ToPlainText(), out currentCount) || currentCount == int.MaxValue)
				{
					Release(admission.Pid!.Value, QueueOutcome.InvalidTarget);
					return Reject(QueueRejectionReason.InvalidTarget);
				}
				god = (await mediator.Send(new GetObjectNodeQuery(new DBRef(1)), ExecutionBudget.CurrentToken)).AsPlayer;
				var nextCount = checked(currentCount + 1);
				// A cancelled acknowledgement does not prove the provider failed to commit.
				counterWriteAttempted = true;
				if (!await mediator.Send(new SetAttributeCommand(fullTarget, dbRefAttribute.Attribute,
					MarkupString.MarkupText.Plain(nextCount.ToString()), god), ExecutionBudget.CurrentToken))
					throw new InvalidOperationException("Semaphore count update failed.");
				if (attribute is null)
					await SemaphoreAttributes.InitializeAsync(mediator, fullTarget, dbRefAttribute.Attribute);
				if (currentCount < 0)
				{
					var activated = await Activate(admission.Pid!.Value);
					if (activated.Accepted) return activated;
					throw new OperationCanceledException("Semaphore reservation was released before activation.");
				}
			}
			timeout = Nonnegative(timeout);
			var due = DateTimeOffset.UtcNow + timeout;
			lock (_admissionLock) _pendingEntries[pid] = _pendingEntries[pid] with
			{ Deferred = new(due, Schedule, dbRefAttribute, command, state) };
			scheduleWriteAttempted = true;
			await Schedule(due, 0);
			return admission;
		}
		catch (Exception admissionFailure)
		{
			SemaphoreRepairIdentity? createdIdentity = null;
			async ValueTask Restore(bool retry)
			{
				// A failed acknowledgement may hide a committed trigger. Remove it before
				// restoring the counter or making its PID available for another admission.
				if (scheduleWriteAttempted)
				{
					await _scheduler.UnscheduleJob(triggerKey, ExecutionBudget.CurrentToken);
					scheduleWriteAttempted = false;
				}
				if (!counterWriteAttempted) return;
				if (retry || counterCreated)
				{
					var attribute = await mediator.CreateStream(new GetAttributeQuery(fullTarget, dbRefAttribute.Attribute), ExecutionBudget.CurrentToken)
						.LastOrDefaultAsync(ExecutionBudget.CurrentToken);
					if (counterCreated && attribute is null) return;
					if (attribute is null || !int.TryParse(attribute.Value.ToPlainText(), out var persisted))
						throw new InvalidOperationException("Semaphore changed before admission repair; restore its original counter or remove the newly created attribute before retrying.");
					if (!counterCreated && persisted == currentCount) return;
					if (persisted != checked(currentCount + 1))
						throw new InvalidOperationException("Semaphore changed before admission repair; refusing to overwrite a later counter value.");
					if (counterCreated)
					{
						var identity = await CaptureRepairIdentity(attribute);
						// A read outage may prevent the first snapshot. Accept the first successful
						// read only if it still has the exact counter and metadata we create.
						var expectedCreation = identity.Owner == god!.Object.DBRef
							&& identity.CommandListIndex is null
							&& identity.Name.Equals(dbRefAttribute.Attribute[^1], StringComparison.OrdinalIgnoreCase)
							&& identity.LongName.Equals(string.Join('`', dbRefAttribute.Attribute), StringComparison.OrdinalIgnoreCase)
							&& identity.Flags.Split('\0', StringSplitOptions.RemoveEmptyEntries)
								.All(flag => SemaphoreAttributes.RequiredFlagNames.Contains(flag, StringComparer.OrdinalIgnoreCase));
						if (createdIdentity is null && expectedCreation) createdIdentity = identity;
						else if (createdIdentity != identity)
							throw new InvalidOperationException("Created semaphore metadata changed or could not be verified; remove the newly created attribute before retrying admission repair.");
					}
				}
				var restored = counterCreated
					? await mediator.Send(new WipeAttributeCommand(fullTarget, dbRefAttribute.Attribute), ExecutionBudget.CurrentToken)
					: await mediator.Send(new SetAttributeCommand(fullTarget, dbRefAttribute.Attribute,
						MarkupString.MarkupText.Plain(currentCount.ToString()), god!), ExecutionBudget.CurrentToken);
				if (!restored) throw new InvalidOperationException("Semaphore admission rollback failed.");
			}
			try
			{
				using var cleanup = ExecutionBudget.FromMilliseconds(1000);
				using var cleanupScope = cleanup.Enter();
				await Restore(retry: false);
			}
			catch (Exception cleanupFailure)
			{
				_semaphoreRepairs[admission.Pid!.Value] = () => Restore(retry: true);
				if (_pendingEntries.TryGetValue(admission.Pid.Value, out var retained)) CancelEntry(retained);
				logger.LogError(cleanupFailure, "Semaphore admission repair retained for PID {Pid} at {Target}/{Attribute} (original counter: {Original}); further semaphore mutations require successful repair",
					admission.Pid, fullTarget, string.Join('`', dbRefAttribute.Attribute), counterCreated ? "absent" : currentCount.ToString());
				throw new AggregateException("Semaphore admission and rollback failed; accounting retained for retry.", admissionFailure, cleanupFailure);
			}
			Release(admission.Pid!.Value, QueueOutcome.ScheduleFailed);
			throw;
		}
		finally
		{
			lock (_admissionLock) _semaphorePublications.Remove(admission.Pid!.Value);
		}
	}

	public async ValueTask<IReadOnlyList<QueueAdmissionResult>> NotifyCounted(DbRefAttribute dbAttribute, int oldValue, int count = 1)
	{
		using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ExecutionBudget.CurrentToken, _shutdownCts.Token);
		var remaining = ExecutionBudget.Current?.Remaining ?? TimeSpan.FromSeconds(1);
		using var budget = new ExecutionBudget(remaining == TimeSpan.MaxValue ? TimeSpan.FromSeconds(1) : remaining, cancellation.Token);
		using var scope = budget.Enter();
		using var lease = await LockDeferred();
		QueueEntry[] waiting;
		var group = $"{SemaphoreGroup}:{dbAttribute}";
		lock (_admissionLock)
			waiting = _pendingEntries.Values.Where(e => e.Group == group && !_ready.Contains(e.Pid)
				&& e.Deferred?.ReleasePending != true && !_semaphoreCommandReservations.Contains(e.Pid) && !_semaphoreRepairs.ContainsKey(e.Pid) && !_semaphorePublications.Contains(e.Pid)).OrderBy(e => e.Pid).Take(Math.Max(0, count)).ToArray();
		var outcomes = new List<QueueAdmissionResult>(waiting.Length);
		foreach (var entry in waiting)
		{
			await RemoveDeferredTrigger(entry);
			outcomes.Add(await Activate(entry.Pid));
		}
		return outcomes;
	}

	public ValueTask<IReadOnlyList<QueueAdmissionResult>> NotifyAllCounted(DbRefAttribute dbAttribute)
	 => NotifyCounted(dbAttribute, 0, int.MaxValue);

	public async ValueTask<bool> ModifyQRegisters(DbRefAttribute dbAttribute, Dictionary<string, MString> qRegisters)
	{
		if (qRegisters is null || qRegisters.Count == 0) return false;
		using var lease = await LockDeferred();
		QueueEntry? entry;
		var group = $"{SemaphoreGroup}:{dbAttribute}";
		lock (_admissionLock)
			entry = _pendingEntries.Values.Where(e => e.Group == group && !_ready.Contains(e.Pid)
				&& e.Deferred?.ReleasePending != true && !_semaphoreCommandReservations.Contains(e.Pid) && !_semaphoreRepairs.ContainsKey(e.Pid) && !_semaphorePublications.Contains(e.Pid)).MinBy(e => e.Pid);
		if (entry?.Deferred is not { } deferred) return false;
		if (!deferred.State.Registers.TryPeek(out var registers))
		{
			registers = new Dictionary<string, MString>();
			deferred.State.Registers.Push(registers);
		}
		foreach (var (key, value) in qRegisters) registers[key.ToUpperInvariant()] = value;
		return true;
	}

	public async ValueTask Drain(DbRefAttribute dbAttribute, int? count = null)
		=> _ = await DrainCounted(dbAttribute, count);

	public async ValueTask<int> DrainCounted(DbRefAttribute dbAttribute, int? count = null)
	{
		QueueEntry[] removed;
		using (await LockDeferred())
		{
			var group = $"{SemaphoreGroup}:{dbAttribute}";
			lock (_admissionLock)
			{
				removed = _pendingEntries.Values.Where(e => e.Group == group && !_ready.Contains(e.Pid)
					&& e.Deferred?.ReleasePending != true && !_semaphoreCommandReservations.Contains(e.Pid) && !_semaphoreRepairs.ContainsKey(e.Pid) && !_semaphorePublications.Contains(e.Pid)).OrderBy(e => e.Pid).Take(Math.Max(0, count ?? int.MaxValue)).ToArray();
			}
			foreach (var entry in removed) await RemoveDeferredTrigger(entry);
			lock (_admissionLock)
				foreach (var entry in removed) RemoveEntry(entry.Pid);
		}
		foreach (var entry in removed) DisposeEntry(entry);
		return removed.Length;
	}

	public async ValueTask Halt(DBRef dbRef)
	{
		long[] pids;
		lock (_admissionLock) pids = _pendingEntries.Values
			.Where(entry => entry.Executor?.Matches(dbRef) == true
				|| (entry.Group.StartsWith(SemaphoreGroup + ":") && entry.SemaphoreTarget?.Matches(dbRef) == true))
			.Select(entry => entry.Pid).ToArray();
		foreach (var pid in pids) await HaltByPid(pid);
	}

	public async ValueTask<bool> HaltByPid(long pid)
	{
		var result = await HaltByPidCore(pid);
		bool settled;
		lock (_admissionLock)
			settled = _pendingEntries.TryGetValue(pid, out var entry) && entry.HaltAccountingSettled;
		// Core's transition leases have been released. Keep successful Halt's
		// synchronous quota release, including a recovered canceled consumer item.
		if (settled) Release(pid);
		return result;
	}

	private async ValueTask<bool> HaltByPidCore(long pid)
	{
		QueueEntry? entry;
		bool ready;
		lock (_admissionLock)
		{
			if (!_pendingEntries.TryGetValue(pid, out entry)) return false;
			ready = _ready.Contains(pid);
		}
		// User cancellation callbacks must never run while either scheduler lock is held.
		CancelEntry(entry);
		if (ready) return true;
		// Halt may be called outside an executing queue entry. Keep provider cleanup
		// bounded and interruptible by shutdown while awaiting every write to settle.
		using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ExecutionBudget.CurrentToken, _shutdownCts.Token);
		using var haltBudget = ExecutionBudget.FromMilliseconds(1000, cancellation.Token);
		using var haltScope = haltBudget.Enter();
		using (entry.Group.StartsWith(SemaphoreGroup + ":", StringComparison.Ordinal) ? await EnterSemaphoreMutationAsync() : null)
		using (await LockDeferred())
		{
			lock (_admissionLock)
			{
				if (!_pendingEntries.TryGetValue(pid, out entry) || _ready.Contains(pid)) return true;
			}
			await RemoveDeferredTrigger(entry);
			// Both transition leases keep the cancelled reservation retryable until persistence succeeds.
			// Notifications and all timeouts account before becoming release-pending.
			if (entry.Group.StartsWith(SemaphoreGroup + ":") && entry.Deferred?.ReleasePending != true)
				await AdjustHaltSemaphoreCountCore(entry);
			lock (_admissionLock)
			{
				_delayedRepairs.Remove(pid);
				entry = RemoveEntry(pid);
			}
			if (entry is null) return true;
		}
		DisposeEntry(entry);
		return true;
	}

	public async ValueTask<QueueAdmissionResult> AdmitCommandList(MString command, ParserState state, TimeSpan delay)
	{
		using var lease = await LockDeferred();
		state = await CaptureExecutor(state);
		var group = $"{DelayGroup}:{state.Executor}";
		delay = Nonnegative(delay);
		var due = DateTimeOffset.UtcNow + delay;
		var admission = await Admit(() => ExecuteList(command, state), $"dbref:{state.Executor}", group, state.Executor, ready: false, sourceAttribute: SourceAttribute(state));
		if (!admission.Accepted) return admission;
		var pid = admission.Pid!.Value;
		QueueEntry entry;
		lock (_admissionLock) entry = _pendingEntries[pid];
		var trigger = new TriggerKey(entry.TriggerName, entry.Group);
		async ValueTask Schedule(DateTimeOffset due, long generation)
		{
			using var publication = CancellationTokenSource.CreateLinkedTokenSource(ExecutionBudget.CurrentToken, entry.Cts.Token);
			await _scheduler.ScheduleJob(JobBuilder.Create<DelayedTask>()
				.SetJobData(new JobDataMap { ["Generation"] = generation }).Build(),
				TriggerBuilder.Create().StartAt(due).WithSimpleSchedule(x => x.WithRepeatCount(0))
					.WithIdentity(trigger).Build(), publication.Token);
			publication.Token.ThrowIfCancellationRequested();
			ExecutionBudget.Current?.ThrowIfExceeded();
		}
		lock (_admissionLock) _pendingEntries[pid] = entry with
		{ Deferred = new(due, Schedule, null, command, state) };
		try { await Schedule(due, 0); return admission; }
		catch
		{
			using var cleanup = ExecutionBudget.FromMilliseconds(1000);
			using var cleanupScope = cleanup.Enter();
			try { await _scheduler.UnscheduleJob(trigger, cleanup.Token); }
			catch (Exception cleanupFailure)
			{
				// Uncertain publication retains quota until halt confirms trigger cleanup.
				lock (_admissionLock)
				{
					_delayedRepairs.Add(pid);
					_pendingEntries[pid] = _pendingEntries[pid] with { DeferredReleaseOutcome = QueueOutcome.ScheduleFailed };
				}
				logger.LogError(cleanupFailure, "Delayed schedule cleanup failed for PID {Pid}; retry halt to release its reservation", pid);
				throw;
			}
			Release(pid, QueueOutcome.ScheduleFailed);
			throw;
		}
	}

	public IAsyncEnumerable<(string Group, (DateTimeOffset, OneOf<string, DBRef>)[])> GetAllTasks()
		=> ReadAllTasks(ExecutionBudget.CurrentToken);

	private async IAsyncEnumerable<(string Group, (DateTimeOffset, OneOf<string, DBRef>)[])> ReadAllTasks(
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ExecutionBudget.CurrentToken);
		var token = cancellation.Token;
		token.ThrowIfCancellationRequested();
		var keys = await _scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.AnyGroup(), token);
		var keyTriggers = keys.ToAsyncEnumerable()
			.Select<TriggerKey, ITrigger?>(async (triggerKey, ct) => await _scheduler.GetTrigger(triggerKey, ct))
			.Where(trigger => trigger?.FinalFireTimeUtc is not null).Select(trigger => trigger!)
			.GroupBy(trigger => trigger.Key.Group, trigger => (trigger.FinalFireTimeUtc!.Value, trigger.Key.Name));
		await foreach (var key in keyTriggers.WithCancellation(token))
		{
			yield return (key.Key, key.Select(x => (x.Value, DescribeTrigger(x.Name))).ToArray());
		}

		foreach (var group in _pendingEntries.Values.Where(e => e.Group is DirectInputGroup or EnqueueGroup).GroupBy(e => e.Group))
		{
			token.ThrowIfCancellationRequested();
			yield return (group.Key, group.Select(e => (
				DateTimeOffset.UtcNow,
				DescribeTrigger(e.TriggerName)
			)).ToArray());
		}
	}

	/// <summary>
	/// The executor a trigger name encodes (<c>dbref:#5:1744849081000-16</c> → <c>#5:1744849081000</c>),
	/// or the raw name when it does not carry one.
	/// </summary>
	private static OneOf<string, DBRef> DescribeTrigger(string triggerName)
	{
		var identity = triggerName.AsSpan();
		if (identity.StartsWith("dbref:"))
		{
			identity = identity["dbref:".Length..];
		}
		else if (identity.StartsWith("handle:"))
		{
			identity = identity["handle:".Length..];
		}

		Span<System.Range> parts = stackalloc System.Range[2];
		identity.Split(parts, '-');

		return DBRef.TryParse(identity[parts[0]].ToString(), out var dbref)
			? OneOf<string, DBRef>.FromT1(dbref!.Value)
			: OneOf<string, DBRef>.FromT0(triggerName);
	}

	/// <summary>
	/// The PID from a trigger name shaped <c>prefix-pid</c>: exactly one dash, followed by the number.
	/// </summary>
	private static bool TryParsePid(string triggerName, out long pid)
	{
		var name = triggerName.AsSpan();
		Span<System.Range> parts = stackalloc System.Range[3];
		if (name.Split(parts, '-') != 2)
		{
			pid = 0;
			return false;
		}

		return long.TryParse(name[parts[1]], out pid);
	}

	public IAsyncEnumerable<SemaphoreTaskData> GetSemaphoreTasks(DBRef obj)
		=> SemaphoreSnapshots(e => e.SemaphoreTarget?.Matches(obj) == true).ToAsyncEnumerable();

	public IAsyncEnumerable<SemaphoreTaskData> GetSemaphoreTasks(long pid)
		=> SemaphoreSnapshots(e => e.Pid == pid).ToAsyncEnumerable();

	public IAsyncEnumerable<SemaphoreTaskData> GetSemaphoreTasks(DbRefAttribute objAttribute)
		=> SemaphoreSnapshots(e => e.Group == $"{SemaphoreGroup}:{objAttribute}").ToAsyncEnumerable();

	private SemaphoreTaskData[] SemaphoreSnapshots(Func<QueueEntry, bool> predicate)
	{
		lock (_admissionLock)
		{
			var now = DateTimeOffset.UtcNow;
			return _pendingEntries.Values.Where(e => e.Deferred?.Semaphore is not null && !_ready.Contains(e.Pid) && predicate(e))
				.OrderBy(e => e.Pid).Select(e => new SemaphoreTaskData(e.Pid, e.Deferred!.Command,
					e.Executor ?? new DBRef(-1), new DbRefAttribute(e.SemaphoreTarget!.Value, e.Deferred.Semaphore!.Value.Attribute),
					e.Deferred.Paused ? e.Deferred.Remaining : Nonnegative(e.Deferred.Due - now))).ToArray();
		}
	}

	public IAsyncEnumerable<long> GetDelayTasks(DBRef obj)
		=> ReadDelayTasks(obj, ExecutionBudget.CurrentToken);

	private async IAsyncEnumerable<long> ReadDelayTasks(DBRef obj, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ExecutionBudget.CurrentToken);
		var token = cancellation.Token;
		token.ThrowIfCancellationRequested();
		var keys = await _scheduler.GetTriggerKeys(
			GroupMatcher<TriggerKey>.GroupEquals($"{DelayGroup}:{obj}"), token);

		// Extract PID from identity: "dbref:{executor}-{pid}"
		foreach (var key in keys)
		{
			token.ThrowIfCancellationRequested();
			// Extract PID from identity: "dbref:{executor}-{pid}"
			if (TryParsePid(key.Name, out var pid))
			{
				yield return pid;
			}
		}
	}

	public IAsyncEnumerable<long> GetEnqueueTasks(DBRef obj)
	{
		var dbRefPrefix = $"dbref:{obj}-";
		return _pendingEntries
			.Where(kvp => kvp.Value.TriggerName.StartsWith(dbRefPrefix) && kvp.Value.Group == EnqueueGroup)
			.Select(kvp => kvp.Key)
			.ToAsyncEnumerable();
	}


	public async ValueTask RescheduleSemaphoreTask(long pid, TimeSpan delay)
	{
		using var lease = await LockDeferred();
		QueueEntry entry;
		lock (_admissionLock)
		{
			if (!_pendingEntries.TryGetValue(pid, out entry!) || _ready.Contains(pid)
				|| entry.Deferred is null || entry.Deferred.ReleasePending || HasPendingCleanup(pid)) return;
			delay = Nonnegative(delay);
			if (entry.Deferred.Paused)
			{
				_pendingEntries[pid] = entry with { Deferred = entry.Deferred with { Remaining = delay } };
				return;
			}
		}
		try { await ReplaceDeferredSchedule(entry, delay); }
		catch
		{
			lock (_admissionLock)
				if (_pendingEntries.TryGetValue(pid, out entry!)) _pendingEntries[pid] = entry with
				{ Deferred = entry.Deferred! with { Paused = true, Remaining = delay, Reason = "Schedule update failed" } };
			throw;
		}
	}

	public async ValueTask DisposeAsync()
	{
		QueueEntry[] entries;
		lock (_admissionLock) { _stopping = true; _immediateQueue.Writer.TryComplete(); entries = _pendingEntries.Values.ToArray(); }
		foreach (var entry in entries) CancelEntry(entry);
		await _shutdownCts.CancelAsync();
		if (_consumerTask is not null)
		{
			try
			{
				await _consumerTask;
			}
			catch (OperationCanceledException)
			{
				// Expected during shutdown
			}
		}
		// Publication observes the cancelled entry token and settles its provider write
		// before shutdown disposes the reservation. Release callbacks run after this gate.
		await _semaphoreMutations.WaitAsync();
		_semaphoreMutations.Release();
		await _deferredChanges.WaitAsync();
		_deferredChanges.Release();
		lock (_admissionLock)
		{
			if (_delayedRepairs.Count != 0)
				logger.LogError("Shutdown with {Count} unrepaired delayed triggers", _delayedRepairs.Count);
			_delayedRepairs.Clear();
		}
		foreach (var pid in _semaphoreRepairs.Keys)
		{
			logger.LogError("Shutdown with unrepaired semaphore admission PID {Pid}; the in-memory repair cannot survive restart", pid);
			_semaphoreRepairs.TryRemove(pid, out _);
		}
		if (_semaphoreCommandRepair is not null)
			logger.LogError("Shutdown with uncertain semaphore command accounting; manual counter reconciliation is required before restarting queued work");
		_semaphoreCommandRepair = null;
		lock (_admissionLock) _semaphoreCommandReservations.Clear();
		foreach (var pid in _pendingEntries.Keys) Release(pid);
		_shutdownCts.Dispose();
		GC.SuppressFinalize(this);
	}
}
