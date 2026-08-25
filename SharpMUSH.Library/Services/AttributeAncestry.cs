using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Assembles the root..leaf ancestor path for an attribute in the attribute tree
/// (e.g. <c>FOO`BAR`BAZ</c> has ancestors <c>FOO</c> and <c>FOO`BAR</c>).
/// PennMUSH re-walks this path on every access to decide permissions - a branch
/// flagged e.g. mortal_dark hides its leaves regardless of the leaf's own flags.
/// </summary>
internal static class AttributeAncestry
{
	/// <summary>
	/// Returns the root..leaf path for <paramref name="leaf"/>, or <c>null</c> when any prefix
	/// segment could not be resolved.
	/// Ancestors present in <paramref name="known"/> are taken from there;
	/// absent ones are fetched via <paramref name="fetch"/>, which receives the
	/// split path and returns null when no such attribute exists.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A missing prefix must never grant. PennMUSH's <c>can_read_attr_internal</c>
	/// (<c>src/attrib.c:324-327</c>) reads <c>if (!atr || ...) goto continue_target;</c> - an
	/// unresolvable prefix on the object being examined abandons that object and moves the walk
	/// to the next one in the parent chain, and when the chain runs out the function ends
	/// <c>return 0</c> (<c>attrib.c:356</c>). Dropping the segment and letting the remaining
	/// levels vote was a fail-open: the path collapsed to <c>[leaf]</c> and every "all levels
	/// must be visual" test passed trivially.
	/// </para>
	/// <para>
	/// <paramref name="fetch"/> must therefore query the object the leaf was actually READ FROM
	/// - with parent-checking on, that is frequently not the object the lookup started at, and
	/// the branch nodes of an inherited tree attribute exist only on the parent.
	/// </para>
	/// <para>
	/// Narrow, unreachable-by-construction divergence: where Penn would abandon one target and
	/// possibly grant on a farther ancestor's complete copy, this denies outright, because the
	/// caller's match set is already deduplicated by name to the nearest copy. It cannot bite
	/// today - all three providers model the tree as one graph edge per level, so a node whose
	/// prefix is absent is not representable.
	/// </para>
	/// </remarks>
	/// <param name="leaf">The attribute whose path is being assembled.</param>
	/// <param name="known">
	/// Already-materialised ancestors, keyed by <c>LongName</c>. Must be built with
	/// <see cref="StringComparer.OrdinalIgnoreCase"/> - attribute names are case-insensitive -
	/// and must only contain attributes from the SAME object the leaf came from.
	/// </param>
	/// <param name="fetch">Loads an ancestor absent from <paramref name="known"/>.</param>
	public static ValueTask<SharpAttribute[]?> PathAsync(
		SharpAttribute leaf,
		IReadOnlyDictionary<string, SharpAttribute> known,
		Func<string[], ValueTask<SharpAttribute?>> fetch)
		=> PathAsync(leaf, known, fetch, static x => x.LongName);

	/// <inheritdoc cref="PathAsync(SharpAttribute,IReadOnlyDictionary{string,SharpAttribute},Func{string[],ValueTask{SharpAttribute}})"/>
	public static ValueTask<LazySharpAttribute[]?> PathAsync(
		LazySharpAttribute leaf,
		IReadOnlyDictionary<string, LazySharpAttribute> known,
		Func<string[], ValueTask<LazySharpAttribute?>> fetch)
		=> PathAsync(leaf, known, fetch, static x => x.LongName);

	private static async ValueTask<T[]?> PathAsync<T>(
		T leaf,
		IReadOnlyDictionary<string, T> known,
		Func<string[], ValueTask<T?>> fetch,
		Func<T, string> longNameOf)
		where T : class
	{
		var segments = longNameOf(leaf).Split('`');
		var result = new T[segments.Length];

		for (var i = 1; i <= segments.Length; i++)
		{
			var prefixParts = segments[..i];
			var prefixName = string.Join('`', prefixParts);

			var attribute = i == segments.Length
				? leaf
				: known.TryGetValue(prefixName, out var knownAttribute)
					? knownAttribute
					: await fetch(prefixParts);

			if (attribute is null)
			{
				return null;
			}

			result[i - 1] = attribute;
		}

		return result;
	}
}
