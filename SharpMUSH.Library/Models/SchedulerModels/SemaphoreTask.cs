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
			// The executor is what charges the work to an owner's queue quota and what makes it visible
			// to @halt, @ps and the runaway halt. PennMUSH charges a semaphore wait to its executor when
			// wait_que builds it (pay_queue, src/cque.c:904) and dequeue_semaphores moves that same
			// entry onto the run queue with its executor intact (src/cque.c:1379-1427), so a released
			// semaphore command is charged exactly like any other queued one.
			await taskScheduler.EnqueueWork(
				() => parser.FromState(state).CommandListParse(command),
				context.Trigger.Key.Name,
				context.Trigger.Key.Group,
				state.Executor);
		}
	}
}