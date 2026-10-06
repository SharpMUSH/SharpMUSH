using SharpMUSH.Library.Services;

namespace SharpMUSH.Library.Common;

/// <summary>
/// PennMUSH's <c>atr_match</c>, which is <c>aname_hash_lookup</c> (<c>src/atr_tab.c:112-122</c>): the
/// standard attribute a name means when no attribute of that exact name was found. The table it
/// searches holds every attribute entry (standard and <c>@attribute</c>-added) and the
/// <c>attralias</c> names (<c>hdrs/atr_tab.h:258</c>, <see cref="AttributeService.StandardAttributeAliases"/>).
/// An exact key always matches; otherwise the name must be a prefix of exactly one key
/// (<c>ptab_find</c>, <c>src/ptab.c:74-140</c>), and the attribute that key names must be
/// <c>prefixmatch</c>. So <c>DESCR</c> means <c>DESCRIBE</c>, and <c>DES</c> means nothing, since
/// <c>DESC</c> and <c>DESCFORMAT</c> begin with it too.
/// </summary>
public static class AttributeNameMatch
{
	private const string PrefixMatchFlag = "prefixmatch";

	/// <summary>
	/// The standard attribute <paramref name="name"/> means, upper-cased, or null. The result can be
	/// <paramref name="name"/> itself (an entry of that exact name), which a caller retrying under
	/// the match treats as nothing new to try.
	/// </summary>
	/// <param name="name">The attribute name as read.</param>
	/// <param name="defaultFlagsOf">The default flags of the entry with this exact name, or null when there is none.</param>
	/// <param name="entryNamesStartingWith">Every entry name that begins with the given upper-case prefix.</param>
	public static string? Match(string name,
		Func<string, IReadOnlyCollection<string>?> defaultFlagsOf,
		Func<string, IEnumerable<string>> entryNamesStartingWith)
	{
		// No table key holds a backtick, so a tree name is neither a key nor a prefix of one.
		if (string.IsNullOrEmpty(name) || name.Contains('`'))
		{
			return null;
		}

		var upper = name.ToUpperInvariant();
		if (defaultFlagsOf(upper) is not null)
		{
			return upper;
		}

		if (AttributeService.StandardAttributeAliases.TryGetValue(upper, out var aliased))
		{
			return aliased;
		}

		var keys = entryNamesStartingWith(upper)
			.Select(key => key.ToUpperInvariant())
			.Concat(AttributeService.StandardAttributeAliases.Keys.Where(key => key.StartsWith(upper, StringComparison.OrdinalIgnoreCase)))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Take(2)
			.ToArray();

		if (keys.Length != 1)
		{
			return null;
		}

		var target = AttributeService.StandardAttributeAliases.TryGetValue(keys[0], out var real) ? real : keys[0];
		return defaultFlagsOf(target) is { } flags && flags.Contains(PrefixMatchFlag, StringComparer.OrdinalIgnoreCase)
			? target.ToUpperInvariant()
			: null;
	}
}
