using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>Restores foreign lock data during world import, retaining legacy expressions and metadata.</summary>
public record ImportLockCommand(SharpObject Target, string LockName, SharpLockData Lock) : ICommand, ICacheInvalidating
{
	public string[] CacheKeys => [Definitions.CacheKeys.Object(Target.DBRef)];
	public string[] CacheTags => [];
}
