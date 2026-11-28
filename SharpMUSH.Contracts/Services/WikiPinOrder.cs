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

	/// <summary>
	/// <paramref name="pinned"/> with <paramref name="category"/> put back where it stood in <paramref name="before"/>:
	/// right after the nearest key that came before it there and is still pinned, or first when none is. The other
	/// keys keep the order <paramref name="pinned"/> gives them, so an order set in the meantime stands.
	/// </summary>
	public static IReadOnlyList<string> Restore(IReadOnlyList<string> pinned, IReadOnlyList<string> before, string category)
	{
		var order = pinned.Where(key => key != category).ToList();
		var at = before.ToList().IndexOf(category);
		if (at < 0) return [.. order, category];
		var after = before.Take(at).LastOrDefault(order.Contains);
		order.Insert(after is null ? 0 : order.IndexOf(after) + 1, category);
		return order;
	}
}
