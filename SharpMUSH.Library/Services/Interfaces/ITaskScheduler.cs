using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.InputSessions;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Puts work on the queue: typed input, command lists (immediate, delayed, or waiting on a semaphore),
/// queued functions and socket work, each admitted against the queue limits. Semaphore signalling is
/// <see cref="ISemaphoreQueue"/>, halting and pausing <see cref="ITaskQueueControl"/>, and every
/// question about what is queued <see cref="ITaskQueueReader"/>.
/// </summary>
public interface ITaskScheduler
{
	/// <summary>
	/// Waits until the immediate-execution queue has no entries left to run, so a test can assert on
	/// the effects of work that was queued rather than run inline — an action attribute triggered by
	/// <see cref="IDidItService"/>, for one.
	/// </summary>
	/// <param name="timeout">How long to wait before giving up; defaults to five seconds.</param>
	/// <exception cref="TimeoutException">The queue was still busy when the timeout elapsed.</exception>
	ValueTask DrainImmediateQueueForTests(TimeSpan? timeout = null);

	ValueTask<QueueCommandReservation> ReserveCommandList(MString command, ParserState state);

	ValueTask<QueueAdmissionResult> AdmitUserCommand(long handle, MString command, ParserState state);

	ValueTask<QueueAdmissionResult> AdmitCommandList(MString command, ParserState state);

	ValueTask<QueueAdmissionResult> AdmitCommandList(MString command, ParserState state, DbRefAttribute dbAttribute, int oldValue, bool manageSemaphoreCount = false);

	ValueTask<QueueAdmissionResult> AdmitAsyncAttribute(Func<ValueTask<ParserState>> function, DbRefAttribute dbAttribute, DBRef? executor = null, DBRef? enactor = null);

	ValueTask<QueueAdmissionResult> AdmitCommandList(MString command, ParserState state, DbRefAttribute dbAttribute, int oldValue, TimeSpan timeout, bool manageSemaphoreCount = false);

	ValueTask<QueueAdmissionResult> AdmitCommandList(MString command, ParserState state, TimeSpan delay);

	ValueTask<QueueAdmissionResult> AdmitWork(Func<ValueTask<CallState?>> action, string triggerName, string group);

	ValueTask<QueueAdmissionResult> AdmitWork(Func<ValueTask<CallState?>> action, string triggerName, string group, DBRef executor, bool notifyOnRejection = true);

	/// <summary>
	/// Admits work that arrived on a socket of its own rather than from an object or a player, as an
	/// inbound HTTP request does. PennMUSH queues such an entry <c>QUEUE_SOCKET</c> and <c>do_entry</c>
	/// charges it to no one (<c>src/cque.c</c>, <c>run_http_command</c>), so it counts against the global
	/// queue limit only, never against an owner's quota or the shared system bucket.
	/// </summary>
	/// <param name="onReleased">
	/// Runs once the entry leaves the queue, whether it ran, was halted before it ran, or was dropped at
	/// shutdown, so a caller waiting on the work always has an answer.
	/// </param>
	ValueTask<QueueAdmissionResult> AdmitSocketWork(Func<ValueTask<CallState?>> action, string triggerName, string group, Action? onReleased = null);

	ValueTask<QueueAdmissionResult> ReleaseScheduledWork(long pid, bool semaphoreTimeout = false);

	ValueTask<QueueAdmissionResult> ReleaseScheduledWork(long pid, bool semaphoreTimeout, long? generation);

	/// <summary>Admit an expired input callback under its initiating executor and normal queue budget.</summary>
	ValueTask<QueueAdmissionResult> WriteInputSessionTimeout(InputSession session);
}
