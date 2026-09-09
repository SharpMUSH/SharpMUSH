using Quartz;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Models.SchedulerModels;

internal class SemaphoreTask(ITaskScheduler taskScheduler) : IJob
{
	public async Task Execute(IJobExecutionContext context)
	{
		var generation = context.MergedJobDataMap.GetLong("Generation");
		try
		{
			await taskScheduler.ReleaseScheduledWork(long.Parse(context.Trigger.Key.Name.Split('-').Last()), semaphoreTimeout: true, generation: generation);
		}
		catch (Exception exception) when (!context.CancellationToken.IsCancellationRequested)
		{
			// The ledger removes only the current generation after accounting succeeds.
			// Retain this Quartz job for retry without deleting a replacement timer.
			await Task.Delay(TimeSpan.FromSeconds(1), context.CancellationToken);
			throw new JobExecutionException(exception, refireImmediately: true);
		}
	}
}
