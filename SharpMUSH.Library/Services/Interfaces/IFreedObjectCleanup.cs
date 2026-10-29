using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Something outside the object store that holds objects by number and has to let go of them once they are
/// freed for good, before their numbers can name other objects: PennMUSH's <c>free_object()</c> sweeps
/// everything else that holds a dbref for the same reason. Each registered cleanup runs after the objects are
/// deleted, once for however many were freed together.
/// </summary>
public interface IFreedObjectCleanup
{
	/// <summary>Forgets <paramref name="objids"/>, none of which exists any longer.</summary>
	ValueTask ObjectsFreedAsync(IReadOnlyCollection<DBRef> objids, CancellationToken cancellationToken = default);
}
