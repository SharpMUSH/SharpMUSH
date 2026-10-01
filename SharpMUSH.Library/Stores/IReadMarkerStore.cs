using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>
/// Each character's read markers (<see cref="SharpReadMarker"/>), keyed by the character's objid and the
/// marker's scope. A character is always named by its full objid, so a player who takes a recycled dbref
/// inherits nothing, and destroying the character drops its markers.
/// </summary>
public interface IReadMarkerStore
{
	/// <summary>Every marker <paramref name="character"/> has, in no particular order.</summary>
	/// <exception cref="ArgumentException"><paramref name="character"/> is not an objid.</exception>
	ValueTask<IReadOnlyList<SharpReadMarker>> GetReadMarkersAsync(DBRef character, CancellationToken cancellationToken = default);

	/// <summary>
	/// Moves <paramref name="character"/>'s marker for <paramref name="marker"/>'s scope to it, unless the
	/// stored one is already as far on (<see cref="SharpReadMarker.IsPast"/>), and returns the marker as
	/// stored afterwards.
	/// </summary>
	/// <remarks>
	/// A conditional write, decided inside the write: two devices marking the same channel read at
	/// different points can land in either order, and a read-then-write in the caller would let the one
	/// that is behind move the marker back.
	/// </remarks>
	/// <exception cref="ArgumentException"><paramref name="character"/> is not an objid.</exception>
	ValueTask<SharpReadMarker> AdvanceReadMarkerAsync(DBRef character, SharpReadMarker marker,
		CancellationToken cancellationToken = default);
}
