namespace SharpMUSH.Library.Services;

/// <summary>How a store applies an order staff set for the categories pinned to the wiki home.</summary>
public static class WikiPinOrder
{
	/// <summary>
	/// The pinned keys <paramref name="pinned"/> in the order <paramref name="requested"/> gives them. A requested
	/// key that is not pinned is passed over (it may have been unpinned since the editor loaded), and a pinned key
	/// the request leaves out keeps its place relative to the others, after every key the request names.
	/// </summary>
	public static IReadOnlyList<string> Arrange(IReadOnlyList<string> pinned, IReadOnlyList<string> requested)
	{
		var named = requested.Where(pinned.Contains).Distinct(StringComparer.Ordinal).ToList();
		return [.. named, .. pinned.Where(key => !named.Contains(key, StringComparer.Ordinal))];
	}
}
