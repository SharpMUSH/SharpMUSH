using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>
/// Bulk-reassigns all attributes currently owned by <see cref="OldOwner"/> to <see cref="NewOwner"/>.
/// Issued automatically when a player is deleted so that surviving attribute records are
/// transferred to the probate player rather than being left pointing at the deleted player.
/// </summary>
public record ReassignAttributeOwnerCommand(SharpPlayer OldOwner, SharpPlayer NewOwner) : ICommand;
