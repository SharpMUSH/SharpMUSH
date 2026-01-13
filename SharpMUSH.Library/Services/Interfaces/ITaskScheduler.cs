using OneOf;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

public interface ITaskScheduler
{
	/// <summary>
	/// Write a user command to the scheduler, to be immediately executed when the scheduler runs.
	/// </summary>
	/// <param name="handle">The identifier for the handle to send to.</param>
	/// <param name="command">The command to run.</param>
	/// <param name="state">A ParserState to ensure valid parsing.</param>
	ValueTask WriteUserCommand(long handle, MString command, ParserState state);

	/// <summary>
	/// Write a command-list to the scheduler, to be immediately executed when the scheduler runs.
	/// </summary>
	/// <param name="command">The command to run.</param>
	/// <param name="state">A ParserState to ensure valid parsing.</param>
	ValueTask WriteCommandList(MString command, ParserState state);

	/// <summary>
	/// Write a command-list to the scheduler on semaphore, to be immediately executed when the scheduler runs.
	/// </summary>
	/// <param name="command">The command to run.</param>
	/// <param name="state">A ParserState to ensure valid parsing.</param>
	/// <param name="dbAttribute">Attribute to register under.</param>
	/// <param name="oldValue">Check the old value, in case we don't need to wait at all.</param>
	ValueTask WriteCommandList(MString command, ParserState state, DbRefAttribute dbAttribute, int oldValue);
	
	/// <summary>
	/// Write an async function to the scheduler, to be immediately executed when the scheduler runs.
	/// </summary>
	/// <param name="function">Function to run, before invoking the DbRefAttribute</param>
	/// <param name="dbAttribute">Attribute to register under.</param>
	/// <returns></returns>
	ValueTask WriteAsyncAttribute(Func<ValueTask<ParserState>> function, DbRefAttribute dbAttribute);

	/// <summary>
	/// Write a command-list to the scheduler on semaphore with a timeout, to be immediately executed when the scheduler runs.
	/// </summary>
	/// <param name="command">The command to run.</param>
	/// <param name="state">A ParserState to ensure valid parsing.</param>
	/// <param name="timeout">Timeout after which the command is re-queued as a regular command to be immediately run.</param>
	/// <param name="dbAttribute">Attribute to register under.</param>
	/// <param name="oldValue">Check the old value, in case we don't need to wait at all.</param>
	ValueTask WriteCommandList(MString command, ParserState state, DbRefAttribute dbAttribute, int oldValue, TimeSpan timeout);

	/// <summary>
	/// Write a commandlist to the scheduler on semaphore with a timeout, to be immediately executed when the scheduler runs.
	/// </summary>
	/// <param name="command">The command to run.</param>
	/// <param name="state">A ParserState to ensure valid parsing.</param>
	/// <param name="delay">Timeout after which the command is re-queued as a regular command to be immediately run.</param>
	ValueTask WriteCommandList(MString command, ParserState state, TimeSpan delay);

	/// <summary>
	/// Get all Tasks currently running on the scheduler, when they are due, and the handle they are associated with.
	/// </summary>
	/// <returns>An AsyncEnumerable grouped by type, with either a <see cref="string"/> handle or <see cref="DBRef"/>, and the time/date they are expected to run by.</returns>
	IAsyncEnumerable<(string Group, (DateTimeOffset, OneOf<string, DBRef>)[])> GetAllTasks();

	/// <summary>
	/// Get all Tasks currently running on the scheduler for a pid, when they are due, and the handle they are associated with.
	/// Normally, these should only be immediate tasks in the case of a handle.
	/// </summary>
	/// <returns>An AsyncEnumerable grouped by type, and the time/date they may be expected to run by.</returns>
	IAsyncEnumerable<SemaphoreTaskData> GetSemaphoreTasks(long pid);

	/// <summary>
	/// Get all Tasks currently running on the scheduler for a DBref, when they are due, and the handle they are associated with.
	/// </summary>
	/// <returns>An AsyncEnumerable grouped by type, and the time/date they may be expected to run by.</returns>
	IAsyncEnumerable<SemaphoreTaskData> GetSemaphoreTasks(DBRef obj);
	
	/// <summary>
	/// Get all Tasks currently running on the scheduler for a DBref's specific Attribute, when they are due, and the handle they are associated with.
	/// </summary>
	/// <returns>An AsyncEnumerable grouped by type, and the time/date they may be expected to run by.</returns>
	IAsyncEnumerable<SemaphoreTaskData> GetSemaphoreTasks(DbRefAttribute obj);

	/// <summary>
	/// Get all Delay queue tasks (from @wait) for a specific DBRef.
	/// </summary>
	/// <param name="obj">DBRef to query delay tasks for</param>
	/// <returns>PIDs of delay queue tasks</returns>
	IAsyncEnumerable<long> GetDelayTasks(DBRef obj);

	/// <summary>
	/// Get all Enqueue tasks for a specific DBRef.
	/// </summary>
	/// <param name="obj">DBRef to query enqueue tasks for</param>
	/// <returns>PIDs of enqueue tasks (currently not tracked, returns 0)</returns>
	IAsyncEnumerable<long> GetEnqueueTasks(DBRef obj);

	/// <summary>
	/// Notify a Semaphore trigger to trigger one or more waiting jobs.
	/// </summary>
	/// <param name="dbAttribute">DbRef and Attribute with a value</param>
	/// <param name="oldValue">The old value, before notifying.</param>
	/// <param name="count">Number of tasks to notify (default 1)</param>
	ValueTask Notify(DbRefAttribute dbAttribute, int oldValue, int count = 1);

	/// <summary>
	/// Notify a Semaphore trigger to trigger all waiting jobs.
	/// </summary>
	/// <param name="dbAttribute">DbRef and Attribute with a value</param>
	ValueTask NotifyAll(DbRefAttribute dbAttribute);
	
	/// <summary>
	/// Modify Q-registers of the first waiting task on a semaphore.
	/// </summary>
	/// <param name="dbAttribute">DbRef and Attribute with a value</param>
	/// <param name="qRegisters">Dictionary of Q-register names to values</param>
	/// <returns>True if a task was found and modified, false otherwise</returns>
	ValueTask<bool> ModifyQRegisters(DbRefAttribute dbAttribute, Dictionary<string, MString> qRegisters);
	
	/// <summary>
	/// Drains a series of Jobs, removing them from jobs to be performed.
	/// </summary>
	/// <param name="dbAttribute">DbRef and Attribute with a value</param>
	/// <param name="count">Optional number of tasks to drain (null = all)</param>
	ValueTask Drain(DbRefAttribute dbAttribute, int? count = null);
	
	/// <summary>
	/// Removes all non-Semaphore jobs related to a DBRef from executing immediately.
	/// </summary>
	/// <param name="dbRef">DbRef</param>
	ValueTask Halt(DBRef dbRef);

	/// <summary>
	/// Reschedules a Semaphore trigger, or otherwise adds a delay to it.
	/// </summary>
	/// <param name="handle">Trigger Handle</param>
	/// <param name="delay">How long from now to reschedule it to</param>
	ValueTask RescheduleSemaphoreTask(long handle, TimeSpan delay);

	/// <summary>
	/// Halts a specific task by PID.
	/// </summary>
	/// <param name="pid">Process ID</param>
	/// <returns>True if task was found and halted, false otherwise</returns>
	ValueTask<bool> HaltByPid(long pid);
}