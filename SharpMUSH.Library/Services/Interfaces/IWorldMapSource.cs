using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Stores a map's places apart from rooms, for the World map. A plugin registers one through
/// <c>IServiceRegistrar</c>; the World map asks each in turn whether it holds a map, and draws that map's places from
/// the one that does. Every read here is side-effect free: it never lends a room, assigns anything, or runs softcode
/// that could change the world.
/// </summary>
public interface IWorldMapSource
{
	/// <summary>The space <paramref name="map"/>'s places are stored in and how far they reach; not found when this source does not hold it.</summary>
	ValueTask<Found<MapSpaceDto>> SpaceAsync(AnySharpObject map, CancellationToken ct);

	/// <summary>
	/// The places of <paramref name="window"/> on <paramref name="map"/>, as <paramref name="viewer"/> may see them. A source
	/// returns at most <see cref="MapWindow.MaxCells"/> cells and sets <see cref="MapCellsDto.Truncated"/> past that.
	/// </summary>
	ValueTask<MapCellsDto> CellsAsync(AnySharpObject viewer, AnySharpObject map, MapWindow window, CancellationToken ct);

	/// <summary>
	/// Whether the place <paramref name="x"/>, <paramref name="y"/> on level <paramref name="level"/> of <paramref name="map"/>
	/// is hidden from <paramref name="viewer"/>, so a room standing there is left off the map as the place is.
	/// </summary>
	ValueTask<bool> HiddenAsync(AnySharpObject viewer, AnySharpObject map, int x, int y, int level, CancellationToken ct);
}

/// <summary>A rectangle of one level of a map, edges included.</summary>
public readonly record struct MapWindow(int Level, int Left, int Bottom, int Right, int Top)
{
	/// <summary>The most cells one window may cover, and one read return.</summary>
	public const int MaxCells = MapCellsDto.MaxCells;

	public long Width => (long)Right - Left + 1;
	public long Height => (long)Top - Bottom + 1;

	/// <summary>How many cells the window covers; past <see cref="MaxCells"/> on either side, more than any window may hold.</summary>
	public long Cells => Width > MaxCells || Height > MaxCells ? long.MaxValue : Width * Height;
}
