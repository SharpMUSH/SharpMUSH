using Quartz;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Models.SchedulerModels;

internal class SemaphoreTask(IMUSHCodeParser parser) : IJob
{
	public async Task Execute(IJobExecutionContext context)
	{
		var state = context.MergedJobDataMap.Get("State") as ParserState;
		var command = context.MergedJobDataMap.Get("Command") as MString;

		await context.Scheduler.UnscheduleJob(context.Trigger.Key);
		await parser.FromState(state!).CommandListParse(command!);
		await context.Scheduler.DeleteJob(context.JobDetail.Key);
	}
}