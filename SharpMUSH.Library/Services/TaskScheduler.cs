using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Extensions;
using OneOf;
using Quartz;
using Quartz.Impl.Matchers;
using Quartz.Lambda;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Concurrent;
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
public class TaskScheduler(
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
		Func<ValueTask>? BeforeExecution = null,
		DBRef? SemaphoreTarget = null,
		bool ManagesSemaphoreCount = false
	);

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
	 string identity, string group, DBRef? executor, long? handle = null, bool ready = true, DBRef? semaphoreTarget = null, bool managesSemaphoreCount = false)
	{
		string owner = $"handle:{handle}";
		long ownerLimit = configuration?.CurrentValue.Limit.PlayerQueueLimit ?? 100;
		if (executor is not null)
		{
			var target = await mediator.Send(new GetObjectNodeQuery(executor.Value));
			if (target.IsNone) return Reject(QueueRejectionReason.InvalidTarget);
			executor = target.Known().Object().DBRef;
			if (await target.Known().IsWizard() || await target.Known().HasPower("Queue"))
				ownerLimit += Math.Max(0, await mediator.Send(new GetObjectCountQuery()));
			owner = (await target.Known().Object().Owner.WithCancellation(CancellationToken.None)).Object.DBRef.ToString();
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
		if (!result.Accepted && notifyService is not null)
		{
			if (handle is not null) await notifyService.NotifyLocalized(handle.Value, "QueueRejected", result.Reason);
			else if (DBRef.TryParse(owner, out var player))
				await foreach (var connection in connectionService.Get(player!.Value))
					await notifyService.NotifyLocalized(connection.Handle, "QueueRejected", result.Reason);
		}
		return result;
	}
	private readonly SemaphoreSlim _semaphoreMutations = new(1, 1);

	/// <summary>Serialize semaphore counter transactions. Acquire before any deferred queue lease.</summary>
	public async ValueTask<IDisposable> EnterSemaphoreMutationAsync()
	{
		await _semaphoreMutations.WaitAsync(ExecutionBudget.CurrentToken);
		return new SemaphoreMutationLease(_semaphoreMutations);
	}

	private sealed class SemaphoreMutationLease(SemaphoreSlim gate) : IDisposable
	{
		private SemaphoreSlim? _gate = gate;
		public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
	}

	private async ValueTask AdjustSemaphoreCount(string group, DBRef? target = null)
	{
		using var lease = await EnterSemaphoreMutationAsync();
		await AdjustSemaphoreCountCore(group, target);
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
	private ValueTask<QueueAdmissionResult> Activate(long pid, bool semaphoreTimeout = false, bool readyReserved = false)
	{
		lock (_admissionLock)
		{
			if (_stopping) return ValueTask.FromResult(Reject(QueueRejectionReason.ShuttingDown));
			if (!_pendingEntries.TryGetValue(pid, out var entry)) return ValueTask.FromResult(new QueueAdmissionResult(null, QueueRejectionReason.AlreadyReleased));
			if (readyReserved ? !_ready.Contains(pid) : !_ready.Add(pid)) return ValueTask.FromResult(new QueueAdmissionResult(null, QueueRejectionReason.AlreadyReleased));
			var group = entry.Group;
			var semaphoreTarget = entry.SemaphoreTarget;
			entry = entry with
			{
				Group = EnqueueGroup,
				// Run accounting on the serialized consumer, including when this released job is halted.
				// This prevents timeout updates racing a command's @notify attribute update.
				BeforeExecution = semaphoreTimeout && group.StartsWith(SemaphoreGroup + ":") ? () => AdjustSemaphoreCount(group, semaphoreTarget) : null
			};
			_pendingEntries[pid] = entry;
			_immediateQueue.Writer.TryWrite(entry);
		}
		EnsureConsumerStarted();
		return ValueTask.FromResult(new QueueAdmissionResult(pid, QueueRejectionReason.None));
	}
	private async ValueTask<CallState?> ExecuteList(MString command, ParserState state)
	{
		if (state.Executor is not null && (await mediator.Send(new GetObjectNodeQuery(state.Executor.Value))).IsNone) return null;
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
					// Released semaphore accounting still runs for halted entries, but shares the
					// entry's elapsed-time limit. Link halt cancellation only for the user body.
					using var accountingBudget = entry.BeforeExecution is null ? null : ExecutionBudget.FromMilliseconds(milliseconds);
					using var accountingScope = accountingBudget?.Enter();
					if (entry.BeforeExecution is not null)
					{
						try { await entry.BeforeExecution(); }
						catch (Exception ex) { logger.LogError(ex, "Semaphore bookkeeping failed for PID {Pid}", entry.Pid); }
					}
					if (entry.Cts.IsCancellationRequested) continue;
					if (accountingBudget?.IsExceeded == true)
					{
						await NotifyExpired(entry);
						continue;
					}
					using var budget = accountingBudget is null
						? ExecutionBudget.FromMilliseconds(milliseconds, entry.Cts.Token)
						: new ExecutionBudget(accountingBudget.Remaining == TimeSpan.MaxValue ? Timeout.InfiniteTimeSpan : accountingBudget.Remaining, entry.Cts.Token);
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
		if (notifyService is null || entry.Cts.IsCancellationRequested) return;
		try
		{
			if (DBRef.TryParse(entry.Owner, out var owner))
				await foreach (var connection in connectionService.Get(owner!.Value)) await notifyService.Notify(connection.Handle, ExecutionBudget.Error);
			else if (entry.Owner.StartsWith("handle:") && long.TryParse(entry.Owner[7..], out var handle))
				await notifyService.Notify(handle, ExecutionBudget.Error);
		}
		catch (Exception ex) { logger.LogWarning(ex, "Could not report execution limit for PID {Pid}", entry.Pid); }
	}

	public ValueTask<QueueAdmissionResult> EnqueueWork(Func<ValueTask<CallState?>> action, string triggerName, string group)
	 => Admit(action, triggerName, group, null);

	public ValueTask<QueueAdmissionResult> EnqueueWork(Func<ValueTask<CallState?>> action, string triggerName, string group, DBRef executor)
	 => Admit(action, triggerName, group, executor);

	public async ValueTask<QueueAdmissionResult> ReleaseScheduledWork(long pid, bool semaphoreTimeout = false)
	{
		QueueEntry? entry;
		lock (_admissionLock) _pendingEntries.TryGetValue(pid, out entry);
		if (!semaphoreTimeout || entry?.ManagesSemaphoreCount is not true)
			return await Activate(pid, semaphoreTimeout);

		using var mutation = await EnterSemaphoreMutationAsync();
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
			await AdjustSemaphoreCountCore(entry.Group, entry.SemaphoreTarget);
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

	public ValueTask<QueueAdmissionResult> WriteUserCommand(long handle, MString command, ParserState state)
	 => Admit(async () =>
	 {
		 if (!string.IsNullOrEmpty(state.ConnectionSessionId) && connectionService.Get(handle)?.Metadata.GetValueOrDefault("SessionId") != state.ConnectionSessionId) return null;
		 return await parser.FromState(state).CommandParse(handle, connectionService, command);
	 }, $"handle:{handle}", DirectInputGroup, connectionService.Get(handle)?.Ref, handle);

	private async ValueTask<ParserState> CaptureExecutor(ParserState state)
	{
		if (state.Executor is not { } executor) return state;
		var target = await mediator.Send(new GetObjectNodeQuery(executor));
		return target.IsNone ? state : state with { Executor = target.Known().Object().DBRef };
	}
	public async ValueTask<QueueAdmissionResult> WriteCommandList(MString command, ParserState state)
	{
		state = await CaptureExecutor(state);
		return await Admit(() => ExecuteList(command, state), $"dbref:{state.Executor}", EnqueueGroup, state.Executor);
	}

	public ValueTask<QueueAdmissionResult> WriteCommandList(MString command, ParserState state, DbRefAttribute dbRefAttribute, int oldValue, bool manageSemaphoreCount = false)
	 => WriteCommandList(command, state, dbRefAttribute, oldValue, TimeSpan.FromDays(36500), manageSemaphoreCount);

	public async ValueTask<QueueAdmissionResult> WriteAsyncAttribute(Func<ValueTask<ParserState>> function, DbRefAttribute dbAttribute, DBRef? executor = null)
	{
		var target = await mediator.Send(new GetObjectNodeQuery(dbAttribute.DbRef));
		if (target.IsNone) return Reject(QueueRejectionReason.InvalidTarget);
		dbAttribute = new DbRefAttribute(target.Known().Object().DBRef, dbAttribute.Attribute);
		executor = (await CaptureExecutor(ParserState.Empty with { Executor = executor ?? dbAttribute.DbRef })).Executor;
		return await Admit(async () =>
		{
			if (executor is not null && (await mediator.Send(new GetObjectNodeQuery(executor.Value))).IsNone) return null;
			var obj = await mediator.Send(new GetObjectNodeQuery(dbAttribute.DbRef));
			if (obj.IsNone) return null;
			var parserState = await function();
			ExecutionBudget.Current?.ThrowIfExceeded();
			var actor = await parserState.KnownExecutorObject(mediator);
			var attr = await attributeService.GetAttributeAsync(actor, obj.Known, string.Join('`', dbAttribute.Attribute), IAttributeService.AttributeMode.Execute);
			if (!attr.IsAttribute) return new CallState("#-1");
			return await ExecuteList(attr.AsAttribute.Last().Value, parserState);
		}, $"async:{dbAttribute}", EnqueueGroup, executor);
	}

	public async ValueTask<QueueAdmissionResult> WriteCommandList(MString command, ParserState state,
	 DbRefAttribute dbRefAttribute, int oldValue, TimeSpan timeout, bool manageSemaphoreCount = false)
	{
		if (!manageSemaphoreCount && oldValue < 0) return await WriteCommandList(command, state);
		state = await CaptureExecutor(state);
		var target = await mediator.Send(new GetObjectNodeQuery(dbRefAttribute.DbRef));
		if (target.IsNone) return Reject(QueueRejectionReason.InvalidTarget);
		var group = $"{SemaphoreGroup}:{dbRefAttribute}";
		// Do not expose a reservation to halt until its counter transaction owns the lease.
		using var mutation = manageSemaphoreCount ? await EnterSemaphoreMutationAsync() : null;
		var admission = await Admit(() => ExecuteList(command, state), $"dbref:{state.Executor}", group, state.Executor, ready: false, semaphoreTarget: target.Known().Object().DBRef, managesSemaphoreCount: manageSemaphoreCount);
		if (!admission.Accepted) return admission;
		var counterWritten = false;
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
				var attribute = await mediator.CreateStream(new GetAttributeQuery(fullTarget, dbRefAttribute.Attribute)).LastOrDefaultAsync();
				counterCreated = attribute is null;
				currentCount = attribute is null || attribute.Value.Length == 0 ? 0 : int.Parse(attribute.Value.ToPlainText());
				god = (await mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).AsPlayer;
				if (!await mediator.Send(new SetAttributeCommand(fullTarget, dbRefAttribute.Attribute,
					MarkupString.MarkupText.Plain(checked(currentCount + 1).ToString()), god)))
					throw new InvalidOperationException("Semaphore count update failed.");
				counterWritten = true;
				if (attribute is null)
					await SemaphoreAttributes.InitializeAsync(mediator, fullTarget, dbRefAttribute.Attribute);
				if (currentCount < 0)
				{
					var activated = await Activate(admission.Pid!.Value);
					if (activated.Accepted) return activated;
					throw new OperationCanceledException("Semaphore reservation was released before activation.");
				}
			}
			await _scheduler.ScheduleJob(JobBuilder.CreateForAsync<SemaphoreTask>()
			 .SetJobData(new JobDataMap((IDictionary<string, object>)new Dictionary<string, object> { { "Command", command }, { "State", state } })).Build(),
			 TriggerBuilder.Create().WithSimpleSchedule(x => x.WithRepeatCount(0)).StartAt(DateTimeOffset.UtcNow + timeout)
				.WithIdentity($"dbref:{state.Executor}-{admission.Pid}", group).Build());
			return admission;
		}
		catch
		{
			try
			{
				// The mutation lease excludes notify, drain and timeout bookkeeping until rollback completes.
				if (counterWritten)
				{
					// Restore absence as well as value: partial custom flags cannot pass validation.
					var restored = counterCreated
						? await mediator.Send(new WipeAttributeCommand(fullTarget, dbRefAttribute.Attribute))
						: await mediator.Send(new SetAttributeCommand(fullTarget, dbRefAttribute.Attribute,
							MarkupString.MarkupText.Plain(currentCount.ToString()), god!));
					if (!restored) throw new InvalidOperationException("Semaphore admission rollback failed.");
				}
			}
			finally { Release(admission.Pid!.Value); }
			throw;
		}
	}

	public async ValueTask<IReadOnlyList<QueueAdmissionResult>> Notify(DbRefAttribute dbAttribute, int oldValue, int count = 1)
	{
		var keys = await _scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals($"{SemaphoreGroup}:{dbAttribute}"));
		var outcomes = new List<QueueAdmissionResult>();
		foreach (var key in keys.OrderBy(k => long.Parse(k.Name.Split('-').Last())).Take(Math.Max(0, count)))
		{
			var trigger = await _scheduler.GetTrigger(key);
			if (trigger is null) continue;
			await _scheduler.UnscheduleJob(key);
			await _scheduler.DeleteJob(trigger.JobKey);
			outcomes.Add(await Activate(long.Parse(key.Name.Split('-').Last())));
		}
		return outcomes;
	}

	public ValueTask<IReadOnlyList<QueueAdmissionResult>> NotifyAll(DbRefAttribute dbAttribute)
	 => Notify(dbAttribute, 0, int.MaxValue);

	public async ValueTask<bool> ModifyQRegisters(DbRefAttribute dbAttribute, Dictionary<string, MString> qRegisters)
	{
		if (qRegisters == null || qRegisters.Count == 0)
		{
			return false;
		}

		var semaphoresForObject = await _scheduler
			.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals($"{SemaphoreGroup}:{dbAttribute}"));

		var firstTrigger = semaphoresForObject.OrderBy(k => long.Parse(k.Name.Split('-').Last())).FirstOrDefault();
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

		var selected = semaphoresForObject.OrderBy(k => long.Parse(k.Name.Split('-').Last())).Take(count ?? int.MaxValue).ToArray();
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
		using var mutation = await EnterSemaphoreMutationAsync();
		await _scheduler.UnscheduleJob(new TriggerKey(entry.TriggerName, entry.Group));
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
		lock (_admissionLock) removed = RemoveEntry(pid);
		removed?.Cts.Dispose();
		return true;
	}

	public async ValueTask<QueueAdmissionResult> WriteCommandList(MString command, ParserState state, TimeSpan delay)
	{
		state = await CaptureExecutor(state);
		var admission = await Admit(() => ExecuteList(command, state), $"dbref:{state.Executor}", $"{DelayGroup}:{state.Executor}", state.Executor, ready: false);
		if (!admission.Accepted) return admission;
		try
		{
			await _scheduler.ScheduleJob(async () => { await Activate(admission.Pid!.Value); },
			 builder => builder.StartAt(DateTimeOffset.UtcNow + delay).WithSimpleSchedule(x => x.WithRepeatCount(0))
				.WithIdentity($"dbref:{state.Executor}-{admission.Pid}", $"{DelayGroup}:{state.Executor}"));
			return admission;
		}
		catch { Release(admission.Pid!.Value); throw; }
	}

	public async IAsyncEnumerable<(string Group, (DateTimeOffset, OneOf<string, DBRef>)[])> GetAllTasks()
	{
		var translate = new Func<string, string>(x =>
			new string(x.Replace("dbref:", string.Empty).Replace("handle:", string.Empty)
				.TakeWhile(c => c != '-').ToArray()));

		var keys = await _scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.AnyGroup());
		var keyTriggers = keys.ToAsyncEnumerable()
			.Select<TriggerKey, ITrigger>(async (triggerKey, ct) => await _scheduler.GetTrigger(triggerKey, ct))
			.GroupBy(trigger => trigger.Key.Group, trigger => (trigger.FinalFireTimeUtc!.Value, trigger.Key.Name));
		await foreach (var key in keyTriggers)
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
			yield return (group.Key, group.Select(e => (
				DateTimeOffset.UtcNow,
				DBRef.TryParse(translate(e.TriggerName), out var dbref)
					? OneOf<string, DBRef>.FromT1(dbref!.Value)
					: OneOf<string, DBRef>.FromT0(e.TriggerName)
			)).ToArray());
		}
	}

	public async IAsyncEnumerable<SemaphoreTaskData> GetSemaphoreTasks(DBRef obj)
	{
		var candidates = await _scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupStartsWith($"{SemaphoreGroup}:#{obj.Number}"));
		var keys = candidates.Where(key => DbRefAttribute.TryParse(key.Group[(SemaphoreGroup.Length + 1)..], out var attribute)
		 && attribute!.Value.DbRef.Matches(obj));
		var keyTriggers = keys.ToAsyncEnumerable()
			.Select<TriggerKey, SemaphoreTaskData>(async (triggerKey, _) =>
				await MapSemaphoreTaskData(_scheduler, triggerKey));

		await foreach (var key in keyTriggers)
		{
			yield return key;
		}
	}

	public async IAsyncEnumerable<SemaphoreTaskData> GetSemaphoreTasks(long pid)
	{
		var keys = await _scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupStartsWith($"{SemaphoreGroup}:"));
		var keyTriggers = keys.ToAsyncEnumerable()
			.Where(key => key.Name.EndsWith($"-{pid}"))
			.Select<TriggerKey, SemaphoreTaskData>(async (triggerKey, _) =>
				await MapSemaphoreTaskData(_scheduler, triggerKey));

		await foreach (var key in keyTriggers)
		{
			yield return key;
		}
	}

	public async IAsyncEnumerable<SemaphoreTaskData> GetSemaphoreTasks(DbRefAttribute objAttribute)
	{
		var keys = await _scheduler.GetTriggerKeys(
			GroupMatcher<TriggerKey>.GroupEquals($"{SemaphoreGroup}:{objAttribute}"));
		var keyTriggers = keys.ToAsyncEnumerable()
			.Select<TriggerKey, SemaphoreTaskData>(async (triggerKey, _) =>
				await MapSemaphoreTaskData(_scheduler, triggerKey));

		await foreach (var key in keyTriggers)
		{
			yield return key;
		}
	}

	public async IAsyncEnumerable<long> GetDelayTasks(DBRef obj)
	{
		var keys = await _scheduler.GetTriggerKeys(
			GroupMatcher<TriggerKey>.GroupEquals($"{DelayGroup}:{obj}"));

		foreach (var key in keys)
		{
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

	private static async ValueTask<SemaphoreTaskData> MapSemaphoreTaskData(IScheduler scheduler, TriggerKey triggerKey)
	{
		var trigger = await scheduler.GetTrigger(triggerKey);
		var job = await scheduler.GetJobDetail(trigger.JobKey);
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
		foreach (var pid in _pendingEntries.Keys) Release(pid);
		_shutdownCts.Dispose();
		GC.SuppressFinalize(this);
	}
}