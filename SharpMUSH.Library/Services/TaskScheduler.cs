using Mediator;
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
	ILogger<TaskScheduler> logger) : ITaskScheduler, IAsyncDisposable
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
		CancellationTokenSource Cts
	);

	private readonly Channel<QueueEntry> _immediateQueue = Channel.CreateUnbounded<QueueEntry>(
		new UnboundedChannelOptions { SingleReader = true });
	private readonly ConcurrentDictionary<long, QueueEntry> _pendingEntries = new();
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
					_pendingEntries.TryRemove(entry.Pid, out _);
					entry.Cts.Dispose();
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
					_pendingEntries.TryRemove(entry.Pid, out _);
					entry.Cts.Dispose();
				}
			}
		}
		catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
		{
			// Graceful shutdown
		}
	}

	public ValueTask EnqueueWork(Func<ValueTask<CallState?>> action, string triggerName, string group)
	{
		EnsureConsumerStarted();
		var pid = NextPid();
		var entry = new QueueEntry(pid, triggerName, group, action, new CancellationTokenSource());
		_pendingEntries[pid] = entry;
		_immediateQueue.Writer.TryWrite(entry);
		return ValueTask.CompletedTask;
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
		var entry = new QueueEntry(
			pid,
			$"handle:{handle}-{pid}",
			DirectInputGroup,
			async() => await parser.FromState(state).CommandParse(handle, connectionService, command),
			new CancellationTokenSource()
		);
		_pendingEntries[pid] = entry;
		_immediateQueue.Writer.TryWrite(entry);
		return ValueTask.CompletedTask;
	}

	public ValueTask WriteCommandList(MString command, ParserState state)
	{
		EnsureConsumerStarted();
		var pid = NextPid();
		var entry = new QueueEntry(
			pid,
			$"dbref:{state.Executor}-{pid}",
			EnqueueGroup,
			() => parser.FromState(state).CommandListParse(command),
			new CancellationTokenSource()
		);
		_pendingEntries[pid] = entry;
		_immediateQueue.Writer.TryWrite(entry);
		return ValueTask.CompletedTask;
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

		// DIAGNOSTIC: Log the group key being used for job creation

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
		var entry = new QueueEntry(
			pid,
			$"async:{dbAttribute}-{pid}",
			EnqueueGroup,
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
			},
			new CancellationTokenSource()
		);
		_pendingEntries[pid] = entry;
		_immediateQueue.Writer.TryWrite(entry);
		return ValueTask.CompletedTask;
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

		// DIAGNOSTIC: Log the group key being used for notification lookup

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
						triggerKey.Group);
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
						triggerKey.Group);
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

		// Get the first waiting task
		var firstTrigger = semaphoresForObject.FirstOrDefault();
		if (firstTrigger == null)
		{
			return false; // No tasks waiting
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

			// Get the current ParserState
			if (!data.TryGetValue("State", out var stateObj) || stateObj is not ParserState state)
			{
				return false;
			}

			// Modify the Q-registers in the state
			// We need to get the top dictionary from the Registers stack and add/update the Q-registers
			if (state.Registers.TryPeek(out var registers))
			{
				foreach (var qreg in qRegisters)
				{
					registers[qreg.Key.ToUpper()] = qreg.Value;
				}
			}
			else
			{
				// If no registers stack exists, create one
				var newRegisters = new Dictionary<string, MString>();
				foreach (var qreg in qRegisters)
				{
					newRegisters[qreg.Key.ToUpper()] = qreg.Value;
				}
				state.Registers.Push(newRegisters);
			}

			// Update the job data with the modified state
			data["State"] = state;

			// Need to re-add the job to persist the changes
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
			// Drain only the specified number of tasks
			var tasksToDrain = semaphoresForObject.Take(count.Value).ToList();
			await _scheduler.UnscheduleJobs(tasksToDrain);
		}
		else
		{
			// Drain all tasks
			await _scheduler.UnscheduleJobs(semaphoresForObject);
		}
	}

	public async ValueTask Halt(DBRef dbRef)
	{
		var delayed = await _scheduler
			.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupStartsWith($"{DelayGroup}:{dbRef}"));
		await _scheduler.UnscheduleJobs(delayed);

		// Cancel pending channel entries for this dbref
		var dbRefPrefix = $"dbref:{dbRef}-";
		foreach (var kvp in _pendingEntries)
		{
			if (kvp.Value.TriggerName.StartsWith(dbRefPrefix) && kvp.Value.Group == EnqueueGroup)
			{
				kvp.Value.Cts.Cancel();
				_pendingEntries.TryRemove(kvp.Key, out _);
			}
		}
	}

	public async ValueTask<bool> HaltByPid(long pid)
	{
		// Check channel entries first
		if (_pendingEntries.TryRemove(pid, out var entry))
		{
			entry.Cts.Cancel();
			return true;
		}

		// Fall back to Quartz for semaphore/delay tasks
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
				EnqueueGroup),
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

		// Quartz tasks (semaphore, delay, scheduled)
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

		// Channel tasks (enqueue, direct-input)
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

	public async IAsyncEnumerable<long> GetEnqueueTasks(DBRef obj)
	{
		await Task.CompletedTask;
		var dbRefPrefix = $"dbref:{obj}-";
		foreach (var kvp in _pendingEntries)
		{
			if (kvp.Value.TriggerName.StartsWith(dbRefPrefix) && kvp.Value.Group == EnqueueGroup)
			{
				yield return kvp.Key;
			}
		}
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