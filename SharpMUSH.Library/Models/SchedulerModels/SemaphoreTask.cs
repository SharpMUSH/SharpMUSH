using Quartz;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Models.SchedulerModels;

internal class SemaphoreTask(IMUSHCodeParser parser, ITaskScheduler taskScheduler) : IJob
{
	public async Task Execute(IJobExecutionContext context)
	{
		var state = context.MergedJobDataMap.Get("State") as ParserState;
		var command = context.MergedJobDataMap.Get("Command") as MString;

		await context.Scheduler.UnscheduleJob(context.Trigger.Key);
		await context.Scheduler.DeleteJob(context.JobDetail.Key);

		if (state != null && command != null)
		{
			await taskScheduler.EnqueueWork(
				() => parser.FromState(state).CommandListParse(command),
				context.Trigger.Key.Name,
				context.Trigger.Key.Group);
		}
	}
}