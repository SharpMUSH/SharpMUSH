using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Extensions;

public static class SharpAttributeFlagExtensions
{
	/// <summary>
	/// The second names PennMUSH's attr_privs_set (<c>src/atr_tab.c</c>) gives four of its flags, each
	/// with the name SharpMUSH's flag table uses.
	/// </summary>
	private static readonly (string Alias, string Name)[] PennMUSHAliases =
	[
		("private", "no_inherit"),
		("hidden", "mortal_dark"),
		("no_name", "noname"),
		("no_space", "nospace")
	];

	/// <summary>
	/// The flag in <paramref name="table"/> that <paramref name="name"/> means, read the way PennMUSH reads an
	/// attribute flag list: a name, alias or symbol in either case, else the shortest name or alias it is a
	/// prefix of (<c>wiz</c> is <c>wizard</c>, <c>hid</c> is <c>mortal_dark</c>). Null when nothing matches.
	/// </summary>
	/// <remarks>
	/// An empty name matches nothing. It would otherwise match <c>prefixmatch</c>, whose symbol is empty, and
	/// failing that be a prefix of every flag.
	/// </remarks>
	public static SharpAttributeFlag? Named(this IReadOnlyCollection<SharpAttributeFlag> table, string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return null;
		}

		var names = table
			.Select(flag => (Name: flag.Name, Flag: flag))
			.Concat(PennMUSHAliases.SelectMany(alias => table
				.Where(flag => flag.Name.Equals(alias.Name, StringComparison.OrdinalIgnoreCase))
				.Select(flag => (Name: alias.Alias, Flag: flag))))
			.ToArray();

		return names.FirstOrDefault(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Flag
			?? table.FirstOrDefault(flag => flag.Symbol != null && flag.Symbol.Equals(name, StringComparison.OrdinalIgnoreCase))
			?? names
				.Where(n => n.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase))
				.OrderBy(n => n.Name.Length)
				.Select(n => n.Flag)
				.FirstOrDefault();
	}
}
