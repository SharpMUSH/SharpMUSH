using System.Collections.Immutable;
using SharpMUSH.Library.ParserInterfaces;
using Quartz;
using Quartz.Impl.Matchers;
using Quartz.Lambda;
using Quartz.Util;
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

	public async ValueTask WriteCommandList(MString command, ParserState state, DbRefAttribute dbRefAttribute,
		int oldValue)
	{
		await _scheduler.ScheduleJob(() => parser.FromState(state).CommandListParse(command).AsTask(),
			builder => builder
				.StartNow()
				.WithSimpleSchedule(x => x.WithRepeatCount(0))
				.WithIdentity($"dbref:{state.Executor}-{Guid.NewGuid()}", SemaphoreGroup));
	}

	public async ValueTask WriteCommandList(MString command, ParserState state, DbRefAttribute dbRefAttribute,
		int oldValue,
		TimeSpan timeout)
	{
		await _scheduler.ScheduleJob(() => parser.FromState(state).CommandListParse(command).AsTask(),
			builder => builder
				.StartNow()
				.WithSimpleSchedule(x => x.WithRepeatCount(0))
				.WithIdentity($"dbref:{state.Executor}-{Guid.NewGuid()}", SemaphoreGroup));
	}

	public async ValueTask Notify(DbRefAttribute dbAttribute, int oldValue)
	{
		await ValueTask.CompletedTask;
	}

	public async ValueTask Drain(DbRefAttribute dbAttribute)
	{
		await ValueTask.CompletedTask;
	}

	public async ValueTask WriteCommandList(MString command, ParserState state, TimeSpan delay) =>
		await _scheduler.ScheduleJob(() => parser.FromState(state).CommandListParse(command).AsTask(),
			builder => builder
				.StartAt(DateTimeOffset.UtcNow + delay)
				.WithSimpleSchedule(x => x.WithRepeatCount(0))
				.WithIdentity($"dbref:{state.Executor}-{Guid.NewGuid()}", DelayGroup));

	public async IAsyncEnumerable<(string Group, (DateTimeOffset, OneOf.OneOf<string, DBRef>)[])> GetAllTasks()
	{
		var keys = await _scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.AnyGroup());
		var keyTriggers = keys.ToAsyncEnumerable()
			.SelectAwait(async triggerKey => await _scheduler.GetTrigger(triggerKey))
			.GroupBy(trigger => trigger.JobKey.Group, trigger => (trigger.FinalFireTimeUtc!.Value, trigger.Key.Name));
		await foreach (var key in keyTriggers)
		{
			var translate = new Func<string, string>(x =>
				x.Replace("dbref:", "").Replace("handle:", "").TakeWhile(x => x != '-').ToString()!);

			yield return (key.Key, await key.Select(x => (
				x.Value,
				DBRef.TryParse(translate(x.Name), out var dbref)
					? OneOf.OneOf<string, DBRef>.FromT1(dbref!.Value)
					: OneOf.OneOf<string, DBRef>.FromT0(x.Name)
			)).ToArrayAsync());
		}
	}

	public async IAsyncEnumerable<(string Group, DateTimeOffset[])> GetTasks(string handle)
	{
		var keys = await _scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.AnyGroup());
		var keyTriggers = keys.ToAsyncEnumerable()
			.Where(x => x.Name.StartsWith($"handle:{handle}"))
			.SelectAwait(async triggerKey => await _scheduler.GetTrigger(triggerKey))
			.GroupBy(trigger => trigger.JobKey.Group, trigger => trigger.FinalFireTimeUtc!.Value);
		await foreach (var key in keyTriggers)
		{
			yield return (key.Key, await key.ToArrayAsync());
		}
	}

	public async IAsyncEnumerable<(string Group, DateTimeOffset[])> GetTasks(DBRef obj)
	{
		var keys = await _scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.AnyGroup());
		var keyTriggers = keys.ToAsyncEnumerable()
			.Where(triggerKey => triggerKey.Name.StartsWith($"dbref:{obj}"))
			.SelectAwait(async triggerKey => await _scheduler.GetTrigger(triggerKey))
			.GroupBy(trigger => trigger.JobKey.Group, trigger => trigger.FinalFireTimeUtc!.Value);
		await foreach (var key in keyTriggers)
		{
			yield return (key.Key, await key.ToArrayAsync());
		}
	}
}