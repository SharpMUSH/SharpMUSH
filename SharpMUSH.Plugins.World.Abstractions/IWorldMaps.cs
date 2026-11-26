using SharpMUSH.Library.Models;
using SharpMUSH.Plugins.World.Storage;

namespace SharpMUSH.Plugins.World;

/// <summary>
/// What another plugin may read of the stored maps. Changes go through <c>@world</c>, which checks who may make them.
/// </summary>
public interface IWorldMaps
{
	/// <summary>The stored map <paramref name="map"/>, or null when it is not one.</summary>
	WorldMapRecord? Map(DBRef map);

	/// <summary>
	/// The terrain id and owned edges of one place; empty when nothing is painted there. The terrain's type is
	/// <see cref="WorldMapRecord.Type"/> of <see cref="WorldCell.Terrain"/>.
	/// </summary>
	WorldCell Cell(DBRef map, int x, int y, int z);

	/// <summary>The place's name, data, edge reasons and locks, or null when it has none.</summary>
	WorldPlaceRecord? Place(DBRef map, int x, int y, int z);

	/// <summary>Where the room stands on a stored map, or null when it stands on none.</summary>
	WorldRoomRecord? Room(DBRef room);

	/// <summary>The room standing on the place, or null when none does.</summary>
	DBRef? StandingAt(DBRef map, int x, int y, int z);
}
