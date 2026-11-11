using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>
/// The zones an object is in, in order. The first is its zone (<see cref="SharpObject.Zone"/>), the one
/// <see cref="IObjectStore.SetObjectZone"/> sets; RhostMUSH lets an object be in more, and a game imported from one keeps them.
/// </summary>
public interface IZoneListStore
{
	/// <summary>The zones <paramref name="obj"/> is in, its first zone first; empty when it has none or is gone.</summary>
	ValueTask<IReadOnlyList<DBRef>> GetZoneRefsAsync(DBRef obj, CancellationToken cancellationToken = default);

	/// <summary>
	/// Makes <paramref name="zones"/> the zones <paramref name="obj"/> is in, the first being its zone. A zone named twice
	/// is kept once, at its first place; an empty list leaves it in none.
	/// </summary>
	ValueTask SetZonesAsync(DBRef obj, IReadOnlyList<DBRef> zones, CancellationToken cancellationToken = default);
}
