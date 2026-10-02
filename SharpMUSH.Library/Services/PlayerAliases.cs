using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Services;

/// <summary>
/// A player's aliases are its <c>ALIAS</c> attribute, as in PennMUSH: there is no other copy to write.
/// The provider indexes them for player lookup in the same transaction as every write to that
/// attribute (<c>reset_player_list</c>, <c>src/plyrlist.c</c>), so <c>*alias</c>, page and
/// <see cref="Models.SharpPlayer.Aliases"/> follow whatever wrote it — <c>@alias</c>, <c>&amp;ALIAS</c>,
/// <c>@set</c>, <c>@name</c>, <c>alias()</c>, <c>@wipe</c> or the PennMUSH importer.
/// </summary>
public static class PlayerAliases
{
	/// <summary>The attribute that holds an object's aliases.</summary>
	public const string AttributeName = "ALIAS";

	/// <summary>PennMUSH's <c>ALIAS_DELIMITER</c>.</summary>
	public const char Delimiter = ';';

	/// <summary>Whether a write of <paramref name="attribute"/> on <paramref name="thing"/> is a write of
	/// a player's alias list, the case <c>do_set_atr</c> treats apart (<c>src/attrib.c:2268</c>).</summary>
	public static bool Applies(AnySharpObject thing, string attribute)
		=> thing.IsPlayer && IsAliasAttribute(attribute);

	/// <summary>Whether <paramref name="attribute"/> names the alias attribute itself, not a leaf below it.</summary>
	public static bool IsAliasAttribute(string attribute)
		=> attribute.Equals(AttributeName, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// The names an alias list makes a player answer to: PennMUSH's <c>add_player_alias</c>
	/// (<c>src/plyrlist.c:116</c>) splits on <c>;</c>, skips leading spaces and drops empty entries.
	/// </summary>
	public static string[] Split(string value)
		=> [.. value.Split(Delimiter)
			.Select(entry => entry.TrimStart(' '))
			.Where(name => name.Length > 0)
			.Distinct(StringComparer.OrdinalIgnoreCase)];

	/// <summary>PennMUSH's <c>shortalias</c> (<c>src/utils.c</c>): the alias list up to its first <c>;</c>.</summary>
	public static string Short(string fullAlias)
	{
		var end = fullAlias.IndexOf(Delimiter);
		return end < 0 ? fullAlias : fullAlias[..end];
	}
}
