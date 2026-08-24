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
	public static async ValueTask<SharpAttribute[]> PathAsync(
		SharpAttribute leaf,
		IReadOnlyDictionary<string, SharpAttribute> known,
		Func<string[], ValueTask<SharpAttribute?>> fetch)
	{
		var segments = leaf.LongName.Split('`');
		var result = new List<SharpAttribute>(segments.Length);

		for (var i = 1; i <= segments.Length; i++)
		{
			var prefixParts = segments[..i];
			var prefixName = string.Join('`', prefixParts);

			SharpAttribute? attribute = i == segments.Length
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
