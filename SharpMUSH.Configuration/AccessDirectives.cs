namespace SharpMUSH.Configuration;

/// <summary>
/// The <c>access</c> and <c>function_access</c> lines of a TinyMUX or RhostMUSH configuration, read as the
/// <c>restrict_command</c> and <c>restrict_function</c> restrictions SharpMUSH applies, and the access lines it has
/// nothing for.
/// </summary>
/// <remarks>
/// <para>Both servers give a line as a name and permission words, each word set on the name's permissions or, with
/// <c>!</c>, cleared from them (<c>cf_modify_bits</c>), so the lines for one name add up. A word may be abbreviated to
/// the length the server's own table allows (<c>access_nametab</c>).</para>
/// <para>A word SharpMUSH has a restriction word for is carried over as that word. A clearing word is not: it
/// loosens the source server's own default, which a SharpMUSH command or function does not share. Of the words that
/// say who may use the name (<c>wizard</c>, <c>builder</c>, a RhostMUSH level), any one is enough, so leaving out one
/// SharpMUSH has nothing for narrows the line rather than widening it; a line none of whose such words is carried
/// over is not carried over at all, and the name keeps SharpMUSH's own restriction. Every word left out is
/// reported.</para>
/// </remarks>
public static class AccessDirectives
{
	/// <summary>A permission word: its name, the shortest abbreviation the server takes, and what it is here.</summary>
	/// <param name="Command">The <c>restrict_command</c> word, or null for none.</param>
	/// <param name="Function">The <c>restrict_function</c> word, or null for none.</param>
	/// <param name="Grants">Whether the word says who may use the name, rather than who may not.</param>
	private sealed record Word(string Name, int Shortest, string? Command, string? Function, bool Grants);

	private static Word Who(string name, int shortest, string? command = null, string? function = null)
		=> new(name, shortest, command, function, true);

	private static Word Not(string name, int shortest, string? command = null, string? function = null)
		=> new(name, shortest, command, function, false);

	/// <summary>TinyMUX's <c>access_nametab</c> (<c>command.cpp</c>).</summary>
	private static readonly Word[] TinyMUX =
	[
		Who("builder", 6, "builder"),
		Not("dark", 4),
		Not("disabled", 4, "nobody", "nobody"),
		Not("global_build", 8),
		Not("global_interp", 8),
		Who("god", 2, "god", "god"),
		Who("head", 2),
		Who("immortal", 3, "immortal"),
		Not("need_location", 6),
		Not("need_contents", 6),
		Not("need_player", 6, "player"),
		Not("no_haven", 4),
		Not("no_robot", 4),
		Not("no_slave", 5),
		Not("no_suspect", 5),
		Not("no_guest", 5, "noguest", "noguest"),
		Not("no_uninspected", 5),
		Who("robot", 2),
		Who("staff", 4),
		Not("static", 4),
		Who("uninspected", 5),
		Who("wizard", 3, "wizard", "wizard")
	];

	/// <summary>
	/// RhostMUSH's <c>access_nametab</c> and <c>access_nametab2</c> (<c>command.c</c>). Its <c>royalty</c> is the wizard
	/// level, which a RhostMUSH import gives the <c>wizard</c> role.
	/// </summary>
	private static readonly Word[] RhostMUSH =
	[
		Who("god", 2, "god", "god"),
		Who("immortal", 3),
		Who("royalty", 3, "wizard", "wizard"),
		Who("wizard", 3, "wizard", "wizard"),
		Who("councilor", 4),
		Who("architect", 4),
		Who("guildmaster", 5),
		Who("robot", 2),
		Not("no_haven", 4),
		Not("no_robot", 4),
		Not("no_slave", 5),
		Not("no_suspect", 5),
		Not("no_guest", 5, "noguest", "noguest"),
		Not("no_wanderer", 5),
		Not("global_build", 8),
		Not("global_interp", 8),
		Not("disabled", 4, "nobody", "nobody"),
		Not("need_location", 6),
		Not("need_contents", 9),
		Not("need_player", 6, "player"),
		Not("dark", 4),
		Not("ignore", 6),
		Not("ignore_mortal", 8),
		Not("ignore_gm", 8),
		Not("ignore_arch", 8),
		Not("ignore_counc", 8),
		Not("ignore_royal", 8),
		Not("ignore_immortal", 8),
		Not("ignore_zone", 8),
		Not("disable_zone", 8),
		Not("logflag", 3),
		Not("no_code", 5),
		Not("no_eval", 5),
		Not("eval", 5),
		Not("bypass", 5),
		Not("noparse", 5),
		Not("ansiterm", 5)
	];

	/// <summary>The access directives SharpMUSH reads no equivalent of, and why.</summary>
	private static readonly Dictionary<string, string> NotCarried = new(StringComparer.OrdinalIgnoreCase)
	{
		["attr_access"] = "SharpMUSH sets a standard attribute's flags with @attribute/access",
		["attr_cmd_access"] = "SharpMUSH has no permissions on setting an attribute with its @-command apart from the attribute's own",
		["config_access"] = "SharpMUSH's configuration permissions are the config.* permissions of roles",
		["flag_access"] = "SharpMUSH sets who may set a flag with @flag/restrict",
		["list_access"] = "SharpMUSH has no permissions on @list's topics",
		["logout_cmd_access"] = "SharpMUSH has no separate table of commands for players who are not logged in",
		["power_access"] = "SharpMUSH's powers are permissions, given through roles",
		["user_attr_access"] = "SharpMUSH sets the flags a new attribute is created with per attribute, with @attribute/access"
	};

	/// <summary>Whether <paramref name="name"/> is an access directive this reader handles, carried or reported.</summary>
	public static bool IsDirective(string name, ConfigurationDialect dialect)
		=> dialect != ConfigurationDialect.PennMUSH
			&& (name.Equals("access", StringComparison.OrdinalIgnoreCase)
				|| name.Equals("function_access", StringComparison.OrdinalIgnoreCase)
				|| NotCarried.ContainsKey(name));

	/// <summary>
	/// The command and function restrictions the access lines among <paramref name="lines"/> carry, in the shape
	/// <see cref="Options.RestrictionOptions"/> holds; each line or word left out is added to <paramref name="skipped"/>.
	/// A PennMUSH file has none: its <c>restrict_command</c> and <c>restrict_function</c> are read as they are.
	/// </summary>
	public static (Dictionary<string, string[]> Commands, Dictionary<string, string[]> Functions) Read(
		IEnumerable<(string Name, string Value, string Line)> lines, ConfigurationDialect dialect, List<string> skipped)
	{
		var commands = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		var functions = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		if (dialect == ConfigurationDialect.PennMUSH)
		{
			return ([], []);
		}

		var table = dialect == ConfigurationDialect.RhostMUSH ? RhostMUSH : TinyMUX;
		var refusedCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var refusedFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var (name, value, line) in lines)
		{
			if (NotCarried.TryGetValue(name, out var reason))
			{
				skipped.Add($"{line.Trim()}: {reason}, so it was not carried over.");
				continue;
			}

			var isFunction = name.Equals("function_access", StringComparison.OrdinalIgnoreCase);
			if (!isFunction && !name.Equals("access", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var words = value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
			if (words.Length < 2)
			{
				skipped.Add($"{line.Trim()}: an access line names a {(isFunction ? "function" : "command")} and its permissions, so it was not carried over.");
				continue;
			}

			var target = words[0];
			if (!isFunction && target.Contains('/'))
			{
				skipped.Add($"{line.Trim()}: SharpMUSH restricts a command, not one of its switches, so it was not carried over.");
				continue;
			}

			var into = isFunction ? functions : commands;
			var refused = isFunction ? refusedFunctions : refusedCommands;
			if (refused.Contains(target))
			{
				skipped.Add($"{line.Trim()}: an earlier access line for {target} could not be carried over, and this one alone would let in someone the source turns away, so it was not carried over.");
				continue;
			}

			var (carried, refusedLine) = Translate(line.Trim(), words[1..], table, isFunction, skipped);
			if (refusedLine)
			{
				// The source's lines for one name add up, so what the others carry would stand without this line's
				// requirement: none of them is kept.
				refused.Add(target);
				if (into.Remove(target))
				{
					skipped.Add($"{line.Trim()}: the earlier access lines for {target} were not carried over either, since without this one they would let in someone the source turns away.");
				}

				continue;
			}

			if (carried.Count == 0)
			{
				continue;
			}

			var restriction = into.TryGetValue(target, out var earlier) ? earlier : into[target] = [];
			restriction.AddRange(carried.Where(word => !restriction.Contains(word, StringComparer.OrdinalIgnoreCase)));
		}

		return (Shape(commands), Shape(functions));

		static Dictionary<string, string[]> Shape(Dictionary<string, List<string>> restrictions)
			=> restrictions.ToDictionary(entry => entry.Key, entry => new[] { string.Join(' ', entry.Value) },
				StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>
	/// The SharpMUSH restriction words <paramref name="words"/> become, and whether the line is refused: it says who
	/// may use the name and SharpMUSH has none of those words. What is left out is reported against <paramref name="line"/>.
	/// </summary>
	private static (List<string> Carried, bool Refused) Translate(string line, string[] words, Word[] table, bool isFunction, List<string> skipped)
	{
		var carried = new List<string>();
		var left = new List<string>();
		var grants = 0;
		var grantsCarried = 0;
		foreach (var given in words)
		{
			if (given.StartsWith('!'))
			{
				left.Add($"{given} (it loosens the source's own default, which SharpMUSH's does not share)");
				continue;
			}

			if (Find(table, given) is not { } word)
			{
				left.Add($"{given} (not a permission the source server has)");
				continue;
			}

			grants += word.Grants ? 1 : 0;
			if ((isFunction ? word.Function : word.Command) is { } restriction)
			{
				grantsCarried += word.Grants ? 1 : 0;
				carried.Add(restriction);
			}
			else
			{
				left.Add(word.Name);
			}
		}

		if (grants > 0 && grantsCarried == 0)
		{
			skipped.Add($"{line}: SharpMUSH has no restriction for {string.Join(", ", left)}, so it was not carried over and SharpMUSH's own restriction stays.");
			return ([], true);
		}

		if (left.Count > 0)
		{
			skipped.Add(carried.Count > 0
				? $"{line}: carried over as {string.Join(' ', carried)}; SharpMUSH has no restriction for {string.Join(", ", left)}."
				: $"{line}: SharpMUSH has no restriction for {string.Join(", ", left)}, so it was not carried over.");
		}

		return (carried, false);
	}

	/// <summary>The word <paramref name="given"/> names, in full or abbreviated to at least its shortest form.</summary>
	private static Word? Find(Word[] table, string given)
		=> table.FirstOrDefault(word => given.Length >= word.Shortest
			&& word.Name.StartsWith(given, StringComparison.OrdinalIgnoreCase));
}
