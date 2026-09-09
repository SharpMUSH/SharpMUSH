using SharpMUSH.Library.Models.SchedulerModels;

namespace SharpMUSH.Library.Services;

public partial class TaskScheduler
{
	public IReadOnlyList<QueueEntrySnapshot> GetQueueEntries() => [];
	public ValueTask<QueueControlResult> PausePending(long pid, string reason)
		=> ValueTask.FromResult(QueueControlResult.NotPending);
	public ValueTask<QueueControlResult> ResumePending(long pid)
		=> ValueTask.FromResult(QueueControlResult.NotFound);
}
