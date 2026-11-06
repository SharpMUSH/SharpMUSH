namespace SharpMUSH.Configuration;

/// <summary>What an alias line of a configuration file gives another name to.</summary>
public enum AliasKind
{
	Command,
	Function,
	Flag,
	Power
}

/// <summary>
/// One alias line of a configuration file, in the source server's own names: <paramref name="Alias"/> is to mean
/// <paramref name="Target"/>. A command target may carry switches (<c>@mail/cc</c>), as TinyMUX and RhostMUSH allow.
/// </summary>
/// <param name="Line">The line as the file gave it, for the import report.</param>
public sealed record AliasDirective(AliasKind Kind, string Alias, string Target, string Line);

/// <summary>
/// The alias lines of a <c>mush.cnf</c>, <c>netmux.conf</c> or <c>netrhost.conf</c>, and the alias lines SharpMUSH
/// has nothing for. PennMUSH writes the target first (<c>command_alias @switch @sw</c>, <c>function_alias modulo
/// mod</c>, in <c>alias.cnf</c>); TinyMUX and RhostMUSH write the alias first (<c>alias @sw @switch</c>,
/// <c>flag_alias ab abode</c>, in <c>alias.conf</c>). Neither file is followed when the configuration was uploaded, so
/// a game pastes it into the file it uploads.
/// </summary>
public static class AliasDirectives
{
	private static readonly Dictionary<string, AliasKind> PennMUSH = new(StringComparer.OrdinalIgnoreCase)
	{
		["command_alias"] = AliasKind.Command,
		["function_alias"] = AliasKind.Function
	};

	private static readonly Dictionary<string, AliasKind> TinyMUX = new(StringComparer.OrdinalIgnoreCase)
	{
		["alias"] = AliasKind.Command,
		["function_alias"] = AliasKind.Function,
		["flag_alias"] = AliasKind.Flag,
		["power_alias"] = AliasKind.Power
	};

	// RhostMUSH has no power_alias.
	private static readonly Dictionary<string, AliasKind> RhostMUSH = new(StringComparer.OrdinalIgnoreCase)
	{
		["alias"] = AliasKind.Command,
		["function_alias"] = AliasKind.Function,
		["flag_alias"] = AliasKind.Flag
	};

	/// <summary>The alias directives SharpMUSH reads no equivalent of, and why.</summary>
	private static readonly Dictionary<string, string> NotCarried = new(StringComparer.OrdinalIgnoreCase)
	{
		["attribute_alias"] = "SharpMUSH keeps no per-game attribute aliases",
		["attr_alias"] = "SharpMUSH keeps no per-game attribute aliases; the dialect's own attribute names are read without it",
		["logout_cmd_alias"] = "SharpMUSH has no separate table of commands for players who are not logged in",
		["reserve_alias"] = "SharpMUSH has no reserved alias names"
	};

	private static Dictionary<string, AliasKind> Table(ConfigurationDialect dialect) => dialect switch
	{
		ConfigurationDialect.TinyMUX => TinyMUX,
		ConfigurationDialect.RhostMUSH => RhostMUSH,
		_ => PennMUSH
	};

	/// <summary>Whether <paramref name="name"/> is an alias directive this reader handles, carried or reported.</summary>
	public static bool IsDirective(string name, ConfigurationDialect dialect)
		=> Table(dialect).ContainsKey(name) || NotCarried.ContainsKey(name);

	/// <summary>
	/// The alias lines among <paramref name="lines"/>, each a directive name, its value and the line, in file order.
	/// A line naming only one name, and a directive SharpMUSH has nothing for, are added to
	/// <paramref name="skipped"/>.
	/// </summary>
	public static List<AliasDirective> Read(IEnumerable<(string Name, string Value, string Line)> lines,
		ConfigurationDialect dialect, List<string> skipped)
	{
		var table = Table(dialect);
		var aliases = new List<AliasDirective>();
		foreach (var (name, value, line) in lines)
		{
			if (NotCarried.TryGetValue(name, out var reason))
			{
				skipped.Add($"{line.Trim()}: {reason}, so it was not carried over.");
				continue;
			}

			if (!table.TryGetValue(name, out var kind))
			{
				continue;
			}

			// Both TinyMUX's string_token and PennMUSH's own parse split on any of these.
			var words = value.Split([' ', '\t', '=', ','], StringSplitOptions.RemoveEmptyEntries);
			if (words.Length < 2)
			{
				skipped.Add($"{line.Trim()}: an alias line names a name and what it stands for, so it was not carried over.");
				continue;
			}

			var (alias, target) = dialect == ConfigurationDialect.PennMUSH ? (words[1], words[0]) : (words[0], words[1]);
			aliases.Add(new AliasDirective(kind, alias, target, line.Trim()));
		}

		return aliases;
	}
}
