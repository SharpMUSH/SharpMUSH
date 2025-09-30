using Mediator;
using OneOf;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;

namespace SharpMUSH.Library.Queries;

public record ScheduleSemaphoreQuery(OneOf<long,DBRef,DbRefAttribute> Query) :  IQuery<IAsyncEnumerable<SemaphoreTaskData>>;

// TODO - get non-semaphore data
// public record ScheduleQueueQuery(DBRef Query) :  IQuery<IAsyncEnumerable<(string Group, DateTimeOffset[])>>;