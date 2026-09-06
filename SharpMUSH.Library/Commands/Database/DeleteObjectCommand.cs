using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>
/// Irrevocably removes an object from the database — the storage half of PennMUSH's
/// <c>free_object()</c>. See <see cref="IObjectStore.DeleteObjectAsync"/> for what this does
/// <i>not</i> do; game-layer callers want <c>IObjectDestructionService.DestroyObjectAsync</c>.
/// </summary>
/// <remarks>
/// The invalidation is deliberately broad. Deletion severs edges on objects the caller never named
/// (anything that parented, zoned, homed or was located here), and it is rare enough that dropping
/// every object-shaped tag costs far less than serving a reference to a row that no longer exists.
/// </remarks>
public record DeleteObjectCommand(DBRef Target) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys => [Definitions.CacheKeys.Object(Target), Definitions.CacheKeys.Contents(Target)];

	public string[] CacheTags =>
	[
		Definitions.CacheTags.ObjectList,
		Definitions.CacheTags.PlayerList,
		Definitions.CacheTags.PlayerNames,
		Definitions.CacheTags.RoomList,
		Definitions.CacheTags.ThingList,
		Definitions.CacheTags.ExitList,
		Definitions.CacheTags.ObjectContents,
		Definitions.CacheTags.ObjectAttributes,
		Definitions.CacheTags.ObjectOwnership,
		Definitions.CacheTags.ObjectLocks,
		Definitions.CacheTags.ChannelList,
		Definitions.CacheTags.ZoneObjects
	];
}
