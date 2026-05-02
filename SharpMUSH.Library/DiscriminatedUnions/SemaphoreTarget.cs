using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A union for semaphore scheduler queries,
/// replacing OneOf&lt;long, DBRef, DbRefAttribute&gt;.
/// </summary>
public union SemaphoreTarget(long, DBRef, DbRefAttribute)
{
	public bool IsHandle       => Value is long;
	public bool IsDBRef        => Value is DBRef;
	public bool IsDbRefAttr    => Value is DbRefAttribute;

	public long          AsHandle    => (long)Value!;
	public DBRef         AsDBRef     => (DBRef)Value!;
	public DbRefAttribute AsDbRefAttr => (DbRefAttribute)Value!;
}
