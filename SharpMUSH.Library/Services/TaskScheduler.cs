using OneOf;
using Quartz;
using Quartz.Impl.Matchers;
using Quartz.Lambda;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

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
public class TaskScheduler(IMUSHCodeParser parser, IConnectionService connectionService, ISchedulerFactory schedulerFactory) : ITaskScheduler
{
	private long _nextPid = 0;
	private long NextPid() => Interlocked.Increment(ref _nextPid);
	
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

	public async ValueTask WriteUserCommand(long handle, MString command, ParserState state) =>
		await _scheduler.ScheduleJob(() => parser.FromState(state).CommandParse(handle, connectionService, command).AsTask(),
			builder => builder
				.StartNow()
				.WithSimpleSchedule(x => x.WithRepeatCount(0))
				.WithIdentity($"handle:{handle}-{Guid.NewGuid()}", DirectInputGroup)
		);

	public async ValueTask WriteCommandList(MString command, ParserState state) =>
		await _scheduler.ScheduleJob(() => parser.FromState(state).CommandListParse(command).AsTask(),
			builder => builder
				.StartNow()
				.WithSimpleSchedule(x => x.WithRepeatCount(0))
				.WithIdentity($"dbref:{state.Executor}-{Guid.NewGuid()}", EnqueueGroup)
		);

	public async ValueTask WriteCommandList(MString command, ParserState state, DbRefAttribute dbRefAttribute,
		int oldValue)
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
				.WithIdentity(
					$"dbref:{state.Executor}-{NextPid()}",
					$"{SemaphoreGroup}:{dbRefAttribute}").Build());
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

	public async ValueTask Notify(DbRefAttribute dbAttribute, int oldValue)
	{
		var semaphoresForObject = await _scheduler
			.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals($"{SemaphoreGroup}:{dbAttribute}"));

		if (oldValue < 0)
		{
			var immediatelyRun = semaphoresForObject.Take(0 - oldValue).ToAsyncEnumerable();
			await foreach (var trigger in immediatelyRun)
			{
				try
				{
					var to = await _scheduler.GetTrigger(trigger, CancellationToken.None);
					var job = to.JobKey;
					await _scheduler.TriggerJob(job);
				}
				catch
				{
					// Intentionally do nothing for that job. It likely no longer exists somehow.
				}
			}
		}
	}

	public async ValueTask NotifyAll(DbRefAttribute dbAttribute)
	{
		var semaphoresForObject = await _scheduler
			.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals($"{SemaphoreGroup}:{dbAttribute}"));

		var immediatelyRun = semaphoresForObject.ToAsyncEnumerable();
		await foreach (var trigger in immediatelyRun)
		{
			try
			{
				var to = await _scheduler.GetTrigger(trigger, CancellationToken.None);
				var job = to.JobKey;
				await _scheduler.TriggerJob(job);
			}
			catch
			{
				// Intentionally do nothing for that job. It likely no longer exists somehow.
			}
		}
	}

	public async ValueTask Drain(DbRefAttribute dbAttribute)
	{
		var semaphoresForObject = await _scheduler
			.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals($"{SemaphoreGroup}:{dbAttribute}"));

		await _scheduler.UnscheduleJobs(semaphoresForObject);
	}

	public async ValueTask Halt(DBRef dbRef)
	{
		var delayed = await _scheduler
			.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupStartsWith($"{DelayGroup}:{dbRef}"));
		await _scheduler.UnscheduleJobs(delayed);

		var enqueued = await _scheduler
			.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupStartsWith($"{EnqueueGroup}:{dbRef}"));
		await _scheduler.UnscheduleJobs(enqueued);
	}

	public async ValueTask WriteCommandList(MString command, ParserState state, TimeSpan delay) =>
		await _scheduler.ScheduleJob(() => parser.FromState(state).CommandListParse(command).AsTask(),
			builder => builder
				.StartAt(DateTimeOffset.UtcNow + delay)
				.WithSimpleSchedule(x => x.WithRepeatCount(0))
				.WithIdentity($"dbref:{state.Executor}-{NextPid()}", $"{DelayGroup}:{state.Executor}"));

	public async IAsyncEnumerable<(string Group, (DateTimeOffset, OneOf<string, DBRef>)[])> GetAllTasks()
	{
		var keys = await _scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.AnyGroup());
		var keyTriggers = keys.ToAsyncEnumerable()
			.Select<TriggerKey, ITrigger>(async (triggerKey, ct) => await _scheduler.GetTrigger(triggerKey, ct))
			.GroupBy(trigger => trigger.JobKey.Group, trigger => (trigger.FinalFireTimeUtc!.Value, trigger.Key.Name));
		await foreach (var key in keyTriggers)
		{
			var translate = new Func<string, string>(x =>
				x.Replace("dbref:", "").Replace("handle:", "")
					.TakeWhile(c => c != '-').ToString()!);

			yield return (key.Key, key.Select(x => (
				x.Value,
				DBRef.TryParse(translate(x.Name), out var dbref)
					? OneOf<string, DBRef>.FromT1(dbref!.Value)
					: OneOf<string, DBRef>.FromT0(x.Name)
			)).ToArray());
		}
	}

	public async IAsyncEnumerable<SemaphoreTaskData> GetSemaphoreTasks(DBRef obj)
	{
		var keys = await _scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupStartsWith($"{SemaphoreGroup}:{obj}"));
		var keyTriggers = keys.ToAsyncEnumerable()
			 .Select<TriggerKey, SemaphoreTaskData>(async (triggerKey,_) => await MapSemaphoreTaskData(_scheduler, triggerKey));
		
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
			.Select<TriggerKey, SemaphoreTaskData>(async (triggerKey,_) => await MapSemaphoreTaskData(_scheduler, triggerKey));
		
		await foreach (var key in keyTriggers)
		{
			yield return key;
		}
	}
	
	public async IAsyncEnumerable<SemaphoreTaskData> GetSemaphoreTasks(DbRefAttribute objAttribute)
	{
		var keys = await _scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals($"{SemaphoreGroup}:{objAttribute}"));
		var keyTriggers = keys.ToAsyncEnumerable()
			.Select<TriggerKey, SemaphoreTaskData>(async (triggerKey,_) => await MapSemaphoreTaskData(_scheduler, triggerKey));
		
		await foreach (var key in keyTriggers)
		{
			yield return key;
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
}