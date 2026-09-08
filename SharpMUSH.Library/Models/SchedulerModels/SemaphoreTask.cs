using Quartz;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Models.SchedulerModels;

internal class SemaphoreTask(ITaskScheduler taskScheduler) : IJob
{
	public async Task Execute(IJobExecutionContext context)
	{
		await context.Scheduler.UnscheduleJob(context.Trigger.Key);
		await context.Scheduler.DeleteJob(context.JobDetail.Key);
		await taskScheduler.ReleaseScheduledWork(long.Parse(context.Trigger.Key.Name.Split('-').Last()), semaphoreTimeout: true);
	}
}
