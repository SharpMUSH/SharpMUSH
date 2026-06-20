using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record SetLockCommand(SharpObject Target, string LockName, string LockString, AnySharpObject? Executor = null) : ICommand, ICacheInvalidating
{
	public string[] CacheKeys => [Definitions.CacheKeys.Object(Target.DBRef), $"lock:{Target.DBRef}:{LockName}"];
	public string[] CacheTags => [];
}

public record UnsetLockCommand(SharpObject Target, string LockName) : ICommand, ICacheInvalidating
{
	public string[] CacheKeys => [Definitions.CacheKeys.Object(Target.DBRef), $"lock:{Target.DBRef}:{LockName}"];
	public string[] CacheTags => [];
}
