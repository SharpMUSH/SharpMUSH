using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Signals the entries waiting on a semaphore attribute: <c>@notify</c>, <c>@drain</c>, their
/// counted forms, and the changes a waiting entry can still take (its q-registers, its timeout).
/// Entries are put on a semaphore through <see cref="ITaskScheduler"/>.
/// </summary>
public interface ISemaphoreQueue
{
	/// <summary>
	/// Holds the semaphore lock while a command reads, signals and persists a semaphore count, so no
	/// other notify or drain interleaves with it.
	/// </summary>
	ValueTask<IDisposable> EnterSemaphoreMutationAsync();

	ValueTask<int> ApplySemaphoreCommandAsync(DbRefAttribute target, int? count, bool drain,
		Func<int, ValueTask> persist, Func<ValueTask<bool>> reconcile, Dictionary<string, MString>? registers = null);

	/// <summary>Releases up to <paramref name="count"/> entries waiting on the semaphore.</summary>
	/// <param name="dbAttribute">DbRef and Attribute with a value</param>
	/// <param name="oldValue">The old value, before notifying.</param>
	/// <param name="count">Number of tasks to notify (default 1)</param>
	ValueTask<IReadOnlyList<QueueAdmissionResult>> NotifyCounted(DbRefAttribute dbAttribute, int oldValue, int count = 1);

	/// <summary>Releases every entry waiting on the semaphore.</summary>
	ValueTask<IReadOnlyList<QueueAdmissionResult>> NotifyAllCounted(DbRefAttribute dbAttribute);

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

	/// <summary>Like <see cref="Drain"/>, and answers how many entries were removed.</summary>
	ValueTask<int> DrainCounted(DbRefAttribute dbAttribute, int? count = null);

	/// <summary>
	/// Reschedules a Semaphore trigger, or otherwise adds a delay to it.
	/// </summary>
	/// <param name="handle">Trigger Handle</param>
	/// <param name="delay">How long from now to reschedule it to</param>
	ValueTask RescheduleSemaphoreTask(long handle, TimeSpan delay);
}
