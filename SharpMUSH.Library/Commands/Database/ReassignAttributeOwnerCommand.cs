using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>
/// Bulk-reassigns all attributes currently owned by <see cref="OldOwner"/> to <see cref="NewOwner"/>.
/// Issued automatically when a player is deleted so that surviving attribute records are
/// transferred to the probate player rather than being left pointing at the deleted player.
/// </summary>
/// <remarks>
/// It names no keys because it names no objects: the attributes it rewrites are whatever the old
/// owner happened to own, anywhere in the database. Every cached attribute may now report the wrong
/// owner and attribute permission checks read that, so both attribute tags go. This is the one write
/// that genuinely needs the game-wide sweep the per-object tag replaced, and a player deletion is
/// rare enough that the breadth costs nothing.
/// </remarks>
public record ReassignAttributeOwnerCommand(SharpPlayer OldOwner, SharpPlayer NewOwner)
	: ICommand, ICacheInvalidating
{
	public string[] CacheKeys => [];

	public string[] CacheTags =>
	[
		Definitions.CacheTags.AllObjectAttributes,
		Definitions.CacheTags.InheritedAttributes
	];
}
