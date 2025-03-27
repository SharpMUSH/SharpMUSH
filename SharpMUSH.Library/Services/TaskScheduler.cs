using System.Collections.Immutable;
using SharpMUSH.Library.ParserInterfaces;
using Quartz;
using Quartz.Impl.Matchers;
using Quartz.Lambda;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Task scheduler, schedules items onto the queue.
/// </summary>
/// <param name="parser"></param>
/// <param name="schedulerFactory"></param>
public class TaskScheduler(IMUSHCodeParser parser, ISchedulerFactory schedulerFactory) : ITaskScheduler
{
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

	public async ValueTask WriteUserCommand(string handle, MString command, ParserState state) =>
		await _scheduler.ScheduleJob(() => parser.FromState(state).CommandParse(handle, command).AsTask(),
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

	public async ValueTask WriteCommandList(MString command, ParserState state, SemaphoreSlim semaphore) =>
		await _scheduler.ScheduleJob(() => parser.FromState(state).CommandListParse(command).AsTask(),
			builder => builder
				.StartNow()
				.WithSimpleSchedule(x => x.WithRepeatCount(0))
				.WithIdentity($"dbref:{state.Executor}-{Guid.NewGuid()}", SemaphoreGroup));

	public async ValueTask WriteCommandList(MString command, ParserState state, SemaphoreSlim semaphore,
		TimeSpan timeout) =>
		await _scheduler.ScheduleJob(() => parser.FromState(state).CommandListParse(command).AsTask(),
			builder => builder
				.StartNow()
				.WithSimpleSchedule(x => x.WithRepeatCount(0))
				.WithIdentity($"dbref:{state.Executor}-{Guid.NewGuid()}", SemaphoreGroup));

	public async ValueTask WriteCommandList(MString command, ParserState state, TimeSpan delay) =>
		await _scheduler.ScheduleJob(() => parser.FromState(state).CommandListParse(command).AsTask(),
			builder => builder
				.StartAt(DateTimeOffset.UtcNow + delay)
				.WithSimpleSchedule(x => x.WithRepeatCount(0))
				.WithIdentity($"dbref:{state.Executor}-{Guid.NewGuid()}", DelayGroup));

	public async IAsyncEnumerable<(string,IAsyncEnumerable<DateTimeOffset>)> GetAllTasks()
	{
		var keys = await _scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.AnyGroup());
		var keyTriggers = keys.ToAsyncEnumerable()
			.SelectAwait(async triggerKey => await _scheduler.GetTrigger(triggerKey))
			.GroupBy(trigger => trigger.JobKey.Group, trigger => trigger.FinalFireTimeUtc!.Value);
		await foreach (var key in keyTriggers)
		{
			yield return (key.Key, key);
		}
	}

	public async IAsyncEnumerable<(string,IAsyncEnumerable<DateTimeOffset>)> GetTasks(string handle)
	{
		var keys = await _scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.AnyGroup());
		var keyTriggers = keys.ToAsyncEnumerable()
			.Where(x => x.Name.StartsWith($"handle:{handle}"))
			.SelectAwait(async triggerKey => await _scheduler.GetTrigger(triggerKey))
			.GroupBy(trigger => trigger.JobKey.Group, trigger => trigger.FinalFireTimeUtc!.Value);
		await foreach (var key in keyTriggers)
		{
			yield return (key.Key, key);
		}
	}

	public async IAsyncEnumerable<(string,IAsyncEnumerable<DateTimeOffset>)> GetTasks(DBRef obj) 
	{
		var keys = await _scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.AnyGroup());
		var keyTriggers = keys.ToAsyncEnumerable()
			.Where(triggerKey => triggerKey.Name.StartsWith($"dbref:{obj}"))
			.SelectAwait(async triggerKey => await _scheduler.GetTrigger(triggerKey))
			.GroupBy(trigger => trigger.JobKey.Group, trigger => trigger.FinalFireTimeUtc!.Value);
		await foreach (var key in keyTriggers)
		{
			yield return (key.Key, key);
		}
	}
}