using Quartz;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Models.SchedulerModels;

internal class SemaphoreTask(ITaskScheduler taskScheduler) : IJob
{
	public async Task Execute(IJobExecutionContext context)
	{
		var generation = context.MergedJobDataMap.GetLong("Generation");
		await taskScheduler.ReleaseScheduledWork(long.Parse(context.Trigger.Key.Name.Split('-').Last()), semaphoreTimeout: true, generation: generation);
	}
}
