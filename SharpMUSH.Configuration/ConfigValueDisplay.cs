namespace SharpMUSH.Configuration;

/// <summary>
/// PennMUSH's <c>display_config_value</c> (<c>src/conf.c:1679</c>): <c>config()</c> and
/// <c>@config</c> both print an option through this one formatter, so the two can never disagree
/// about how a value reads. Two of its cases are not the stored value's own <c>ToString()</c>:
/// <list type="bullet">
/// <item>a <c>cf_dbref</c> option carries its <c>#</c> (<c>src/conf.c:1707</c>), and an unset one
/// holds <c>NOTHING</c>, so it prints <c>#-1</c> — that is what makes
/// <c>isdbref(config(master_room))</c> and <c>name(config(master_room))</c> work;</item>
/// <item>a boolean reads <c>Yes</c> or <c>No</c> (<c>src/conf.c:1705</c>, documented under
/// <c>help config()</c>), never the language's own spelling of the bit.</item>
/// </list>
/// Observed against PennMUSH 1.8.8 (<c>80a1d5b9</c>, shipped <c>mushcnf.dst</c>):
/// <c>config(master_room)</c> → <c>#2</c>, <c>config(event_handler)</c> → <c>#-1</c>,
/// <c>config(exits_connect_rooms)</c> → <c>No</c>.
/// </summary>
public static class ConfigValueDisplay
{
	/// <summary>PennMUSH's <c>NOTHING</c>, which is what an unset dbref option holds.</summary>
	private const string Nothing = "#-1";

	/// <summary>The text <c>config()</c> and <c>@config</c> print for one option's stored value.</summary>
	/// <param name="value">The value read off the live options, null when the option is unset.</param>
	/// <param name="metadata">The option's declaration, which says whether it names an object.</param>
	/// <remarks>
	/// A list option prints its words space-separated, never the array's type name. A default-flag
	/// list (<see cref="SharpConfigAttribute.Flag"/>) also keeps the leading space <c>cf_flag</c>
	/// stores: PennMUSH 1.8.8 (<c>80a1d5b9</c>, shipped <c>mushcnf.dst</c>) prints
	/// <c>config(player_flags)</c> as <c>" enter_ok ansi no_command"</c>. A mapping option
	/// (<c>command_aliases</c>, <c>command_restrictions</c>, <c>sitelock_rules</c>, <c>mssp</c>, ...) has no
	/// PennMUSH counterpart, which writes these as repeated <c>mush.cnf</c> directives; it prints as
	/// <c>key=values</c> for each of its <see cref="Entries"/>, separated by <c>|</c>.
	/// </remarks>
	public static string Format(object? value, SharpConfigAttribute metadata)
		=> metadata.Dbref
			? value is null ? Nothing : $"#{value}"
			: value switch
			{
				bool flag => flag ? "Yes" : "No",
				null => string.Empty,
				string text => text,
				IEnumerable<KeyValuePair<string, string[]>> map => string.Join('|', Entries(map).Select(entry => $"{entry.Key}={entry.Values}")),
				IEnumerable<string> words => FormatList(words.ToArray(), metadata.Flag),
				_ => value.ToString() ?? string.Empty
			};

	/// <summary>
	/// A mapping option's entries in key order, each key with its values space-separated, as
	/// <c>@config</c> lists them one per line.
	/// </summary>
	public static IEnumerable<(string Key, string Values)> Entries(IEnumerable<KeyValuePair<string, string[]>> map)
		=> map
			.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
			.Select(entry => (entry.Key, string.Join(' ', entry.Value ?? [])));

	private static string FormatList(string[] words, bool flagList)
		=> words.Length == 0 ? string.Empty
			: flagList ? " " + string.Join(' ', words)
			: string.Join(' ', words);
}
