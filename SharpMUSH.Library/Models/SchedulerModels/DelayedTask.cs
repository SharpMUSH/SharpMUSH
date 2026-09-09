using Quartz;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Models.SchedulerModels;

internal class DelayedTask(ITaskScheduler taskScheduler, IOptionsWrapper<SharpMUSHOptions>? configuration = null) : IJob
{
	public async Task Execute(IJobExecutionContext context)
	{
		try
		{
			var milliseconds = configuration?.CurrentValue.Limit.QueueEntryCpuTime ?? 1000;
			using var budget = ExecutionBudget.FromMilliseconds(milliseconds == 0 ? 1000 : milliseconds, context.CancellationToken);
			using var scope = budget.Enter();
			await taskScheduler.ReleaseScheduledWork(long.Parse(context.Trigger.Key.Name.Split('-').Last()),
				semaphoreTimeout: false, generation: context.MergedJobDataMap.GetLong("Generation"));
		}
		catch (Exception exception) when (!context.CancellationToken.IsCancellationRequested)
		{
			// Keep the admitted timer retryable if a deferred transition cannot finish yet.
			await Task.Delay(TimeSpan.FromSeconds(1), context.CancellationToken);
			throw new JobExecutionException(exception, refireImmediately: true);
		}
	}
}
