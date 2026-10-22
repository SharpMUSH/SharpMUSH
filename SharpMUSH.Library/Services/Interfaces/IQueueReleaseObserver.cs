using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>Optional release observation on the actual scheduler; no second queue is created.</summary>
public interface IQueueReleaseObserver
{
	/// <summary>
	/// Observes one admitted host entry after its reservation and cancellation source are released.
	/// Rejected work has no release callback. Outcome describes this entry, not future descendants
	/// or business success. A throwing observer is logged and cannot stop the consumer. The callback
	/// can run before admission returns and runs synchronously: keep it short, use asynchronous task
	/// continuations, and never wait for this queue's drain or disposal from inside it. An entry
	/// admitted with <paramref name="timed"/> false is not softcode: <c>queue_entry_cpu_time</c> does
	/// not time it, and halt and shutdown still cancel it.
	/// </summary>
	ValueTask<QueueAdmissionResult> AdmitObservedSocketWork(Func<ValueTask<CallState?>> action,
		string triggerName, string group, Action<QueueEntryRelease> onReleased, bool timed = true);
}
