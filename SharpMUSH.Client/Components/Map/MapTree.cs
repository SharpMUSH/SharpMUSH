using SharpMUSH.Library.API;

namespace SharpMUSH.Client.Components.Map;

/// <summary>
/// The maps as regions inside regions: each map's parent as the list nests it. A parent the list does not hold, or a
/// chain of parents that comes back to the map (Brooklyn inside New York inside Brooklyn), leaves it at the top.
/// </summary>
public static class MapTree
{
	public static IReadOnlyDictionary<string, string?> Parents(IReadOnlyList<MapSummaryDto> maps)
	{
		var stored = maps.ToDictionary(map => map.Id, map => map.Parent is { } parent && parent != map.Id ? parent : null);
		return stored.ToDictionary(entry => entry.Key,
			entry => entry.Value is { } parent && stored.ContainsKey(parent) && !InLoop(stored, entry.Key) ? parent : null);
	}

	private static bool InLoop(Dictionary<string, string?> parents, string id)
	{
		var seen = new HashSet<string>();
		for (var at = parents[id]; at is not null && parents.ContainsKey(at); at = parents[at])
		{
			if (at == id) return true;
			if (!seen.Add(at)) return false;
		}
		return false;
	}
}
