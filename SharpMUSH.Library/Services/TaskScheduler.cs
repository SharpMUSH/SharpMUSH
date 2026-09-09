using Mediator;
using OneOf;
using Quartz;
using Quartz.Impl.Matchers;
using Quartz.Lambda;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
	INotifyService notifyService,
	IOptionsMonitor<SharpMUSHOptions> options,
	ILogger<TaskScheduler> logger) : ITaskScheduler, IAsyncDisposable
{
	private long _nextPid = 0;
	private long NextPid() => Interlocked.Increment(ref _nextPid);

	/// <summary>
	/// Represents a queued command entry for the FIFO immediate-execution queue.
	/// </summary>
	/// <param name="Executor">
	/// The object the entry runs as, or <see langword="null"/> for work that belongs to no object
	/// (the scheduler's own plumbing, and test fixtures). Entries without an executor are not
	/// charged against anyone's queue quota.
	/// </param>
	/// <param name="OwnerNumber">
	/// The dbref number of <paramref name="Executor"/>'s owner at admission time, which is the key
	/// the quota is counted under. Held on the entry rather than looked up again on release, so a
	/// <c>@chown</c> between admission and completion cannot credit the decrement to the wrong owner.
	/// </param>
	private sealed record QueueEntry(
		long Pid,
		DBRef? Executor,
		int? OwnerNumber,
		string TriggerName,
		string Group,
		Func<ValueTask<CallState?>> Action,
		CancellationTokenSource Cts
	);

	private const int ImmediateQueueCapacity = 10_000;

	private readonly Channel<QueueEntry> _immediateQueue = Channel.CreateBounded<QueueEntry>(
		new BoundedChannelOptions(ImmediateQueueCapacity)
		{
			SingleReader = true,
			FullMode = BoundedChannelFullMode.Wait
		});
	private readonly ConcurrentDictionary<long, QueueEntry> _pendingEntries = new();

	/// <summary>
	/// Pending immediate-queue entries per owner dbref number — PennMUSH's per-object <c>QUEUE</c>
	/// counter (<c>src/cque.c:173</c>), incremented on admission and decremented when the entry
	/// leaves the queue.
	/// </summary>
	private readonly ConcurrentDictionary<int, int> _pendingByOwner = new();

	private readonly CancellationTokenSource _shutdownCts = new();
	private Task? _consumerTask;

	private void EnsureConsumerStarted()
	{
		LazyInitializer.EnsureInitialized(ref _consumerTask, () => Task.Run(() => ProcessQueueAsync(_shutdownCts.Token)));
	}

	private async Task ProcessQueueAsync(CancellationToken shutdownToken)
	{
		try
		{
			await foreach (var entry in _immediateQueue.Reader.ReadAllAsync(shutdownToken))
			{
				if (entry.Cts.IsCancellationRequested)
				{
					ReleaseEntry(entry);
					continue;
				}

				try
				{
					await entry.Action();
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "Error executing queued command (PID {Pid}, Group {Group})", entry.Pid, entry.Group);
				}
				finally
				{
					ReleaseEntry(entry);
				}
			}
		}
		catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
		{
		}
	}

	/// <summary>
	/// Takes one entry out of the pending set and gives its owner the quota slot back — the
	/// <c>add_to(executor, -1)</c> that PennMUSH pairs with every admission (<c>src/cque.c:1133</c>).
	/// </summary>
	/// <remarks>
	/// The <see cref="ConcurrentDictionary{TKey,TValue}.TryRemove(TKey,out TValue)"/> is what makes
	/// this exactly-once: an entry can reach here twice — cancelled by <see cref="Halt(DBRef)"/> and
	/// then read off the channel by the consumer — and only the first call releases anything.
	/// </remarks>
	private void ReleaseEntry(QueueEntry entry)
	{
		if (!_pendingEntries.TryRemove(entry.Pid, out _))
		{
			return;
		}

		if (entry.OwnerNumber is { } owner)
		{
			_pendingByOwner.AddOrUpdate(owner, 0, (_, current) => Math.Max(0, current - 1));
		}

		entry.Cts.Dispose();
	}

	/// <summary>
	/// Builds a queue entry, charges it to its owner's quota, and writes it to the immediate queue.
	/// This is the one admission point for that queue: PennMUSH's <c>insert_que</c>.
	/// </summary>
	/// <param name="executor">
	/// The object the entry runs as, or <see langword="null"/> for work owned by nobody, which is
	/// admitted unconditionally.
	/// </param>
	private async ValueTask Admit(
		long pid,
		DBRef? executor,
		string triggerName,
		string group,
		Func<ValueTask<CallState?>> action)
	{
		var owner = executor is null ? null : await OwnerOf(executor.Value);

		if (owner is not null && !await Charge(owner, executor!.Value))
		{
			return;
		}

		var entry = new QueueEntry(
			pid, executor, owner?.Object().DBRef.Number, triggerName, group, action, new CancellationTokenSource());
		_pendingEntries[pid] = entry;

		if (_immediateQueue.Writer.TryWrite(entry))
		{
			return;
		}

		ReleaseEntry(entry);
		logger.LogWarning(
			"Immediate queue is full at {Capacity} entries; dropped PID {Pid} in group {Group}",
			ImmediateQueueCapacity, pid, group);
	}

	/// <summary>
	/// PennMUSH <c>queue_limit</c> (<c>src/cque.c:226</c>): increment the owner's pending count, and
	/// refuse the entry if that count is now past the owner's quota.
	/// </summary>
	/// <remarks>
	/// The count is incremented before the quota is read, so concurrent admissions cannot both slip
	/// past the same free slot. Reading the quota costs a wizard-and-power lookup, so it is only read
	/// once the cheap configured floor has been passed — every owner's quota is at least that floor.
	/// </remarks>
	private async ValueTask<bool> Charge(AnySharpObject owner, DBRef offender)
	{
		var ownerNumber = owner.Object().DBRef.Number;
		var pending = _pendingByOwner.AddOrUpdate(ownerNumber, 1, (_, current) => current + 1);

		if (pending <= (int)options.CurrentValue.Limit.PlayerQueueLimit || pending <= await QuotaFor(owner))
		{
			return true;
		}

		_pendingByOwner.AddOrUpdate(ownerNumber, 0, (_, current) => Math.Max(0, current - 1));
		await HaltRunaway(offender, owner);
		return false;
	}

	/// <summary>
	/// How many entries this owner may hold. Wizards and holders of the <c>Queue</c> power get the
	/// configured limit plus the size of the database, as <c>help @queue</c> documents and
	/// <c>HugeQueue</c> (<c>hdrs/mushdb.h:36</c>) decides.
	/// </summary>
	private async ValueTask<int> QuotaFor(AnySharpObject owner)
	{
		var limit = (int)options.CurrentValue.Limit.PlayerQueueLimit;

		return await owner.IsWizard() || await owner.HasPower("QUEUE")
			? limit + await mediator.Send(new GetObjectCountQuery())
			: limit;
	}

	/// <summary>
	/// The object an entry is charged to. PennMUSH counts per owner when <c>QUEUE_PER_OWNER</c> is
	/// set (<c>src/cque.c:180</c>); SharpMUSH has no such switch and always counts per owner, so one
	/// player cannot multiply their allowance by spreading a loop over their objects.
	/// </summary>
	private async ValueTask<AnySharpObject?> OwnerOf(DBRef executor)
	{
		var node = await mediator.Send(new GetObjectNodeQuery(executor));

		if (node.IsNone)
		{
			return null;
		}

		// An object whose owner cannot be resolved is charged to nobody rather than to a guessed
		// stand-in: a wrong owner would spend someone else's allowance.
		var owner = await node.Known.Object().Owner.WithCancellation(CancellationToken.None);

		return owner is null ? null : new AnySharpObject(owner);
	}

	/// <summary>
	/// PennMUSH's "Runaway object" path (<c>src/cque.c:303-310</c>): tell the owner, log it, wipe the
	/// offender's queue and set it HALT. The refused entry is simply not queued.
	/// </summary>
	private async ValueTask HaltRunaway(DBRef offender, AnySharpObject owner)
	{
		var node = await mediator.Send(new GetObjectNodeQuery(offender));
		var name = node.IsNone ? offender.ToString() : node.Known.Object().Name;

		// The wipe has to happen: without it the backlog the object already built keeps running, each
		// entry freeing a slot the next one takes, and the quota alone never brings the loop to a stop.
		await Halt(offender);

		if (!node.IsNone)
		{
			// The @halt flag path (GeneralCommands.cs:2055): the flag is looked up by name, and a
			// database missing it is a seeding problem rather than something to invent a flag for.
			var haltFlag = await mediator.Send(new GetObjectFlagQuery("HALT"));

			if (haltFlag is not null)
			{
				await mediator.Send(new SetObjectFlagCommand(node.Known, haltFlag));
			}
		}

		// Penn notifies first and halts second, which it can afford because insert_que refuses to
		// queue anything for a halted object. SharpMUSH has no such gate, so the flag goes on before
		// the notification: a @listen or ^-pattern woken by that notification would otherwise be able
		// to queue as the offender again and land straight back here.
		await notifyService.NotifyLocalized(owner.Object().DBRef,
			nameof(ErrorMessages.Notifications.RunawayObjectFormat), name, offender.ToString());

		logger.LogWarning("Runaway object {Name} ({DbRef}) exceeded its queue quota; commands halted",
			name, offender);
	}

	public async ValueTask DrainImmediateQueueForTests(TimeSpan? timeout = null)
	{
		EnsureConsumerStarted();

		// An entry leaves _pendingEntries only after its action has finished, and anything that action
		// queued is in the dictionary before that removal, so an empty dictionary means the queue is
		// quiet — including work the drained entries themselves produced.
		var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));

		while (!_pendingEntries.IsEmpty)
		{
			if (DateTimeOffset.UtcNow >= deadline)
			{
				throw new TimeoutException(
					$"The immediate queue still held {_pendingEntries.Count} entries after the drain timeout.");
			}

			await Task.Delay(TimeSpan.FromMilliseconds(5));
		}
	}

	public ValueTask EnqueueWork(Func<ValueTask<CallState?>> action, string triggerName, string group,
		DBRef? executor = null)
	{
		EnsureConsumerStarted();
		return Admit(NextPid(), executor, triggerName, group, action);
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

	public ValueTask WriteUserCommand(long handle, MString command, ParserState state)
	{
		EnsureConsumerStarted();
		var pid = NextPid();

		return Admit(pid, state.Executor, $"handle:{handle}-{pid}", DirectInputGroup,
			async () =>
			{
				if (!string.IsNullOrEmpty(state.ConnectionSessionId) &&
					connectionService.Get(handle)?.Metadata.GetValueOrDefault("SessionId") != state.ConnectionSessionId) return null;
				return await parser.FromState(state).CommandParse(handle, connectionService, command);
			});
	}

	public ValueTask WriteCommandList(MString command, ParserState state)
	{
		EnsureConsumerStarted();
		var pid = NextPid();

		return Admit(pid, state.Executor, $"dbref:{state.Executor}-{pid}", EnqueueGroup,
			() => parser.FromState(state).CommandListParse(command));
	}

	public async ValueTask WriteCommandList(MString command, ParserState state, DbRefAttribute dbRefAttribute,
int oldValue)
	{
		if (oldValue < 0)
		{
			await WriteCommandList(command, state);
			return;
		}

		var triggerIdentity = $"dbref:{state.Executor}-{NextPid()}";
		var triggerGroup = $"{SemaphoreGroup}:{dbRefAttribute}";

		await _scheduler.ScheduleJob(
			JobBuilder
				.CreateForAsync<SemaphoreTask>()
				.SetJobData(new((IDictionary<string, object>)new Dictionary<string, object>
				{
					{ "Command", command },
					{ "State", state },
				}))
				.Build(),
			TriggerBuilder.Create()
				.WithSimpleSchedule(x => x.WithRepeatCount(0))
				.StartAt(DateTimeOffset.UtcNow.AddYears(100))  // Far future - will be triggered manually by @notify
				.WithIdentity(triggerIdentity, triggerGroup).Build());
	}

	public ValueTask WriteAsyncAttribute(Func<ValueTask<ParserState>> function,
		DbRefAttribute dbAttribute)
	{
		EnsureConsumerStarted();
		var pid = NextPid();

		// The state the closure produces is not available until the entry runs, so the attribute's own
		// object stands in as the executor: an attribute queued this way always runs as its holder.
		return Admit(pid, dbAttribute.DbRef, $"async:{dbAttribute}-{pid}", EnqueueGroup,
			async () =>
			{
				var parserState = await function();
				var executor = await parserState.KnownExecutorObject(mediator);
				var obj = await mediator.Send(new GetObjectNodeQuery(dbAttribute.DbRef));
				if (obj.IsNone) return new CallState("#-1");

				var attr = await attributeService.GetAttributeAsync(
					executor,
					obj.Known,
					string.Join('`', dbAttribute.Attribute),
					IAttributeService.AttributeMode.Execute);

				if (!attr.IsAttribute) return new CallState("#-1");

				return await parser.FromState(parserState).CommandListParse(attr.AsAttribute.Last().Value);
			});
	}

	public async ValueTask WriteCommandList(MString command, ParserState state, DbRefAttribute dbRefAttribute,
		int oldValue,
		TimeSpan timeout)
	{
		if (oldValue < 0)
		{
			await WriteCommandList(command, state);
			return;
		}

		await _scheduler.ScheduleJob(
			JobBuilder
				.CreateForAsync<SemaphoreTask>()
				.SetJobData(new JobDataMap((IDictionary<string, object>)new Dictionary<string, object>
				{
					{ "Command", command },
					{ "State", state },
				}))
				.Build(),
			TriggerBuilder.Create()
				.WithSimpleSchedule(x => x.WithRepeatCount(0))
				.StartAt(DateTimeOffset.Now + timeout)
				.WithIdentity(
					$"dbref:{state.Executor}-{NextPid()}",
					$"{SemaphoreGroup}:{dbRefAttribute}").Build());
	}

	public async ValueTask Notify(DbRefAttribute dbAttribute, int oldValue, int count = 1)
	{
		var groupKey = $"{SemaphoreGroup}:{dbAttribute}";

		var semaphoresForObject = await _scheduler
			.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals(groupKey));

		// Sort by PID to ensure FIFO ordering for semaphore notifications
		var sorted = semaphoresForObject
			.OrderBy(k =>
			{
				var parts = k.Name.Split('-');
				return parts.Length == 2 && long.TryParse(parts[1], out var pid) ? pid : long.MaxValue;
			});

		// If oldValue is negative, we notify the specified number of tasks
		// If oldValue is >= 0, we notify based on count
		var tasksToNotify = oldValue < 0 ? Math.Min(count, 0 - oldValue) : count;

		foreach (var triggerKey in sorted.Take(tasksToNotify))
		{
			try
			{
				var trigger = await _scheduler.GetTrigger(triggerKey, CancellationToken.None);
				if (trigger == null) continue;

				var job = await _scheduler.GetJobDetail(trigger.JobKey);
				if (job == null) continue;

				var command = job.JobDataMap.Get("Command") as MString;
				var state = job.JobDataMap.Get("State") as ParserState;

				await _scheduler.UnscheduleJob(triggerKey);
				await _scheduler.DeleteJob(trigger.JobKey);

				if (command != null && state != null)
				{
					await EnqueueWork(
						() => parser.FromState(state).CommandListParse(command),
						triggerKey.Name,
						triggerKey.Group,
						state.Executor);
				}
			}
			catch (Exception ex)
			{
				logger.LogWarning(ex, "Failed to notify semaphore task {TriggerKey}", triggerKey);
			}
		}
	}

	public async ValueTask NotifyAll(DbRefAttribute dbAttribute)
	{
		var semaphoresForObject = await _scheduler
			.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals($"{SemaphoreGroup}:{dbAttribute}"));

		foreach (var triggerKey in semaphoresForObject)
		{
			try
			{
				var trigger = await _scheduler.GetTrigger(triggerKey, CancellationToken.None);
				if (trigger == null) continue;

				var job = await _scheduler.GetJobDetail(trigger.JobKey);
				if (job == null) continue;

				var command = job.JobDataMap.Get("Command") as MString;
				var state = job.JobDataMap.Get("State") as ParserState;

				await _scheduler.UnscheduleJob(triggerKey);
				await _scheduler.DeleteJob(trigger.JobKey);

				if (command != null && state != null)
				{
					await EnqueueWork(
						() => parser.FromState(state).CommandListParse(command),
						triggerKey.Name,
						triggerKey.Group,
						state.Executor);
				}
			}
			catch (Exception ex)
			{
				logger.LogWarning(ex, "Failed to notify semaphore task {TriggerKey}", triggerKey);
			}
		}
	}

	public async ValueTask<bool> ModifyQRegisters(DbRefAttribute dbAttribute, Dictionary<string, MString> qRegisters)
	{
		if (qRegisters == null || qRegisters.Count == 0)
		{
			return false;
		}

		var semaphoresForObject = await _scheduler
			.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals($"{SemaphoreGroup}:{dbAttribute}"));

		var firstTrigger = semaphoresForObject.FirstOrDefault();
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
	{
		var semaphoresForObject = await _scheduler
			.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals($"{SemaphoreGroup}:{dbAttribute}"));

		if (count.HasValue)
		{
			var tasksToDrain = semaphoresForObject.Take(count.Value).ToList();
			await _scheduler.UnscheduleJobs(tasksToDrain);
		}
		else
		{
			await _scheduler.UnscheduleJobs(semaphoresForObject);
		}
	}

	public async ValueTask Halt(DBRef dbRef)
	{
		var delayed = await _scheduler
			.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupStartsWith($"{DelayGroup}:{dbRef}"));
		await _scheduler.UnscheduleJobs(delayed);

		foreach (var entry in _pendingEntries.Values.Where(e => IsQueuedFor(e, dbRef)))
		{
			entry.Cts.Cancel();
			ReleaseEntry(entry);
		}
	}

	/// <summary>
	/// Whether an immediate-queue entry belongs to <paramref name="dbRef"/>'s object queue, which is
	/// what <c>@halt &lt;object&gt;</c> and <c>@ps</c> ask about. The entry's executor answers this
	/// directly; the trigger name does not, because an attribute queued by
	/// <see cref="WriteAsyncAttribute"/> is named after its attribute rather than its object.
	/// Direct player input is deliberately excluded, as it is in PennMUSH.
	/// </summary>
	private static bool IsQueuedFor(QueueEntry entry, DBRef dbRef)
		=> entry.Group == EnqueueGroup && entry.Executor?.Number == dbRef.Number;

	public async ValueTask<bool> HaltByPid(long pid)
	{
		if (_pendingEntries.TryGetValue(pid, out var entry))
		{
			entry.Cts.Cancel();
			ReleaseEntry(entry);
			return true;
		}

		var allKeys = await _scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.AnyGroup());
		var pidString = $"-{pid}";

		var matchingKeys = allKeys.Where(key => key.Name.EndsWith(pidString)).ToList();

		if (matchingKeys.Count == 0)
			return false;

		await _scheduler.UnscheduleJobs(matchingKeys);
		return true;
	}

	public async ValueTask WriteCommandList(MString command, ParserState state, TimeSpan delay)
	{
		var pid = NextPid();
		await _scheduler.ScheduleJob(
			async () => await EnqueueWork(
				() => parser.FromState(state).CommandListParse(command),
				$"dbref:{state.Executor}-{pid}",
				EnqueueGroup,
				state.Executor),
			builder => builder
				.StartAt(DateTimeOffset.UtcNow + delay)
				.WithSimpleSchedule(x => x.WithRepeatCount(0))
				.WithIdentity($"dbref:{state.Executor}-{pid}", $"{DelayGroup}:{state.Executor}"));
	}

	public async IAsyncEnumerable<(string Group, (DateTimeOffset, OneOf<string, DBRef>)[])> GetAllTasks()
	{
		var translate = new Func<string, string>(x =>
			new string(x.Replace("dbref:", string.Empty).Replace("handle:", string.Empty)
				.TakeWhile(c => c != '-').ToArray()));

		var keys = await _scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.AnyGroup());
		var keyTriggers = keys.ToAsyncEnumerable()
			.Select<TriggerKey, ITrigger>(async (triggerKey, ct) => await _scheduler.GetTrigger(triggerKey, ct))
			.GroupBy(trigger => trigger.JobKey.Group, trigger => (trigger.FinalFireTimeUtc!.Value, trigger.Key.Name));
		await foreach (var key in keyTriggers)
		{
			yield return (key.Key, key.Select(x => (
				x.Value,
				DBRef.TryParse(translate(x.Name), out var dbref)
					? OneOf<string, DBRef>.FromT1(dbref!.Value)
					: OneOf<string, DBRef>.FromT0(x.Name)
			)).ToArray());
		}

		foreach (var group in _pendingEntries.Values.GroupBy(e => e.Group))
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
		var keys = await _scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupStartsWith($"{SemaphoreGroup}:#{obj.Number}"));
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
		return _pendingEntries.Values
			.Where(entry => IsQueuedFor(entry, obj))
			.Select(entry => entry.Pid)
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
		var semaphoreSourceString = string.Join(':', trigger.JobKey.Group.Split(':').Skip(1));
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
		_immediateQueue.Writer.TryComplete();
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
		_shutdownCts.Dispose();
		GC.SuppressFinalize(this);
	}
}