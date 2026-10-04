using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Answers what is on the queue — <c>@ps</c>, queue diagnostics, the queue limits — without changing it.
/// </summary>
public interface ITaskQueueReader
{
	/// <summary>
	/// Get all Tasks currently running on the scheduler, when they are due, and the handle they are associated with.
	/// </summary>
	/// <returns>An AsyncEnumerable grouped by type, with either a <see cref="string"/> handle or <see cref="DBRef"/>, and the time/date they are expected to run by.</returns>
	IAsyncEnumerable<(string Group, (DateTimeOffset, NameOrDbRef)[])> GetAllTasks();

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
	/// <returns>PIDs of enqueue tasks</returns>
	IAsyncEnumerable<long> GetEnqueueTasks(DBRef obj);

	/// <summary>Whether work admitted with this original trigger name and group still owns a queued, running, or canceled reservation.</summary>
	bool HasPendingWork(string triggerName, string group);

	QueueUsage GetQueueUsage();

	IReadOnlyList<QueueEntrySnapshot> GetQueueEntries();

	QueueEntrySnapshot? GetQueueEntry(long pid);

	IEnumerable<QueueEntrySnapshot> EnumerateQueueEntries();
}
