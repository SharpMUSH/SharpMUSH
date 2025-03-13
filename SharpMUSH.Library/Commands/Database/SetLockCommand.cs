using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record SetLockCommand(SharpObject Target, string LockName, string LockString) : ICommand, ICacheInvalidating
{
	public string[] CacheKeys => [Target.DBRef.ToString()];
	public string[] CacheTags => [Definitions.CacheTags.ObjectLocks];
}