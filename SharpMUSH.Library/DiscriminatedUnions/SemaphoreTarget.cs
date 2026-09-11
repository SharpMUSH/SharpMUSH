using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// Which semaphore tasks to select: by process id, by the object they wait on, or by the exact
/// object/attribute semaphore.
/// </summary>
public partial union SemaphoreTarget(long, DBRef, DbRefAttribute);
