using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>Stops queued work: <c>@halt</c> by object or by pid, and pausing or resuming one pending entry.</summary>
public interface ITaskQueueControl
{
	/// <summary>
	/// Halts queued jobs related to a DBRef, including semaphore waits, and excluding the lines it
	/// has already typed — <c>do_halt</c> walks the run, wait and semaphore queues, and typed input
	/// is on none of them (<c>src/cque.c:1076-1090</c>, <c>:2179-2218</c>).
	/// </summary>
	/// <param name="dbRef">DbRef</param>
	ValueTask Halt(DBRef dbRef);

	/// <summary>
	/// Halts a specific task by PID.
	/// </summary>
	/// <param name="pid">Process ID</param>
	/// <returns>True if task was found and halted, false otherwise</returns>
	ValueTask<bool> HaltByPid(long pid);

	ValueTask<QueueControlResult> PausePending(long pid, string reason);

	ValueTask<QueueControlResult> ResumePending(long pid);
}
