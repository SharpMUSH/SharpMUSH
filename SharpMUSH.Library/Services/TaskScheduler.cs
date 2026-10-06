using Mediator;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.DiscriminatedUnions;
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
	IQueueDiagnosticsRecorder? diagnostics = null)
	: ITaskScheduler, ISemaphoreQueue, ITaskQueueControl, ITaskQueueReader, IAsyncDisposable
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

		/// <summary>
		/// The entry's enactor, who hears "CPU usage exceeded." when it runs out of time
		/// (<c>src/parse.c:2083-2084</c>). Its executor when the work has no other enactor.
		/// </summary>
		public DBRef? Enactor { get; init; }

		/// <summary>
		/// Whether this entry is part of the queue quota. False for a typed line and
		/// for host socket work, which PennMUSH's <c>add_to</c> tally never sees.
		/// </summary>
		public bool ChargesOwner { get; init; } = true;

		/// <summary>
		/// The object whose own <c>player_queue_limit</c> count this entry is in when <c>owner_queues</c>
		/// is off: its executor, or <see cref="Owner"/> for an entry with no executor. With
		/// <c>owner_queues</c> on, <see cref="Owner"/>'s count is the one admission reads (PennMUSH
		/// <c>pay_queue</c>, <c>src/cque.c:303</c>). Both counts are kept for every entry, so turning the
		/// option over leaves no pending entry uncounted.
		/// </summary>
		public string ChargedObject { get; init; } = Owner;
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
		 LockedPendingEntries.GroupBy(e => e.Owner).ToDictionary(g => g.Key, g => g.Count()),
		 new Dictionary<QueueRejectionReason, long>(_rejections));
	}
	public bool HasPendingWork(string triggerName, string group)
	{
		lock (_admissionLock) return LockedPendingEntries.Any(entry => entry.Group == group && entry.TriggerName == SchedulerKeys.TriggerName(triggerName, entry.Pid));
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
		Uncount(entry);
		return entry;
	}

	/// <summary>
	/// The one write path into <see cref="_pendingEntries"/>, so the admission tallies below always
	/// describe exactly what it holds. Caller holds <see cref="_admissionLock"/>.
	/// </summary>
	private void StoreEntry(QueueEntry entry)
	{
		if (_pendingEntries.TryGetValue(entry.Pid, out var previous)) Uncount(previous);
		_pendingEntries[entry.Pid] = entry;
		Count(entry);
	}

	/// <summary>
	/// A typed line's socket incarnation, for the per-connection burst: the handle, the connection's
	/// <c>Metadata</c> instance by reference, and the transport session.
	/// </summary>
	/// <remarks>
	/// Not <c>ReferenceEquals</c> on the <see cref="IConnectionService.ConnectionData"/> itself:
	/// <c>Bind</c>, <c>Unbind</c> and <c>BindAccount</c> replace it with a <c>with</c> copy on the same
	/// handle, and logging in mid-burst is not a new socket. A <c>with</c> copy carries the same
	/// <c>Metadata</c> instance, and only <c>Register</c> (or startup reconciliation) builds a new one,
	/// so that dictionary is the identity that survives a state change and is replaced by a new socket.
	/// The transport session id separates two registrations that the state store can tell apart.
	/// </remarks>
	private readonly record struct IncarnationKey(long Handle, object? Metadata, string Transport)
	{
		public IncarnationKey(PendingInputCommand input)
			: this(input.Handle, input.Connection?.Metadata, input.Transport ?? "") { }

		public bool Equals(IncarnationKey other)
			=> Handle == other.Handle && ReferenceEquals(Metadata, other.Metadata) && Transport == other.Transport;

		public override int GetHashCode()
			=> HashCode.Combine(Handle, RuntimeHelpers.GetHashCode(Metadata), Transport);
	}

	// Pending charged entries per object and per owner (owner_queues picks which one admission reads),
	// and pending typed lines per connection incarnation.
	// Kept with _pendingEntries under _admissionLock so admission never enumerates it (#1336).
	private readonly Dictionary<string, int> _chargedPerObject = new();
	private readonly Dictionary<string, int> _chargedPerOwner = new();
	private readonly Dictionary<IncarnationKey, int> _typedPerIncarnation = new();

	private void Count(QueueEntry entry)
	{
		if (entry.ChargesOwner)
		{
			_chargedPerObject[entry.ChargedObject] = _chargedPerObject.GetValueOrDefault(entry.ChargedObject) + 1;
			_chargedPerOwner[entry.Owner] = _chargedPerOwner.GetValueOrDefault(entry.Owner) + 1;
		}
		if (entry.PendingInput is { } input)
		{
			var key = new IncarnationKey(input);
			_typedPerIncarnation[key] = _typedPerIncarnation.GetValueOrDefault(key) + 1;
		}
	}

	private void Uncount(QueueEntry entry)
	{
		if (entry.ChargesOwner)
		{
			Decrement(_chargedPerObject, entry.ChargedObject);
			Decrement(_chargedPerOwner, entry.Owner);
		}
		if (entry.PendingInput is { } input) Decrement(_typedPerIncarnation, new IncarnationKey(input));
	}

	private static void Decrement<TKey>(Dictionary<TKey, int> counts, TKey key) where TKey : notnull
	{
		var remaining = counts.GetValueOrDefault(key) - 1;
		if (remaining > 0) counts[key] = remaining;
		else counts.Remove(key);
	}

	/// <summary>
	/// For tests: the admission tallies as kept, and the same tallies counted from the ledger the way
	/// admission used to. Both are empty strings when nothing is pending.
	/// </summary>
	internal (string Tallied, string Recounted) AdmissionTalliesAgainstLedger()
	{
		lock (_admissionLock)
		{
			var charged = LockedPendingEntries.Where(e => e.ChargesOwner).ToList();
			var objects = charged.GroupBy(e => e.ChargedObject).ToDictionary(g => g.Key, g => g.Count());
			var owners = charged.GroupBy(e => e.Owner).ToDictionary(g => g.Key, g => g.Count());
			var typed = LockedPendingEntries.Where(e => e.PendingInput is not null)
				.GroupBy(e => new IncarnationKey(e.PendingInput!)).ToDictionary(g => g.Key, g => g.Count());
			return (DescribeTallies(_chargedPerObject, _chargedPerOwner, _typedPerIncarnation),
				DescribeTallies(objects, owners, typed));
		}
	}

	private static string DescribeTallies(IReadOnlyDictionary<string, int> objects, IReadOnlyDictionary<string, int> owners,
		IReadOnlyDictionary<IncarnationKey, int> typed)
		=> string.Join(';', objects.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"object {pair.Key}={pair.Value}")
			.Concat(owners.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"owner {pair.Key}={pair.Value}"))
			.Concat(typed.OrderBy(pair => pair.Key.Handle).ThenBy(pair => pair.Key.Transport, StringComparer.Ordinal)
				.ThenBy(pair => pair.Value)
				.Select(pair => $"#{pair.Key.Handle}/{pair.Key.Transport}={pair.Value}")));

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
	 Action? onReleased = null, string? sourceAttribute = null, bool managesSemaphoreCount = false, bool notifyOnRejection = true, PendingInputCommand? pendingInput = null,
	 bool chargesOwner = true, DBRef? enactor = null)
	{
		// A typed line is invisible to the queue quota, as it is in PennMUSH: run_user_input builds its
		// entry with QUEUE_SOCKET and hands it straight to do_entry (src/cque.c:1076-1088), so it never
		// reaches insert_que, never reaches pay_queue, and never touches the add_to tally queue_limit
		// reads (:226-235). It can therefore neither be refused by the quota nor be the reason someone
		// else is. What bounds it instead is per-connection, as Penn's descriptor quota is
		// (COMMAND_BURST_SIZE, src/bsd.c:197,1000-1004). #1320.
		chargesOwner &= group != DirectInputGroup;
		// Actorless host callbacks share a bounded system bucket; they do not bypass fairness. A
		// connection still names its own bucket even when it is not charged, so an execution-limit
		// notice reaches the handle that typed the line.
		string owner = handle is not null ? SchedulerKeys.Owner(handle)
			: chargesOwner ? SchedulerKeys.SystemOwner
			: SchedulerKeys.SocketOwner;
		string? chargedObject = null;
		var pooled = false;
		long ownerLimit = configuration?.CurrentValue.Limit.PlayerQueueLimit ?? 100;
		var executorIsPlayer = false;
		if (executor is not null)
		{
			if (await mediator.Send(new GetObjectNodeQuery(executor.Value), ExecutionBudget.CurrentToken) is not AnySharpObject target)
			{
				diagnostics?.Rejected(executor, null, SchedulerKeys.KindOf(group), QueueOutcome.InvalidTarget);
				return Reject(QueueRejectionReason.InvalidTarget);
			}
			executor = target.Object().DBRef;
			executorIsPlayer = target.IsPlayer;
			// insert_que drops a halted non-player before it allocates a PID (src/cque.c:530), as
			// new_queue_actionlist_int does before that (:621), so an already-halted object's work never
			// reaches the queue at all: a live PennMUSH shows nothing in @ps and charges nothing. Silent,
			// like Penn's — the owner hears about a halted object only when a command of its own is
			// refused. Players are exempt; only the socket path can carry a non-player here, and it
			// never does, because a non-player has no connection.
			if (!executorIsPlayer && await target.HasFlag("HALT", ExecutionBudget.CurrentToken))
			{
				diagnostics?.Rejected(executor, (await target.Object().Owner.WithCancellation(ExecutionBudget.CurrentToken)).Object.DBRef,
					SchedulerKeys.KindOf(group), QueueOutcome.Halted);
				return Reject(QueueRejectionReason.Halted);
			}
			// pay_queue charges queue_limit(QUEUE_PER_OWNER ? Owner(player) : player) (src/cque.c:303):
			// each object has a count of its own unless owner_queues pools them on the owner, and
			// HugeQueue asks about whichever of the two is charged (:231).
			pooled = configuration?.CurrentValue.Command.OwnerQueues == true;
			var targetOwner = await target.Object().Owner.WithCancellation(ExecutionBudget.CurrentToken);
			var charged = pooled ? new AnySharpObject(targetOwner) : target;
			if (await charged.IsWizard(ExecutionBudget.CurrentToken) || await charged.HasPower("Queue", ExecutionBudget.CurrentToken))
				ownerLimit += Math.Max(0, await mediator.Send(new GetObjectCountQuery(), ExecutionBudget.CurrentToken));
			owner = targetOwner.Object.DBRef.ToString();
			chargedObject = target.Object().DBRef.ToString();
		}
		chargedObject ??= owner;
		QueueAdmissionResult result;
		lock (_admissionLock)
		{
			if (_stopping) result = Reject(QueueRejectionReason.ShuttingDown);
			else if (_pendingEntries.Count >= (configuration?.CurrentValue.Limit.GlobalQueueLimit ?? 10000)) result = Reject(QueueRejectionReason.GlobalLimit);
			// Only charged entries are in the tally, so a typed line cannot make its owner a runaway.
			else if (chargesOwner && (pooled ? _chargedPerOwner.GetValueOrDefault(owner) : _chargedPerObject.GetValueOrDefault(chargedObject)) >= ownerLimit)
				result = Reject(QueueRejectionReason.OwnerLimit);
			// Per connection incarnation, not per numeric handle: a replaced socket reuses the handle,
			// and work the previous occupant left behind (which the entry's own session check will
			// discard when it reaches the consumer) must not spend the new one's allowance.
			else if (!chargesOwner && pendingInput is { } typed
				&& _typedPerIncarnation.GetValueOrDefault(new IncarnationKey(typed))
					>= (configuration?.CurrentValue.Limit.CommandBurstSize ?? LimitOptions.DefaultCommandBurstSize))
				result = Reject(QueueRejectionReason.ConnectionLimit);
			else
			{
				var pid = NextPid();
				DBRef.TryParse(owner, out var diagnosticOwner);
				var entry = new QueueEntry(pid, SchedulerKeys.TriggerName(identity, pid), group, action, new CancellationTokenSource(), owner, executor, SemaphoreTarget: semaphoreTarget, OnReleased: onReleased, ManagesSemaphoreCount: managesSemaphoreCount)
				{
					Observation = diagnostics?.Admitted(pid, executor, diagnosticOwner, SchedulerKeys.KindOf(group), sourceAttribute),
					PendingInput = pendingInput,
					Enactor = enactor ?? executor,
					ChargesOwner = chargesOwner,
					ChargedObject = chargedObject
				};
				StoreEntry(entry);
				_orderedPids.Add(pid);
				if (ready) { _ready.Add(pid); _immediateQueue.Writer.TryWrite(entry); }
				result = new(pid, QueueRejectionReason.None);
			}
		}
		if (result.Accepted && ready) EnsureConsumerStarted();
		if (!result.Accepted)
		{
			DBRef.TryParse(owner, out var diagnosticOwner);
			diagnostics?.Rejected(executor, diagnosticOwner, SchedulerKeys.KindOf(group), result.Reason switch
			{
				QueueRejectionReason.GlobalLimit => QueueOutcome.GlobalLimit,
				QueueRejectionReason.OwnerLimit => QueueOutcome.OwnerLimit,
				QueueRejectionReason.ConnectionLimit => QueueOutcome.ConnectionLimit,
				QueueRejectionReason.ShuttingDown => QueueOutcome.ShuttingDown,
				_ => QueueOutcome.InvalidTarget
			});
		}
		// pay_queue wipes and halts whatever executor tripped the quota, player or not
		// (do_halt(Owner(player), "", player) then set_flag_internal(player, "HALT"),
		// src/cque.c:303-313). What decides it is the entry, not the type: run_user_input builds its
		// entry with QUEUE_SOCKET and hands it straight to do_entry (src/cque.c:1076-1088), so a typed
		// line never reaches insert_que and so never reaches pay_queue, whoever typed it. That is also
		// what makes the flag safe on a player — they can still type, and process_command
		// (src/game.c:1181) refuses only what the queue carries for them. An uncharged entry can no
		// longer be refused for the owner limit at all, so no group test is needed here.
		//
		// Scheduled before any notice, which is best-effort and can fail: telling nobody about a
		// runaway is survivable, leaving one running is not.
		if (result.Reason == QueueRejectionReason.OwnerLimit && executor is { } offender)
			QueueRunawayHalt(offender, owner);
		// An object refused for its quota hears about it from the runaway path alone, as pay_queue
		// prints only "Runaway object" (src/cque.c:304); what else it queues before the HALT lands is
		// dropped silently, as insert_que drops a halted object's work (:530).
		if (!result.Accepted && notifyOnRejection && notifyService is not null
			&& !(result.Reason == QueueRejectionReason.OwnerLimit && executor is not null))
		{
			if (handle is not null) await notifyService.NotifyLocalized(handle.Value, "QueueRejected", result.Reason);
			else if (DBRef.TryParse(owner, out var player))
				await foreach (var connection in connectionService.Get(player!.Value))
					await notifyService.NotifyLocalized(connection.Handle, "QueueRejected", result.Reason);
		}
		return result;
	}

	// One outstanding halt per offender. A quota that is full stays full until the wipe runs, so
	// every admission behind this one is refused too and would otherwise queue its own duplicate.
	// The value completes once the halt has finished; the consumer waits on it (AwaitRunawayHalts).
	private readonly ConcurrentDictionary<DBRef, Task> _runawayHalts = new();

	/// <summary>
	/// PennMUSH's runaway path (<c>src/cque.c:303-313</c>): <c>pay_queue</c> does not merely refuse
	/// the entry, it tells the owner, wipes the offender's queue and sets <c>HALT</c> on it. The
	/// refused entry is simply not queued.
	/// </summary>
	/// <remarks>
	/// Deviation: the wipe runs detached rather than inline. <see cref="Halt"/> takes the semaphore
	/// and delayed transition leases, and an admission that came from <c>@wait &lt;obj&gt;/&lt;attr&gt;</c>
	/// is already holding the semaphore lease when it is refused, so halting inline would wait on a
	/// lease this call stack owns. It is not admitted as a queue entry either: the refusal it answers
	/// means the queue is at a limit, and taking a slot to run the wipe would deny one to an
	/// unrelated owner. What Penn's inline call guarantees is kept anyway: the consumer starts no
	/// further entry until the halt has finished (<see cref="AwaitRunawayHalts"/>), so work queued
	/// after the refusal, such as a <c>think hasflag(obj,HALT)</c>, sees the wipe and the flag.
	/// <see cref="DrainImmediateQueueForTests"/> waits on the outstanding set.
	/// </remarks>
	private void QueueRunawayHalt(DBRef offender, string owner)
	{
		var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		if (!_runawayHalts.TryAdd(offender, done.Task)) return;
		_ = Task.Run(async () =>
		{
			try
			{
				// The refused admission's budget is disposed the moment it returns, and the ambient one
				// flows into this task. The wipe gets a lifetime of its own, bounded by shutdown and by
				// nothing else: pay_queue's do_halt and set_flag_internal sit outside start_cpu_timer,
				// which bounds an entry's evaluation rather than the queue's bookkeeping
				// (src/cque.c:303-313 against :1141). A per-entry deadline here would abandon a long wipe
				// part-done and leave the offender unhalted with its backlog intact — the one outcome
				// this path exists to prevent, and one nothing retries.
				using var budget = ExecutionBudget.FromMilliseconds(0, _shutdownCts.Token);
				using var scope = budget.Enter();
				await HaltRunaway(offender, owner);
			}
			catch (Exception ex) { logger.LogError(ex, "Could not halt runaway object {DbRef}", offender); }
			finally
			{
				_runawayHalts.TryRemove(offender, out _);
				done.TrySetResult();
			}
		});
	}

	/// <summary>
	/// Waits for every runaway halt in flight. The consumer calls it before each entry, when no entry
	/// of its own holds a lease the wipe needs. A halt never waits on the consumer and completes even
	/// when it fails, so this cannot deadlock; shutdown cancels the wipe's budget.
	/// </summary>
	private async ValueTask AwaitRunawayHalts()
	{
		while (!_runawayHalts.IsEmpty) await Task.WhenAll(_runawayHalts.Values);
	}

	private async ValueTask HaltRunaway(DBRef offender, string owner)
	{
		var node = await mediator.Send(new GetObjectNodeQuery(offender), ExecutionBudget.CurrentToken);
		var name = node is AnySharpObject found ? found.Object().Name : offender.ToString();

		// The wipe has to happen: without it the backlog the object already built keeps running, each
		// entry freeing a slot the next one takes, and the quota alone never brings the loop to a stop.
		await Halt(offender);

		// set_flag_internal(player, "HALT") (src/cque.c:312) names no type and excludes none. A halted
		// player is not a silenced player: the queue exempts them (insert_que, src/cque.c:530; the
		// dequeue re-check, :1136), and what reaches process_command is refused there (src/game.c:1181)
		// unless the player typed it. So the flag stops the runaway's queued work and leaves the
		// person at the keyboard able to type — including `@set me=!halt`.
		if (node is AnySharpObject haltable)
		{
			var haltFlag = await mediator.Send(new GetObjectFlagQuery("HALT"), ExecutionBudget.CurrentToken);
			if (haltFlag is not null) await mediator.Send(new SetObjectFlagCommand(haltable, haltFlag), ExecutionBudget.CurrentToken);
		}

		// pay_queue tells the owner, then do_halt does unless the owner is QUIET (src/cque.c:304,
		// :2176-2178). Both name the object by its plain dbref.
		if (notifyService is not null && DBRef.TryParse(owner, out var parsedOwner) && parsedOwner is { } ownerRef)
		{
			var dbref = $"#{offender.Number}";
			await notifyService.NotifyLocalized(ownerRef,
				nameof(ErrorMessages.Notifications.RunawayObjectFormat), name, dbref);
			if (await mediator.Send(new GetObjectNodeQuery(ownerRef), ExecutionBudget.CurrentToken) is AnySharpObject ownerObject
				&& !await ownerObject.HasFlag("QUIET", ExecutionBudget.CurrentToken))
				await notifyService.NotifyLocalized(ownerRef,
					nameof(ErrorMessages.Notifications.HaltedNoticeFormat), name, dbref);
		}

		logger.LogWarning("Runaway object {Name} ({DbRef}) exceeded its queue quota; commands halted",
			name, offender);
	}

	/// <summary>
	/// Waits until the immediate-execution queue has no entries left to run, so a test can assert on
	/// the effects of work that was queued rather than run inline — an action attribute triggered by
	/// <see cref="IDidItService"/>, for one.
	/// </summary>
	/// <remarks>
	/// Only entries the readiness gate has published are waited on. A <c>@wait</c> parked on a
	/// semaphore or a timer also sits in <see cref="_pendingEntries"/>, and waiting for that to empty
	/// would be waiting for the semaphore to be notified.
	/// </remarks>
	public async ValueTask DrainImmediateQueueForTests(TimeSpan? timeout = null)
	{
		EnsureConsumerStarted();

		// An entry leaves _ready only after its action has finished, and anything that action queued
		// is published before that removal, so an empty set means the queue is quiet — including the
		// work the drained entries themselves produced.
		var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));

		while (true)
		{
			int outstanding;
			lock (_admissionLock) outstanding = _ready.Count;
			if (outstanding == 0 && _runawayHalts.IsEmpty) return;

			if (DateTimeOffset.UtcNow >= deadline)
			{
				throw new TimeoutException(
					$"The immediate queue still held {outstanding} entries after the drain timeout.");
			}

			await Task.Delay(TimeSpan.FromMilliseconds(5));
		}
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
					if (await mediator.Send(new GetObjectNodeQuery(reference), ExecutionBudget.CurrentToken) is AnySharpObject source)
					{
						executor = source.Object().DBRef;
						owner = (await source.Object().Owner.WithCancellation(ExecutionBudget.CurrentToken)).Object.DBRef;
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
		var semaphore = SchedulerKeys.SemaphoreTarget(entry.Group);
		if (entry.SemaphoreTarget is { } target) semaphore = new(target, semaphore.Attribute);
		async ValueTask<SharpAttribute?> Read() => await mediator.CreateStream(
			new GetAttributeQuery(semaphore.DbRef, semaphore.Attribute), ExecutionBudget.CurrentToken)
			.LastOrDefaultAsync(ExecutionBudget.CurrentToken);
		var attribute = await Read();
		if (attribute is null || !int.TryParse(attribute.Value.ToPlainText(), out var original)) return;
		var expected = original > 0 ? original - 1 : 0;
		if (await mediator.Send(new GetObjectNodeQuery(new DBRef(1)), ExecutionBudget.CurrentToken) is not (AnySharpObject and SharpPlayer god)) return;
		async ValueTask Write()
		{
			if (!await mediator.Send(new SetAttributeCommand(semaphore.DbRef, semaphore.Attribute,
				MarkupString.MarkupText.Plain(expected.ToString()), god), ExecutionBudget.CurrentToken))
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
						StoreEntry(pending with { Deferred = null, HaltAccountingSettled = true });
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
		var semaphore = SchedulerKeys.SemaphoreTarget(entry.Group);
		if (entry.SemaphoreTarget is { } target) semaphore = new(target, semaphore.Attribute);
		async ValueTask<SharpAttribute?> Read() => await mediator.CreateStream(
			new GetAttributeQuery(semaphore.DbRef, semaphore.Attribute), ExecutionBudget.CurrentToken)
			.LastOrDefaultAsync(ExecutionBudget.CurrentToken);
		var attribute = await Read();
		var original = 0;
		var accounted = attribute is null || !int.TryParse(attribute.Value.ToPlainText(), out original);
		var expected = original > 0 ? original - 1 : 0;
		SharpPlayer? god = null;
		if (!accounted && await mediator.Send(new GetObjectNodeQuery(new DBRef(1)), ExecutionBudget.CurrentToken) is AnySharpObject and SharpPlayer player)
			god = player;
		accounted = accounted || god is null;
		JobKey? transportJob = null;
		async ValueTask<QueueAdmissionResult> Complete(bool retry)
		{
			if (!accounted && god is not null)
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
					MarkupString.MarkupText.Plain(expected.ToString()), god), ExecutionBudget.CurrentToken))
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
				StoreEntry(entry with { Deferred = entry.Deferred with { ReleasePending = true } });
				return ValueTask.FromResult(new QueueAdmissionResult(pid, QueueRejectionReason.None));
			}
			if (!_ready.Add(pid)) return ValueTask.FromResult(new QueueAdmissionResult(null, QueueRejectionReason.AlreadyReleased));
			entry = entry with { Group = EnqueueGroup };
			StoreEntry(entry);
			_immediateQueue.Writer.TryWrite(entry);
		}
		EnsureConsumerStarted();
		return ValueTask.FromResult(new QueueAdmissionResult(pid, QueueRejectionReason.None));
	}
	private async ValueTask<CallState?> ExecuteList(MString command, ParserState state)
	{
		if (state.Executor is not null)
		{
			var executor = await mediator.Send(new GetObjectNodeQuery(state.Executor.Value), ExecutionBudget.CurrentToken);
			if (executor is None) return null;
			// run_queue_entry re-checks HALT when the entry starts (src/cque.c:1136): an object halted after
			// its work was queued — by the rest of the list that queued it, say — runs none of it. Players are exempt.
			if (executor is AnySharpObject { IsPlayer: false } thing && await thing.HasFlag("HALT", ExecutionBudget.CurrentToken)) return null;
		}
		// Deferred bodies cannot consume the submitting command list's break/include state.
		return await parser.FromState(state with { ExecutionStack = [], BreakPropagation = null, CommandModifierDepth = 0, InplaceDepth = 0 }).CommandListParse(command);
	}
	private readonly ConcurrentDictionary<long, QueueEntry> _pendingEntries = new();

	/// <summary>
	/// The pending entries, enumerated in place. Only for a caller holding <see cref="_admissionLock"/>,
	/// under which every write to <see cref="_pendingEntries"/> happens, so this sees what
	/// <c>Values</c> would without the copy <c>Values</c> takes of the whole table.
	/// </summary>
	private IEnumerable<QueueEntry> LockedPendingEntries => _pendingEntries.Select(pair => pair.Value);
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
				await AwaitRunawayHalts();
				try
				{
					lock (_admissionLock) _running.Add(entry.Pid);
					var milliseconds = RunsSoftcode(entry)
						? configuration?.CurrentValue.Limit.QueueEntryCpuTime ?? LimitOptions.DefaultQueueEntryCpuTime
						: 0;
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

	/// <summary>
	/// Whether <c>queue_entry_cpu_time</c> times <paramref name="entry"/>. A line typed at the login
	/// screen does not: what runs there is the server's own login work (password check, MOTD, LAST
	/// bookkeeping, announcements), which PennMUSH does in microseconds of CPU and which a wall-clock
	/// deadline would charge with every disk write, bus publish and cold JIT along the way. The one
	/// evaluation a login does, its look, takes a limit of its own (<c>SocketCommands.LookAfterLoginAsync</c>).
	/// Decided when the entry starts, so a line queued at the login screen that runs after the login
	/// is timed as the player's.
	/// </summary>
	private bool RunsSoftcode(QueueEntry entry)
		=> entry.PendingInput is not { } input || connectionService.Get(input.Handle)?.Ref is not null;

	/// <summary>
	/// PennMUSH's notice for a queue entry that ran out of <c>queue_entry_cpu_time</c>: once per entry,
	/// <c>if (GoodObject(enactor) &amp;&amp; !Quiet(enactor)) notify(enactor, T("CPU usage exceeded."))</c>
	/// (<c>src/parse.c:2077-2084</c>). The enactor hears it, not the owner, and only the enactor's own
	/// QUIET flag silences it. A line typed before login has no enactor object; its connection hears it.
	/// </summary>
	private async ValueTask NotifyExpired(QueueEntry entry)
	{
		logger.LogWarning("Execution budget exhausted (PID {Pid})", entry.Pid);
		if (notifyService is null || entry.Cts.IsCancellationRequested || _shutdownCts.IsCancellationRequested) return;
		// Reporting has its own bounded I/O lifetime after the user execution deadline.
		using var reportBudget = ExecutionBudget.FromMilliseconds(1000, _shutdownCts.Token);
		using var reportScope = reportBudget.Enter();
		try
		{
			if (entry.Enactor is { } enactorRef)
			{
				if (await mediator.Send(new GetObjectNodeQuery(enactorRef), ExecutionBudget.CurrentToken) is AnySharpObject enactor
					&& !await enactor.HasFlag("QUIET", ExecutionBudget.CurrentToken))
					await notifyService.NotifyLocalized(enactor, nameof(ErrorMessages.Notifications.CpuUsageExceeded));
			}
			else if (SchedulerKeys.TryHandleOwner(entry.Owner, out var handle))
				await notifyService.NotifyLocalized(handle, nameof(ErrorMessages.Notifications.CpuUsageExceeded));
		}
		catch (Exception ex) { logger.LogWarning(ex, "Could not report execution limit for PID {Pid}", entry.Pid); }
	}

	public ValueTask<QueueAdmissionResult> AdmitWork(Func<ValueTask<CallState?>> action, string triggerName, string group)
	 => Admit(action, triggerName, group, null);

	public ValueTask<QueueAdmissionResult> AdmitWork(Func<ValueTask<CallState?>> action, string triggerName, string group, DBRef executor, bool notifyOnRejection = true)
	 => Admit(action, triggerName, group, executor, notifyOnRejection: notifyOnRejection);

	public ValueTask<QueueAdmissionResult> AdmitSocketWork(Func<ValueTask<CallState?>> action, string triggerName, string group, Action? onReleased = null)
	 => Admit(action, triggerName, group, null, onReleased: onReleased, notifyOnRejection: false, chargesOwner: false);

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
		var milliseconds = configuration?.CurrentValue.Limit.QueueEntryCpuTime ?? LimitOptions.DefaultQueueEntryCpuTime;
		using var releaseBudget = ExecutionBudget.FromMilliseconds(milliseconds == 0 ? 1000 : milliseconds, releaseCancellation.Token);
		using var releaseScope = releaseBudget.Enter();
		releaseBudget.ThrowIfExceeded();
		// Counter writes precede deferred transitions; failed persistence retains the PID.
		using var mutation = SchedulerKeys.IsSemaphore(entry.Group)
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
		if (semaphoreTimeout && SchedulerKeys.IsSemaphore(entry.Group))
			return await SettleTimeout(entry);
		await RemoveDeferredTrigger(entry);
		return await Activate(pid);
	}


	private readonly IScheduler _scheduler = schedulerFactory.GetScheduler().GetAwaiter().GetResult();
	/// <inheritdoc cref="SchedulerKeys.DirectInputGroup"/>
	public const string DirectInputGroup = SchedulerKeys.DirectInputGroup;

	/// <inheritdoc cref="SchedulerKeys.EnqueueGroup"/>
	public const string EnqueueGroup = SchedulerKeys.EnqueueGroup;

	/// <inheritdoc cref="SchedulerKeys.SemaphoreGroup"/>
	public const string SemaphoreGroup = SchedulerKeys.SemaphoreGroup;

	/// <inheritdoc cref="SchedulerKeys.DelayGroup"/>
	public const string DelayGroup = SchedulerKeys.DelayGroup;

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
		}, SchedulerKeys.Owner(handle), DirectInputGroup, connectionService.Get(handle)?.Ref, handle, pendingInput: pendingCommand);
	}

	public ValueTask<QueueAdmissionResult> WriteInputSessionTimeout(InputSession session)
		=> inputSessions is null
			? ValueTask.FromResult(new QueueAdmissionResult(null, QueueRejectionReason.InvalidTarget))
			: Admit(() => inputSessions.DeliverAsync(parser, session, MString.Empty, timeout: true),
				$"input-session:{session.Id}", EnqueueGroup, session.Executor, onReleased: () => inputSessions.Discard(session), notifyOnRejection: false);

	/// <summary>
	/// Fixes what a queued entry starts with at the moment it is queued: its executor, and the <c>%></c>
	/// the queuing list had then, since that list keeps running and changes it.
	/// </summary>
	private async ValueTask<ParserState> CaptureExecutor(ParserState state)
	{
		state = state.WithQueuedOutput();
		if (state.Executor is not { } executor) return state;
		return await mediator.Send(new GetObjectNodeQuery(executor), ExecutionBudget.CurrentToken) is AnySharpObject target
			? state with { Executor = target.Object().DBRef }
			: state;
	}
	public async ValueTask<QueueAdmissionResult> AdmitCommandList(MString command, ParserState state)
	{
		state = await CaptureExecutor(state);
		return await Admit(() => ExecuteList(command, state), SchedulerKeys.Owner(state.Executor), EnqueueGroup, state.Executor, sourceAttribute: SourceAttribute(state), enactor: state.Enactor);
	}

	public async ValueTask<QueueCommandReservation> ReserveCommandList(MString command, ParserState state)
	{
		state = await CaptureExecutor(state);
		var admission = await Admit(() => ExecuteList(command, state), SchedulerKeys.Owner(state.Executor), EnqueueGroup, state.Executor, ready: false, sourceAttribute: SourceAttribute(state), enactor: state.Enactor);
		if (!admission.Accepted) return QueueCommandReservation.Rejected(admission.Reason);
		var pid = admission.Pid!.Value;
		return new QueueCommandReservation(admission, () => Activate(pid), () => ReleasePending(pid));
	}

	public ValueTask<QueueAdmissionResult> AdmitCommandList(MString command, ParserState state, DbRefAttribute dbRefAttribute, int oldValue, bool manageSemaphoreCount = false)
	 => AdmitCommandList(command, state, dbRefAttribute, oldValue, TimeSpan.FromDays(36500), manageSemaphoreCount);

	public async ValueTask<QueueAdmissionResult> AdmitAsyncAttribute(Func<ValueTask<ParserState>> function, DbRefAttribute dbAttribute, DBRef? executor = null, DBRef? enactor = null)
	{
		if (await mediator.Send(new GetObjectNodeQuery(dbAttribute.DbRef), ExecutionBudget.CurrentToken) is not AnySharpObject target)
			return await RejectInvalidTarget(executor ?? dbAttribute.DbRef, EnqueueGroup);
		dbAttribute = new DbRefAttribute(target.Object().DBRef, dbAttribute.Attribute);
		executor = (await CaptureExecutor(ParserState.Empty with { Executor = executor ?? dbAttribute.DbRef })).Executor;
		return await Admit(async () =>
		{
			if (executor is not null && await mediator.Send(new GetObjectNodeQuery(executor.Value), ExecutionBudget.CurrentToken) is None) return null;
			if (await mediator.Send(new GetObjectNodeQuery(dbAttribute.DbRef), ExecutionBudget.CurrentToken) is not AnySharpObject obj) return null;
			var parserState = await function();
			ExecutionBudget.Current?.ThrowIfExceeded();
			var actor = await parserState.KnownExecutorObject(mediator);
			var attr = await attributeService.GetAttributeAsync(actor, obj, string.Join('`', dbAttribute.Attribute), IAttributeService.AttributeMode.Execute);
			if (attr is not SharpAttribute[] chain) return new CallState("#-1");
			return await ExecuteList(chain.Last().Value, parserState);
		}, $"async:{dbAttribute}", EnqueueGroup, executor, sourceAttribute: dbAttribute.DbRef == executor ? string.Join('`', dbAttribute.Attribute) : null, enactor: enactor);
	}

	public async ValueTask<QueueAdmissionResult> AdmitCommandList(MString command, ParserState state,
	 DbRefAttribute dbRefAttribute, int oldValue, TimeSpan timeout, bool manageSemaphoreCount = false)
	{
		// Direct callers have no queue-entry budget; shutdown must still interrupt
		// every database operation while this transaction owns the semaphore gate.
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
		if (await mediator.Send(new GetObjectNodeQuery(dbRefAttribute.DbRef), ExecutionBudget.CurrentToken) is not AnySharpObject target)
			return await RejectInvalidTarget(state.Executor, SemaphoreGroup);
		var group = SchedulerKeys.Semaphore(dbRefAttribute);
		var admission = await Admit(() => ExecuteList(command, state), SchedulerKeys.Owner(state.Executor), group, state.Executor, ready: false, semaphoreTarget: target.Object().DBRef, sourceAttribute: SourceAttribute(state), managesSemaphoreCount: manageSemaphoreCount, enactor: state.Enactor);
		if (!admission.Accepted) return admission;
		var pid = admission.Pid!.Value;
		lock (_admissionLock) _semaphorePublications.Add(pid);
		var scheduleWriteAttempted = false;
		var triggerKey = new TriggerKey(SchedulerKeys.Trigger(state.Executor, pid), group);
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
		var fullTarget = target.Object().DBRef;
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
				if (await mediator.Send(new GetObjectNodeQuery(new DBRef(1)), ExecutionBudget.CurrentToken) is not (AnySharpObject and SharpPlayer godPlayer))
					throw new InvalidOperationException("God (#1) must exist as a player to write a semaphore count.");
				god = godPlayer;
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
			lock (_admissionLock) StoreEntry(_pendingEntries[pid] with
			{ Deferred = new(due, Schedule, dbRefAttribute, command, state) });
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
		var group = SchedulerKeys.Semaphore(dbAttribute);
		lock (_admissionLock)
			waiting = LockedPendingEntries.Where(e => e.Group == group && !_ready.Contains(e.Pid)
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
		var group = SchedulerKeys.Semaphore(dbAttribute);
		lock (_admissionLock)
			entry = LockedPendingEntries.Where(e => e.Group == group && !_ready.Contains(e.Pid)
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
			var group = SchedulerKeys.Semaphore(dbAttribute);
			lock (_admissionLock)
			{
				removed = LockedPendingEntries.Where(e => e.Group == group && !_ready.Contains(e.Pid)
					&& e.Deferred?.ReleasePending != true && !_semaphoreCommandReservations.Contains(e.Pid) && !_semaphoreRepairs.ContainsKey(e.Pid) && !_semaphorePublications.Contains(e.Pid)).OrderBy(e => e.Pid).Take(Math.Max(0, count ?? int.MaxValue)).ToArray();
			}
			foreach (var entry in removed) await RemoveDeferredTrigger(entry);
			lock (_admissionLock)
				foreach (var entry in removed) RemoveEntry(entry.Pid);
		}
		foreach (var entry in removed) DisposeEntry(entry);
		return removed.Length;
	}

	/// <summary>
	/// <c>do_halt</c>: everything the object has queued, except what it typed. This is the whole of
	/// <c>@halt &lt;object&gt;</c>, the runaway wipe and <c>free_object</c>'s halt alike, because
	/// PennMUSH gives them one implementation.
	/// </summary>
	/// <remarks>
	/// <c>do_halt</c> walks the run, wait and semaphore queues (<c>src/cque.c:2179-2218</c>), and a
	/// typed line is on none of them: <c>run_user_input</c> builds its entry with
	/// <c>QUEUE_SOCKET</c> and hands it straight to <c>do_entry</c> without ever inserting it
	/// (<c>:1076-1090</c>). SharpMUSH does queue typed input, in <see cref="DirectInputGroup"/>, so
	/// sparing that group is what reproduces the same set — and it is what the halted gate promises,
	/// since the <c>HALT</c> flag the runaway wipe sets leaves a player able to type. A line already
	/// admitted and waiting its turn is as typed as the next one.
	/// </remarks>
	public ValueTask Halt(DBRef dbRef) => HaltWhere(dbRef, entry => entry.Group != DirectInputGroup);

	private async ValueTask HaltWhere(DBRef dbRef, Func<QueueEntry, bool> include)
	{
		long[] pids;
		lock (_admissionLock) pids = LockedPendingEntries
			.Where(entry => include(entry)
				&& (entry.Executor?.Matches(dbRef) == true
					|| (SchedulerKeys.IsSemaphore(entry.Group) && entry.SemaphoreTarget?.Matches(dbRef) == true)))
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
		using (SchedulerKeys.IsSemaphore(entry.Group) ? await EnterSemaphoreMutationAsync() : null)
		using (await LockDeferred())
		{
			lock (_admissionLock)
			{
				if (!_pendingEntries.TryGetValue(pid, out entry) || _ready.Contains(pid)) return true;
			}
			await RemoveDeferredTrigger(entry);
			// Both transition leases keep the cancelled reservation retryable until persistence succeeds.
			// Notifications and all timeouts account before becoming release-pending.
			if (SchedulerKeys.IsSemaphore(entry.Group) && entry.Deferred?.ReleasePending != true)
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
		var group = SchedulerKeys.Delay(state.Executor);
		delay = Nonnegative(delay);
		var due = DateTimeOffset.UtcNow + delay;
		var admission = await Admit(() => ExecuteList(command, state), SchedulerKeys.Owner(state.Executor), group, state.Executor, ready: false, sourceAttribute: SourceAttribute(state), enactor: state.Enactor);
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
		lock (_admissionLock) StoreEntry(entry with
		{ Deferred = new(due, Schedule, null, command, state) });
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
					StoreEntry(_pendingEntries[pid] with { DeferredReleaseOutcome = QueueOutcome.ScheduleFailed });
				}
				logger.LogError(cleanupFailure, "Delayed schedule cleanup failed for PID {Pid}; retry halt to release its reservation", pid);
				throw;
			}
			Release(pid, QueueOutcome.ScheduleFailed);
			throw;
		}
	}

	public IAsyncEnumerable<(string Group, (DateTimeOffset, NameOrDbRef)[])> GetAllTasks()
		=> ReadAllTasks(ExecutionBudget.CurrentToken);

	private async IAsyncEnumerable<(string Group, (DateTimeOffset, NameOrDbRef)[])> ReadAllTasks(
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

		// Not under the lock and enumerated across yields, so this keeps the copy Values makes.
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
	/// The executor a trigger name encodes, or the raw name when it does not carry one.
	/// </summary>
	private static NameOrDbRef DescribeTrigger(string triggerName)
		=> SchedulerKeys.TriggerExecutor(triggerName) is { } executor ? executor : triggerName;

	public IAsyncEnumerable<SemaphoreTaskData> GetSemaphoreTasks(DBRef obj)
		=> SemaphoreSnapshots(e => e.SemaphoreTarget?.Matches(obj) == true).ToAsyncEnumerable();

	public IAsyncEnumerable<SemaphoreTaskData> GetSemaphoreTasks(long pid)
	{
		lock (_admissionLock)
		{
			return (_pendingEntries.TryGetValue(pid, out var entry) && IsWaitingSemaphore(entry)
				? [SemaphoreSnapshot(entry, DateTimeOffset.UtcNow)]
				: Array.Empty<SemaphoreTaskData>()).ToAsyncEnumerable();
		}
	}

	public IAsyncEnumerable<SemaphoreTaskData> GetSemaphoreTasks(DbRefAttribute objAttribute)
	{
		var group = SchedulerKeys.Semaphore(objAttribute);
		return SemaphoreSnapshots(e => e.Group == group).ToAsyncEnumerable();
	}

	private SemaphoreTaskData[] SemaphoreSnapshots(Func<QueueEntry, bool> predicate)
	{
		lock (_admissionLock)
		{
			var now = DateTimeOffset.UtcNow;
			return LockedPendingEntries.Where(e => IsWaitingSemaphore(e) && predicate(e))
				.OrderBy(e => e.Pid).Select(e => SemaphoreSnapshot(e, now)).ToArray();
		}
	}

	// Caller holds _admissionLock.
	private bool IsWaitingSemaphore(QueueEntry e) => e.Deferred?.Semaphore is not null && !_ready.Contains(e.Pid);

	private static SemaphoreTaskData SemaphoreSnapshot(QueueEntry e, DateTimeOffset now)
		=> new(e.Pid, e.Deferred!.Command,
			e.Executor ?? new DBRef(-1), new DbRefAttribute(e.SemaphoreTarget!.Value, e.Deferred.Semaphore!.Value.Attribute),
			e.Deferred.Paused ? e.Deferred.Remaining : Nonnegative(e.Deferred.Due - now));

	public IAsyncEnumerable<long> GetDelayTasks(DBRef obj)
		=> ReadDelayTasks(obj, ExecutionBudget.CurrentToken);

	private async IAsyncEnumerable<long> ReadDelayTasks(DBRef obj, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ExecutionBudget.CurrentToken);
		var token = cancellation.Token;
		token.ThrowIfCancellationRequested();
		var keys = await _scheduler.GetTriggerKeys(
			GroupMatcher<TriggerKey>.GroupEquals(SchedulerKeys.Delay(obj)), token);

		foreach (var key in keys)
		{
			token.ThrowIfCancellationRequested();
			if (SchedulerKeys.TryTriggerPid(key.Name, out var pid))
			{
				yield return pid;
			}
		}
	}

	/// <summary>
	/// The PIDs on <paramref name="obj"/>'s run queue, which is what <c>@ps</c> counts as the command
	/// queue and what <c>@halt &lt;object&gt;</c> reaches. The entry's executor answers this, not its
	/// trigger name or group: an attribute queued by <see cref="AdmitAsyncAttribute"/> is named after
	/// its attribute, and a released <c>@wait &lt;obj&gt;/&lt;attr&gt;</c> keeps the semaphore group
	/// it was scheduled under while running as ordinary queued work. PennMUSH's <c>do_halt</c> matches
	/// on the executor across the run, wait and semaphore queues alike (<c>src/cque.c:2167-2218</c>),
	/// and <c>dequeue_semaphores</c> (<c>:1379-1427</c>) moves a released entry onto the run queue
	/// with its executor intact. Entries still waiting are excluded — they are the wait and semaphore
	/// queues <c>@ps</c> counts separately — and so is direct player input, as it is in Penn.
	/// </summary>
	public IAsyncEnumerable<long> GetEnqueueTasks(DBRef obj)
	{
		long[] pids;
		lock (_admissionLock)
			pids = LockedPendingEntries
				.Where(entry => entry.Executor?.Matches(obj) == true
					&& entry.Group != DirectInputGroup
					&& _ready.Contains(entry.Pid))
				.Select(entry => entry.Pid)
				.ToArray();
		return pids.ToAsyncEnumerable();
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
				StoreEntry(entry with { Deferred = entry.Deferred with { Remaining = delay } });
				return;
			}
		}
		try { await ReplaceDeferredSchedule(entry, delay); }
		catch
		{
			lock (_admissionLock)
				if (_pendingEntries.TryGetValue(pid, out entry!)) StoreEntry(entry with
				{ Deferred = entry.Deferred! with { Paused = true, Remaining = delay, Reason = "Schedule update failed" } });
			throw;
		}
	}

	public async ValueTask DisposeAsync()
	{
		QueueEntry[] entries;
		lock (_admissionLock) { _stopping = true; _immediateQueue.Writer.TryComplete(); entries = LockedPendingEntries.ToArray(); }
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
