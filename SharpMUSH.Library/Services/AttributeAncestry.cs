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
	/// Returns the root..leaf path for <paramref name="leaf"/>.
	/// Ancestors present in <paramref name="known"/> are taken from there;
	/// absent ones are fetched via <paramref name="fetch"/>, which receives the
	/// split path and returns null when no such attribute exists.
	/// </summary>
	/// <param name="leaf">The attribute whose path is being assembled.</param>
	/// <param name="known">
	/// Already-materialised ancestors, keyed by <c>LongName</c>. Must be built with
	/// <see cref="StringComparer.OrdinalIgnoreCase"/> - attribute names are case-insensitive.
	/// </param>
	/// <param name="fetch">Loads an ancestor absent from <paramref name="known"/>.</param>
	public static ValueTask<SharpAttribute[]> PathAsync(
		SharpAttribute leaf,
		IReadOnlyDictionary<string, SharpAttribute> known,
		Func<string[], ValueTask<SharpAttribute?>> fetch)
		=> PathAsync(leaf, known, fetch, static x => x.LongName);

	/// <inheritdoc cref="PathAsync(SharpAttribute,IReadOnlyDictionary{string,SharpAttribute},Func{string[],ValueTask{SharpAttribute}})"/>
	public static ValueTask<LazySharpAttribute[]> PathAsync(
		LazySharpAttribute leaf,
		IReadOnlyDictionary<string, LazySharpAttribute> known,
		Func<string[], ValueTask<LazySharpAttribute?>> fetch)
		=> PathAsync(leaf, known, fetch, static x => x.LongName);

	private static async ValueTask<T[]> PathAsync<T>(
		T leaf,
		IReadOnlyDictionary<string, T> known,
		Func<string[], ValueTask<T?>> fetch,
		Func<T, string> longNameOf)
		where T : class
	{
		var segments = longNameOf(leaf).Split('`');
		var result = new List<T>(segments.Length);

		for (var i = 1; i <= segments.Length; i++)
		{
			var prefixParts = segments[..i];
			var prefixName = string.Join('`', prefixParts);

			var attribute = i == segments.Length
				? leaf
				: known.TryGetValue(prefixName, out var knownAttribute)
					? knownAttribute
					: await fetch(prefixParts);

			if (attribute is not null)
			{
				result.Add(attribute);
			}
		}

		return result.ToArray();
	}
}
