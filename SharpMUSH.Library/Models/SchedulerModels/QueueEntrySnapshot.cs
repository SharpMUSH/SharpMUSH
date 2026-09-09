namespace SharpMUSH.Library.Models.SchedulerModels;

public enum QueueEntryState { Pending, Paused, Ready, Running }
public enum QueueControlResult { Applied, AlreadyInState, NotFound, NotPending, InvalidReason, InvalidIdentity }
public sealed record QueueEntrySnapshot(long Pid, DBRef? Source, DBRef? Owner, string Kind,
	QueueEntryState State, TimeSpan? RemainingDelay, string PauseReason);
