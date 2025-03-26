using SharpMUSH.Library.ParserInterfaces;
using Quartz;
using Quartz.Lambda;

namespace SharpMUSH.Library.Services;

/// <summary>
/// IMushCodeParser is a circular dependency here, so we can't use it!
/// Either we use a Mediator here, or make the TaskScheduler itself a Mediator target for the Parser.
/// </summary>
public class TaskScheduler(IMUSHCodeParser parser, ISchedulerFactory schedulerFactory) : ITaskScheduler
{
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

	public async ValueTask WriteUserCommand(string handle, MString command, ParserState? state)
	{
		var scheduler = await schedulerFactory.GetScheduler();
		if (state is null)
		{
			await scheduler.ScheduleJob(() => parser.Empty().CommandParse(handle, command).AsTask(),
				builder => builder.StartNow().WithSimpleSchedule(x => x.WithRepeatCount(0)));
			return;
		}

		await scheduler.ScheduleJob(() => parser.FromState(state).CommandParse(handle, command).AsTask(),
			builder => builder.StartNow().WithSimpleSchedule(x => x.WithRepeatCount(0)));
	}

	public async ValueTask WriteCommand(MString command, ParserState? state)
	{
		var scheduler = await schedulerFactory.GetScheduler();
		if (state is null)
		{
			await scheduler.ScheduleJob(() => parser.Empty().CommandParse(command).AsTask(),
				builder => builder.StartNow().WithSimpleSchedule(x => x.WithRepeatCount(0)));
			return;
		}

		await scheduler.ScheduleJob(() => parser.FromState(state).CommandParse(command).AsTask(),
			builder => builder.StartNow().WithSimpleSchedule(x => x.WithRepeatCount(0)));
	}

	public async ValueTask WriteCommandList(MString command, ParserState? state)
	{
		var scheduler = await schedulerFactory.GetScheduler();
		if (state is null)
		{
			await scheduler.ScheduleJob(() => parser.Empty().CommandListParse(command).AsTask(),
				builder => builder.StartNow().WithSimpleSchedule(x => x.WithRepeatCount(0)));
			return;
		}

		await scheduler.ScheduleJob(() => parser.FromState(state).CommandListParse(command).AsTask(),
			builder => builder.StartNow().WithSimpleSchedule(x => x.WithRepeatCount(0)));

		return;
	}

	public ValueTask WriteCommandList(MString command, ParserState? state, SemaphoreSlim semaphore)
	{
		throw new NotImplementedException();
	}

	public async ValueTask WriteCommandList(MString command, ParserState? state, TimeSpan time)
	{
		var scheduler = await schedulerFactory.GetScheduler();
		if (state is null)
		{
			await scheduler.ScheduleJob(() => parser.Empty().CommandListParse(command).AsTask(),
				builder => builder.StartAt(DateTimeOffset.UtcNow + time).WithSimpleSchedule(x => x.WithRepeatCount(0)));
			return;
		}

		await scheduler.ScheduleJob(() => parser.FromState(state).CommandListParse(command).AsTask(),
			builder => builder.StartAt(DateTimeOffset.UtcNow + time).WithSimpleSchedule(x => x.WithRepeatCount(0)));
	}
}