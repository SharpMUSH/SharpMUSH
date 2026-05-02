using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;

namespace SharpMUSH.Library.Queries;

public record ScheduleSemaphoreQuery(SemaphoreTarget Query) : IStreamQuery<SemaphoreTaskData>;

public record ScheduleDelayQuery(DBRef Query) : IStreamQuery<long>;

public record ScheduleEnqueueQuery(DBRef Query) : IStreamQuery<long>;

public record ScheduleAllTasksQuery : IStreamQuery<(string Group, (DateTimeOffset, DbRefOrName)[])>;
