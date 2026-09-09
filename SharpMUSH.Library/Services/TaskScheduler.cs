using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Extensions;
using OneOf;
using Quartz;
using Quartz.Impl.Matchers;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

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
	INotifyService? notifyService = null) : ITaskScheduler, IAsyncDisposable
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
		bool ManagesSemaphoreCount = false
	);

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
	private bool _stopping;
	public QueueUsage GetQueueUsage()
	{
		lock (_admissionLock) return new(_pendingEntries.Count,
		 _pendingEntries.Values.GroupBy(e => e.Owner).ToDictionary(g => g.Key, g => g.Count()),
		 new Dictionary<QueueRejectionReason, long>(_rejections));
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
		return _pendingEntries.TryRemove(pid, out var entry) ? entry : null;
	}
	private void Release(long pid)
	{
		QueueEntry? entry;
		lock (_admissionLock) entry = RemoveEntry(pid);
		entry?.Cts.Dispose();
	}
	private void CancelEntry(QueueEntry entry)
	{
		try { entry.Cts.Cancel(); }
		catch (ObjectDisposedException) { /* The consumer already completed and released this entry. */ }
		catch (AggregateException ex) { logger.LogWarning(ex, "Cancellation callback failed for PID {Pid}", entry.Pid); }
	}
	private bool ReleasePending(long pid)
	{
		QueueEntry? entry;
		lock (_admissionLock)
			// A timeout may have won while Quartz's stale trigger was being unscheduled.
			// Drain owns waiting work only; it must not cancel a published/running body.
			entry = _ready.Contains(pid) ? null : RemoveEntry(pid);
		entry?.Cts.Dispose();
		return entry is not null;
	}

	private async ValueTask<QueueAdmissionResult> Admit(Func<ValueTask<CallState?>> action,
	 string identity, string group, DBRef? executor, long? handle = null, bool ready = true, DBRef? semaphoreTarget = null, bool managesSemaphoreCount = false, bool notifyOnRejection = true)
	{
		string owner = $"handle:{handle}";
		long ownerLimit = configuration?.CurrentValue.Limit.PlayerQueueLimit ?? 100;
		if (executor is not null)
		{
			var target = await mediator.Send(new GetObjectNodeQuery(executor.Value), ExecutionBudget.CurrentToken);
			if (target.IsNone) return Reject(QueueRejectionReason.InvalidTarget);
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
				var entry = new QueueEntry(pid, $"{identity}-{pid}", group, action, new CancellationTokenSource(), owner, executor, SemaphoreTarget: semaphoreTarget, ManagesSemaphoreCount: managesSemaphoreCount);
				_pendingEntries[pid] = entry;
				if (ready) { _ready.Add(pid); _immediateQueue.Writer.TryWrite(entry); }
				result = new(pid, QueueRejectionReason.None);
			}
		}
		if (result.Accepted && ready) EnsureConsumerStarted();
		if (!result.Accepted && notifyOnRejection && notifyService is not null)
		{
			if (handle is not null) await notifyService.NotifyLocalized(handle.Value, "QueueRejected", result.Reason);
			else if (DBRef.TryParse(owner, out var player))
				await foreach (var connection in connectionService.Get(player!.Value))
					await notifyService.NotifyLocalized(connection.Handle, "QueueRejected", result.Reason);
		}
		return result;
	}
	private readonly SemaphoreSlim _semaphoreMutations = new(1, 1);
	private readonly SemaphoreSlim _delayedChanges = new(1, 1);
	private readonly HashSet<long> _delayedRepairs = [];
	private readonly HashSet<long> _semaphorePublications = [];

	private async ValueTask<IDisposable> EnterDelayedTransitionAsync()
	{
		await _delayedChanges.WaitAsync(ExecutionBudget.CurrentToken);
		return new SemaphoreMutationLease(_delayedChanges);
	}
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
					Release(pid);
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

	// Caller holds the mutation lease; shared with pending halt bookkeeping.
	private async ValueTask AdjustSemaphoreCountCore(string group, DBRef? target = null)
	{
		var semaphore = DbRefAttribute.Parse(group[(SemaphoreGroup.Length + 1)..]);
		if (target is not null) semaphore = new DbRefAttribute(target.Value, semaphore.Attribute);
		var value = await mediator.CreateStream(new GetAttributeQuery(semaphore.DbRef, semaphore.Attribute), ExecutionBudget.CurrentToken).LastOrDefaultAsync(ExecutionBudget.CurrentToken);
		if (value is null || !int.TryParse(value.Value.ToPlainText(), out var count)) return;
		var god = await mediator.Send(new GetObjectNodeQuery(new DBRef(1)), ExecutionBudget.CurrentToken);
		if (!god.IsPlayer) return;
		if (!await mediator.Send(new SetAttributeCommand(semaphore.DbRef, semaphore.Attribute,
		 MarkupString.MarkupText.Plain((count > 0 ? count - 1 : 0).ToString()), god.AsPlayer), ExecutionBudget.CurrentToken))
			throw new InvalidOperationException("Semaphore count update failed.");
	}
	// Caller holds the semaphore mutation lease. Keep an uncertain timeout write behind
	// the existing command-repair barrier until its before/after value is reconciled.
	private async ValueTask AdjustTimeoutSemaphoreCountCore(QueueEntry entry)
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
				throw new InvalidOperationException("Semaphore timeout count update failed.");
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
				var observed = await Read();
				if (observed is null || !int.TryParse(observed.Value.ToPlainText(), out var current)
					|| current != expected && current != original)
					throw new InvalidOperationException("Semaphore changed during uncertain timeout accounting.");
				if (current != expected) await Write();
				ExecutionBudget.Current?.ThrowIfExceeded();
				lock (_admissionLock) _semaphoreCommandReservations.Remove(entry.Pid);
				await Activate(entry.Pid);
			};
			throw;
		}
	}

	private ValueTask<QueueAdmissionResult> Activate(long pid, bool readyReserved = false)
	{
		lock (_admissionLock)
		{
			if (_stopping) return ValueTask.FromResult(Reject(QueueRejectionReason.ShuttingDown));
			if (!_pendingEntries.TryGetValue(pid, out var entry) || _semaphoreRepairs.ContainsKey(pid) || _semaphoreCommandReservations.Contains(pid) || _delayedRepairs.Contains(pid)) return ValueTask.FromResult(new QueueAdmissionResult(null, QueueRejectionReason.AlreadyReleased));
			if (readyReserved ? !_ready.Contains(pid) : !_ready.Add(pid)) return ValueTask.FromResult(new QueueAdmissionResult(null, QueueRejectionReason.AlreadyReleased));
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
		lock (_admissionLock) _consumerTask ??= Task.Run(() => ProcessQueueAsync(_shutdownCts.Token));
	}

	private async Task ProcessQueueAsync(CancellationToken shutdownToken)
	{
		try
		{
			await foreach (var entry in _immediateQueue.Reader.ReadAllAsync(shutdownToken))
			{
				try
				{
					var milliseconds = configuration?.CurrentValue.Limit.QueueEntryCpuTime ?? 1000;
					if (entry.Cts.IsCancellationRequested) continue;
					using var budget = ExecutionBudget.FromMilliseconds(milliseconds, entry.Cts.Token);
					using var scope = budget.Enter();
					try
					{
						budget.ThrowIfExceeded();
						await entry.Action();
						if (budget.IsExpired) await NotifyExpired(entry);
					}
					catch (OperationCanceledException) when (budget.IsExpired) { await NotifyExpired(entry); }
					catch (OperationCanceledException) when (budget.IsCancelled) { logger.LogDebug("Queued command {Pid} cancelled", entry.Pid); }
				}
				catch (Exception ex) { logger.LogError(ex, "Error executing queued command (PID {Pid}, Group {Group})", entry.Pid, entry.Group); }
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

	public async ValueTask<QueueAdmissionResult> ReleaseScheduledWork(long pid, bool semaphoreTimeout = false)
	{
		QueueEntry? entry;
		bool recoveringTimeout;
		lock (_admissionLock)
		{
			if (_stopping) return Reject(QueueRejectionReason.ShuttingDown);
			if (!_pendingEntries.TryGetValue(pid, out entry) || (_ready.Contains(pid)
				&& !(semaphoreTimeout && entry.Group.StartsWith(SemaphoreGroup + ":", StringComparison.Ordinal)))
				|| _semaphoreRepairs.ContainsKey(pid) || (!semaphoreTimeout && _semaphoreCommandReservations.Contains(pid)) || _delayedRepairs.Contains(pid))
				return new(null, QueueRejectionReason.AlreadyReleased);
			recoveringTimeout = semaphoreTimeout && _semaphoreCommandReservations.Contains(pid);
		}
		using var releaseCancellation = CancellationTokenSource.CreateLinkedTokenSource(ExecutionBudget.CurrentToken, _shutdownCts.Token);
		var milliseconds = configuration?.CurrentValue.Limit.QueueEntryCpuTime ?? 1000;
		using var releaseBudget = ExecutionBudget.FromMilliseconds(milliseconds == 0 ? 1000 : milliseconds, releaseCancellation.Token);
		using var releaseScope = releaseBudget.Enter();
		releaseBudget.ThrowIfExceeded();

		using var delayedTransition = entry?.Group.StartsWith(DelayGroup + ":", StringComparison.Ordinal) is true
			? await EnterDelayedTransitionAsync() : null;
		using var mutation = entry?.Group.StartsWith(SemaphoreGroup + ":", StringComparison.Ordinal) is true
			? await EnterSemaphoreMutationAsync() : null;
		if (recoveringTimeout) return new(pid, QueueRejectionReason.None);
		if (!semaphoreTimeout || entry?.Group.StartsWith(SemaphoreGroup + ":", StringComparison.Ordinal) is not true)
			return await Activate(pid);
		lock (_admissionLock)
		{
			if (_stopping) return Reject(QueueRejectionReason.ShuttingDown);
			if (!_pendingEntries.TryGetValue(pid, out entry) || !_ready.Add(pid))
				return new(null, QueueRejectionReason.AlreadyReleased);
		}
		try
		{
			// Claim the release before awaiting persistence. Halt retains the reservation and
			// drain excludes it, while notify cannot turn its outstanding count into lost credit.
			await AdjustTimeoutSemaphoreCountCore(entry);
			return await Activate(pid, readyReserved: true);
		}
		catch
		{
			lock (_admissionLock) _ready.Remove(pid);
			throw;
		}
	}

	private readonly IScheduler _scheduler = schedulerFactory.GetScheduler().GetAwaiter().GetResult();
	public const string DirectInputGroup = "direct-input";
	public const string EnqueueGroup = "enqueue";
	public const string SemaphoreGroup = "semaphore";
	public const string DelayGroup = "delay";

	[Flags]
	private enum TaskQueueType
	{
		Default = 0,
		Object = 1,
		Player = Object << 1,
		Socket = Player << 1,
		InPlace = Socket << 1,
		NoBreaks = InPlace << 1,
		PreserveQReg = NoBreaks << 1,
		ClearQReg = PreserveQReg << 1,
		PropagateQReg = ClearQReg << 1,
		NoList = PropagateQReg << 1,
		Break = NoList << 1,
		Retry = Break << 1,
		Debug = Retry << 1,
		NoDebug = Debug << 1,
		Priority = NoDebug << 1,
		DebugPrivileges = Priority << 1,
		Event = DebugPrivileges << 1,
		Recurse = InPlace | NoBreaks | PreserveQReg
	}

	public ValueTask<QueueAdmissionResult> AdmitUserCommand(long handle, MString command, ParserState state)
	 => Admit(async () =>
	 {
		 if (!string.IsNullOrEmpty(state.ConnectionSessionId) && connectionService.Get(handle)?.Metadata.GetValueOrDefault("SessionId") != state.ConnectionSessionId) return null;
		 return await parser.FromState(state).CommandParse(handle, connectionService, command);
	 }, $"handle:{handle}", DirectInputGroup, connectionService.Get(handle)?.Ref, handle);

	private async ValueTask<ParserState> CaptureExecutor(ParserState state)
	{
		if (state.Executor is not { } executor) return state;
		var target = await mediator.Send(new GetObjectNodeQuery(executor), ExecutionBudget.CurrentToken);
		return target.IsNone ? state : state with { Executor = target.Known().Object().DBRef };
	}
	public async ValueTask<QueueAdmissionResult> AdmitCommandList(MString command, ParserState state)
	{
		state = await CaptureExecutor(state);
		return await Admit(() => ExecuteList(command, state), $"dbref:{state.Executor}", EnqueueGroup, state.Executor);
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
		if (target.IsNone) return Reject(QueueRejectionReason.InvalidTarget);
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
		}, $"async:{dbAttribute}", EnqueueGroup, executor);
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
		state = await CaptureExecutor(state);
		var target = await mediator.Send(new GetObjectNodeQuery(dbRefAttribute.DbRef), ExecutionBudget.CurrentToken);
		if (target.IsNone) return Reject(QueueRejectionReason.InvalidTarget);
		var group = $"{SemaphoreGroup}:{dbRefAttribute}";
		// Do not expose a reservation to halt until its counter transaction owns the lease.
		using var mutation = await EnterSemaphoreMutationAsync();
		var admission = await Admit(() => ExecuteList(command, state), $"dbref:{state.Executor}", group, state.Executor, ready: false, semaphoreTarget: target.Known().Object().DBRef, managesSemaphoreCount: manageSemaphoreCount);
		if (!admission.Accepted) return admission;
		lock (_admissionLock) _semaphorePublications.Add(admission.Pid!.Value);
		var scheduleWriteAttempted = false;
		var triggerKey = new TriggerKey($"dbref:{state.Executor}-{admission.Pid}", group);
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
					Release(admission.Pid!.Value);
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
			QueueEntry publicationEntry;
			lock (_admissionLock) publicationEntry = _pendingEntries[admission.Pid!.Value];
			using var publication = CancellationTokenSource.CreateLinkedTokenSource(ExecutionBudget.CurrentToken, publicationEntry.Cts.Token);
			scheduleWriteAttempted = true;
			await _scheduler.ScheduleJob(JobBuilder.Create<SemaphoreTask>()
			 .SetJobData(new JobDataMap((IDictionary<string, object>)new Dictionary<string, object> { { "Command", command }, { "State", state } })).Build(),
			 TriggerBuilder.Create().WithSimpleSchedule(x => x.WithRepeatCount(0)).StartAt(DateTimeOffset.UtcNow + timeout)
				.WithIdentity(triggerKey).Build(), publication.Token);
			publication.Token.ThrowIfCancellationRequested();
			ExecutionBudget.Current?.ThrowIfExceeded();
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
			Release(admission.Pid!.Value);
			throw;
		}
		finally
		{
			lock (_admissionLock) _semaphorePublications.Remove(admission.Pid!.Value);
		}
	}

	private bool CanReleasePendingSemaphore(long pid)
	{
		lock (_admissionLock)
			return _pendingEntries.ContainsKey(pid) && !_ready.Contains(pid) && !_semaphorePublications.Contains(pid)
				&& !_semaphoreRepairs.ContainsKey(pid) && !_semaphoreCommandReservations.Contains(pid);
	}

	public async ValueTask<IReadOnlyList<QueueAdmissionResult>> NotifyCounted(DbRefAttribute dbAttribute, int oldValue, int count = 1)
	{
		var keys = await _scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals($"{SemaphoreGroup}:{dbAttribute}"));
		var outcomes = new List<QueueAdmissionResult>();
		foreach (var key in keys.OrderBy(k => long.Parse(k.Name.Split('-').Last()))
			.Where(key => CanReleasePendingSemaphore(long.Parse(key.Name.Split('-').Last()))).Take(Math.Max(0, count)))
		{
			var trigger = await _scheduler.GetTrigger(key);
			if (trigger is null) continue;
			await _scheduler.UnscheduleJob(key);
			await _scheduler.DeleteJob(trigger.JobKey);
			outcomes.Add(await Activate(long.Parse(key.Name.Split('-').Last())));
		}
		return outcomes;
	}

	public ValueTask<IReadOnlyList<QueueAdmissionResult>> NotifyAllCounted(DbRefAttribute dbAttribute)
	 => NotifyCounted(dbAttribute, 0, int.MaxValue);

	public async ValueTask<bool> ModifyQRegisters(DbRefAttribute dbAttribute, Dictionary<string, MString> qRegisters)
	{
		if (qRegisters == null || qRegisters.Count == 0)
		{
			return false;
		}

		var semaphoresForObject = await _scheduler
			.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals($"{SemaphoreGroup}:{dbAttribute}"));

		var firstTrigger = semaphoresForObject.OrderBy(k => long.Parse(k.Name.Split('-').Last()))
			.FirstOrDefault(key => CanReleasePendingSemaphore(long.Parse(key.Name.Split('-').Last())));
		if (firstTrigger == null)
		{
			return false;
		}

		try
		{
			var trigger = await _scheduler.GetTrigger(firstTrigger, CancellationToken.None);
			if (trigger == null)
			{
				return false;
			}

			var job = await _scheduler.GetJobDetail(trigger.JobKey);
			if (job == null)
			{
				return false;
			}

			var data = job.JobDataMap;

			if (!data.TryGetValue("State", out var stateObj) || stateObj is not ParserState state)
			{
				return false;
			}

			if (state.Registers.TryPeek(out var registers))
			{
				foreach (var qreg in qRegisters)
				{
					registers[qreg.Key.ToUpper()] = qreg.Value;
				}
			}
			else
			{
				var newRegisters = new Dictionary<string, MString>();
				foreach (var qreg in qRegisters)
				{
					newRegisters[qreg.Key.ToUpper()] = qreg.Value;
				}
				state.Registers.Push(newRegisters);
			}

			data["State"] = state;

			await _scheduler.AddJob(job, replace: true, storeNonDurableWhileAwaitingScheduling: true);

			return true;
		}
		catch (Exception)
		{
			// Job may have been removed or modified concurrently
			return false;
		}
	}

	public async ValueTask Drain(DbRefAttribute dbAttribute, int? count = null)
		=> _ = await DrainCounted(dbAttribute, count);

	public async ValueTask<int> DrainCounted(DbRefAttribute dbAttribute, int? count = null)
	{
		var semaphoresForObject = await _scheduler
			.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals($"{SemaphoreGroup}:{dbAttribute}"));

		var selected = semaphoresForObject.OrderBy(k => long.Parse(k.Name.Split('-').Last()))
			.Where(key => CanReleasePendingSemaphore(long.Parse(key.Name.Split('-').Last()))).Take(count ?? int.MaxValue).ToArray();
		if (selected.Length == 0) return 0;
		await _scheduler.UnscheduleJobs(selected);
		return selected.Count(key => ReleasePending(long.Parse(key.Name.Split('-').Last())));
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
		QueueEntry? entry;
		bool ready;
		lock (_admissionLock)
		{
			if (!_pendingEntries.TryGetValue(pid, out entry)) return false;
			ready = _ready.Contains(pid);
		}
		CancelEntry(entry);
		if (ready) return true;
		// Halt may be called outside an executing queue entry. Keep provider cleanup
		// bounded and interruptible by shutdown while awaiting every write to settle.
		using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ExecutionBudget.CurrentToken, _shutdownCts.Token);
		using var haltBudget = ExecutionBudget.FromMilliseconds(1000, cancellation.Token);
		using var haltScope = haltBudget.Enter();
		using var delayedTransition = entry.Group.StartsWith(DelayGroup + ":", StringComparison.Ordinal)
			? await EnterDelayedTransitionAsync() : null;
		using var mutation = entry.Group.StartsWith(SemaphoreGroup + ":", StringComparison.Ordinal)
			? await EnterSemaphoreMutationAsync() : null;
		await _scheduler.UnscheduleJob(new TriggerKey(entry.TriggerName, entry.Group), ExecutionBudget.CurrentToken);
		lock (_admissionLock)
		{
			if (!_pendingEntries.TryGetValue(pid, out entry)) return true;
			// Reserve the transition against timeout publication while persistence is awaited.
			if (!_ready.Add(pid)) return true;
		}
		try
		{
			if (entry.Group.StartsWith(SemaphoreGroup + ":"))
				await AdjustSemaphoreCountCore(entry.Group, entry.SemaphoreTarget);
		}
		catch
		{
			// Preserve the cancelled PID and its quota so a later halt or timer can retry.
			lock (_admissionLock) _ready.Remove(pid);
			throw;
		}
		QueueEntry? removed;
		lock (_admissionLock)
		{
			_delayedRepairs.Remove(pid);
			removed = RemoveEntry(pid);
		}
		removed?.Cts.Dispose();
		return true;
	}

	public async ValueTask<QueueAdmissionResult> AdmitCommandList(MString command, ParserState state, TimeSpan delay)
	{
		state = await CaptureExecutor(state);
		using var transition = await EnterDelayedTransitionAsync();
		var admission = await Admit(() => ExecuteList(command, state), $"dbref:{state.Executor}", $"{DelayGroup}:{state.Executor}", state.Executor, ready: false);
		if (!admission.Accepted) return admission;
		QueueEntry entry;
		lock (_admissionLock) entry = _pendingEntries[admission.Pid!.Value];
		using var publication = CancellationTokenSource.CreateLinkedTokenSource(ExecutionBudget.CurrentToken, entry.Cts.Token);
		var trigger = new TriggerKey(entry.TriggerName, entry.Group);
		try
		{
			await _scheduler.ScheduleJob(JobBuilder.Create<DelayedTask>().Build(),
				TriggerBuilder.Create().StartAt(DateTimeOffset.UtcNow + delay).WithSimpleSchedule(x => x.WithRepeatCount(0))
					.WithIdentity(trigger).Build(), publication.Token);
			// A provider may commit after cancellation. Keep the lease until it is removed.
			publication.Token.ThrowIfCancellationRequested();
			ExecutionBudget.Current?.ThrowIfExceeded();
			return admission;
		}
		catch
		{
			using var cleanup = ExecutionBudget.FromMilliseconds(1000);
			using var cleanupScope = cleanup.Enter();
			try { await _scheduler.UnscheduleJob(trigger, cleanup.Token); }
			catch (Exception cleanupFailure)
			{
				// The lost acknowledgement may hide a committed long-lived trigger. Its
				// unpublished reservation remains bounded and can be retried through halt.
				lock (_admissionLock) _delayedRepairs.Add(entry.Pid);
				logger.LogError(cleanupFailure, "Delayed schedule cleanup failed for PID {Pid}; retry halt to release its reservation", entry.Pid);
				throw;
			}
			Release(entry.Pid);
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
		var translate = new Func<string, string>(x =>
			new string(x.Replace("dbref:", string.Empty).Replace("handle:", string.Empty)
				.TakeWhile(c => c != '-').ToArray()));

		var keys = await _scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.AnyGroup(), token);
		var keyTriggers = keys.ToAsyncEnumerable()
			.Select<TriggerKey, ITrigger?>(async (triggerKey, ct) => await _scheduler.GetTrigger(triggerKey, ct))
			.Where(trigger => trigger is not null).Select(trigger => trigger!)
			.GroupBy(trigger => trigger.Key.Group, trigger => (trigger.FinalFireTimeUtc!.Value, trigger.Key.Name));
		await foreach (var key in keyTriggers.WithCancellation(token))
		{
			yield return (key.Key, key.Select(x => (
				x.Value,
				DBRef.TryParse(translate(x.Name), out var dbref)
					? OneOf<string, DBRef>.FromT1(dbref!.Value)
					: OneOf<string, DBRef>.FromT0(x.Name)
			)).ToArray());
		}

		foreach (var group in _pendingEntries.Values.Where(e => e.Group is DirectInputGroup or EnqueueGroup).GroupBy(e => e.Group))
		{
			token.ThrowIfCancellationRequested();
			yield return (group.Key, group.Select(e => (
				DateTimeOffset.UtcNow,
				DBRef.TryParse(translate(e.TriggerName), out var dbref)
					? OneOf<string, DBRef>.FromT1(dbref!.Value)
					: OneOf<string, DBRef>.FromT0(e.TriggerName)
			)).ToArray());
		}
	}

	public IAsyncEnumerable<SemaphoreTaskData> GetSemaphoreTasks(DBRef obj)
		=> ReadSemaphoreTasks(GroupMatcher<TriggerKey>.GroupStartsWith($"{SemaphoreGroup}:#{obj.Number}"),
			key => DbRefAttribute.TryParse(key.Group[(SemaphoreGroup.Length + 1)..], out var attribute)
				&& attribute!.Value.DbRef.Matches(obj), ExecutionBudget.CurrentToken);

	public IAsyncEnumerable<SemaphoreTaskData> GetSemaphoreTasks(long pid)
		=> ReadSemaphoreTasks(GroupMatcher<TriggerKey>.GroupStartsWith($"{SemaphoreGroup}:"),
			key => key.Name.EndsWith($"-{pid}"), ExecutionBudget.CurrentToken);

	public IAsyncEnumerable<SemaphoreTaskData> GetSemaphoreTasks(DbRefAttribute objAttribute)
		=> ReadSemaphoreTasks(GroupMatcher<TriggerKey>.GroupEquals($"{SemaphoreGroup}:{objAttribute}"),
			_ => true, ExecutionBudget.CurrentToken);

	private async IAsyncEnumerable<SemaphoreTaskData> ReadSemaphoreTasks(GroupMatcher<TriggerKey> groups,
		Func<TriggerKey, bool> predicate, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ExecutionBudget.CurrentToken);
		var token = cancellation.Token;
		token.ThrowIfCancellationRequested();
		var keys = await _scheduler.GetTriggerKeys(groups, token);
		foreach (var key in keys)
		{
			token.ThrowIfCancellationRequested();
			if (!predicate(key)) continue;
			var task = await MapSemaphoreTaskData(_scheduler, key, token);
			if (task is not null) yield return task;
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

		foreach (var key in keys)
		{
			token.ThrowIfCancellationRequested();
			// Extract PID from identity: "dbref:{executor}-{pid}"
			var parts = key.Name.Split('-');
			if (parts.Length == 2 && long.TryParse(parts[1], out var pid))
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

	private static async ValueTask<SemaphoreTaskData?> MapSemaphoreTaskData(IScheduler scheduler, TriggerKey triggerKey, CancellationToken token)
	{
		var trigger = await scheduler.GetTrigger(triggerKey, token);
		if (trigger is null) return null;
		var job = await scheduler.GetJobDetail(trigger.JobKey, token);
		if (job is null) return null;
		var data = job.JobDataMap;
		var command = (MString)data["Command"];
		var state = (ParserState)data["State"];
		var fireDelay = trigger.FinalFireTimeUtc is null
			? null
			: DateTimeOffset.UtcNow - trigger.FinalFireTimeUtc;
		var semaphoreSourceString = string.Join(':', triggerKey.Group.Split(':').Skip(1));
		var semaphoreSource = DbRefAttribute.Parse(semaphoreSourceString);
		var pid = long.Parse(triggerKey.Name.Split('-').Last());

		return new SemaphoreTaskData(pid, command, state.Caller!.Value, semaphoreSource, fireDelay);
	}

	public async ValueTask RescheduleSemaphoreTask(long pid, TimeSpan delay)
	{
		var allKeys = await _scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupStartsWith($"{SemaphoreGroup}"));

		// This should return just one or zero, but using it as an iterator simplifies the code.
		foreach (var key in allKeys.Where(x => x.Name.EndsWith($"-{pid}")))
		{
			var trigger = await _scheduler.GetTrigger(key);
			if (trigger is null) continue;
			await _scheduler.RescheduleJob(key, trigger.GetTriggerBuilder().StartAt(DateTimeOffset.UtcNow + delay).Build());
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
		await _delayedChanges.WaitAsync();
		_delayedChanges.Release();
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