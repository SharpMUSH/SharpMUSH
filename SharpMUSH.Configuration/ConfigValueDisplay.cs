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
	public static string Format(object? value, SharpConfigAttribute metadata)
		=> metadata.Dbref
			? value is null ? Nothing : $"#{value}"
			: value switch
			{
				bool flag => flag ? "Yes" : "No",
				null => string.Empty,
				_ => value.ToString() ?? string.Empty
			};
}
