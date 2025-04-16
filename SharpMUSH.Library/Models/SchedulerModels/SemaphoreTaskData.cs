namespace SharpMUSH.Library.Models.SchedulerModels;

public record SemaphoreTaskData(long Pid, MString Command, DBRef Owner, DbRefAttribute SemaphoreSource, TimeSpan? RunDelay);