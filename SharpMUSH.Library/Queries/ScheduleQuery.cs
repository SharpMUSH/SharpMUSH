using Mediator;
using OneOf;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;

namespace SharpMUSH.Library.Queries;

public record ScheduleSemaphoreQuery(OneOf<long,DBRef,DbRefAttribute> Query) :  IStreamQuery<SemaphoreTaskData>;

public record ScheduleDelayQuery(DBRef Query) : IStreamQuery<long>;

public record ScheduleEnqueueQuery(DBRef Query) : IStreamQuery<long>;

public record ScheduleAllTasksQuery : IStreamQuery<SemaphoreTaskData>;