using Quartz;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Models.SchedulerModels;

internal class SemaphoreTask(ITaskScheduler taskScheduler) : IJob
{
	public async Task Execute(IJobExecutionContext context)
	{
		try
		{
			await taskScheduler.ReleaseScheduledWork(long.Parse(context.Trigger.Key.Name.Split('-').Last()), semaphoreTimeout: true);
		}
		catch (Exception exception) when (!context.CancellationToken.IsCancellationRequested)
		{
			// Keep Quartz's current job alive until its durable counter update succeeds.
			// Back off before requesting refire so an unavailable store cannot cause a hot loop.
			await Task.Delay(TimeSpan.FromSeconds(1), context.CancellationToken);
			throw new JobExecutionException(exception, refireImmediately: true);
		}
		await context.Scheduler.UnscheduleJob(context.Trigger.Key);
		await context.Scheduler.DeleteJob(context.JobDetail.Key);
	}
}
