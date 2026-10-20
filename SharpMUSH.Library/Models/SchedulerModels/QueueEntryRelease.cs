using SharpMUSH.Library.Models.Diagnostics;

namespace SharpMUSH.Library.Models.SchedulerModels;

/// <summary>
/// Closed, non-executable entry metadata. A null failed-invocation count means instrumentation was
/// absent, not zero failures. Completed only means the callback returned within its queue budget.
/// </summary>
public sealed record QueueEntryRelease(long Pid, QueueOutcome Outcome, long? FailedInvocations);
