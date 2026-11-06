using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Softcode;

namespace SharpMUSH.Implementation.Services;

/// <summary>
/// The command and function aliases an imported configuration gives, checked against this engine's commands and
/// functions, and the lines that could not be carried over with the reason for each.
/// </summary>
/// <param name="Commands">The aliases to add, keyed as <see cref="AliasOptions.CommandAliases"/> is.</param>
/// <param name="Functions">The aliases to add, keyed as <see cref="AliasOptions.FunctionAliases"/> is.</param>
/// <param name="CommandRestrictions">
/// The imported command restrictions that stay: every one for a PennMUSH file, and for a TinyMUX or RhostMUSH file
/// the access lines that cannot leave a command less restricted than it is here.
/// </param>
/// <param name="FunctionRestrictions">The imported function restrictions, each under the name this engine knows the function by.</param>
/// <param name="NotCarried">Each alias or restriction line left out, and why.</param>
/// <remarks>A restriction on a name the source's dialect gives a built-in (TinyMUX's <c>secs2time</c>) is keyed by
/// the built-in's name here, since a call by the dialect's name runs it and is held to its restriction.</remarks>
public sealed record ConfiguredAliases(
	Dictionary<string, string[]> Commands,
	Dictionary<string, string[]> Functions,
	Dictionary<string, string[]> CommandRestrictions,
	Dictionary<string, string[]> FunctionRestrictions,
	IReadOnlyList<string> NotCarried)
{
	/// <summary><paramref name="current"/> with these aliases added to the ones it has.</summary>
	public AliasOptions Over(AliasOptions current)
		=> new(Merge(current.FunctionAliases, Functions), Merge(current.CommandAliases, Commands));

	private static Dictionary<string, string[]> Merge(Dictionary<string, string[]> now, Dictionary<string, string[]> added)
	{
		var merged = new Dictionary<string, string[]>(now, now.Comparer);
		foreach (var (target, aliases) in added)
		{
			var existing = merged.GetValueOrDefault(target) ?? [];
			merged[target] = [.. existing, .. aliases.Where(alias => !existing.Contains(alias, StringComparer.OrdinalIgnoreCase))];
		}

		return merged;
	}
}

/// <summary>
/// Turns an imported configuration's command and function alias lines (<see cref="PennMushConfigImport.Aliases"/>)
/// into aliases of this engine's built-ins, as <see cref="AliasOptions"/> holds them, and says what it could not.
/// </summary>
/// <remarks>
/// <para>A target is found as typed input finds it: a command or function of that name or alias, else the one the
/// source's dialect means by it (<see cref="SoftcodeDialects"/>). An alias table names only built-ins, so a target
/// that is a softcode <c>@function</c> or a plugin's is reported, as is one with a switch its command lacks.</para>
/// <para>An alias that already names its target here, by name, by the dialect or as the abbreviation of a command,
/// is left out without a note: nothing is lost. One that names something else is reported, and so is a second line
/// for an alias, which TinyMUX and RhostMUSH refuse while the first stands.</para>
/// <para>A restriction naming a command or function this engine does not have is reported too; it stays in the
/// configuration, where it does nothing, as PennMUSH logs and ignores one. A restriction on an alias the same file
/// makes is checked against the command the alias names.</para>
/// <para>A TinyMUX or RhostMUSH access line adds its words to what the command already required there, while a
/// SharpMUSH restriction takes the place of the command's own. So an access line stays only where that cannot open
/// the command further: on a command with no lock of its own here, or when it disables the command or leaves it to
/// God. Any other is reported, and the command keeps SharpMUSH's restriction.</para>
/// </remarks>
public sealed class ConfiguredAliasImport(
	ILibraryProvider<CommandDefinition> commands,
	ILibraryProvider<FunctionDefinition> functions)
{
	public ConfiguredAliases Resolve(PennMushConfigImport import, ConfigurationDialect dialect)
	{
		var overlay = dialect switch
		{
			ConfigurationDialect.TinyMUX => SoftcodeDialects.Mux,
			ConfigurationDialect.RhostMUSH => SoftcodeDialects.Rhost,
			_ => null
		};
		var notCarried = new List<string>();
		var commandAliases = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		var functionAliases = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		var claimed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var commandLibrary = commands.Get();
		var functionLibrary = functions.Get();

		foreach (var line in import.Aliases)
		{
			var directive = ThroughEarlierAlias(line, claimed, commandLibrary, functionLibrary);
			var resolved = directive.Kind switch
			{
				AliasKind.Command => Command(directive, overlay, commandLibrary),
				AliasKind.Function => Function(directive, overlay, functionLibrary),
				_ => null
			};

			switch (resolved)
			{
				case null:
					continue;
				case { Reason: { } reason }:
					notCarried.Add($"{directive.Line}: {reason}, so it was not carried over.");
					continue;
				case { Target: { } target }:
					var key = $"{directive.Kind}:{directive.Alias}";
					if (claimed.TryGetValue(key, out var earlier))
					{
						if (!earlier.Equals(target, StringComparison.OrdinalIgnoreCase))
						{
							notCarried.Add($"{directive.Line}: an earlier line already made {directive.Alias} an alias of {earlier}, so it was not carried over.");
						}

						continue;
					}

					claimed[key] = target;
					var into = directive.Kind == AliasKind.Command ? commandAliases : functionAliases;
					(into.TryGetValue(target, out var list) ? list : into[target] = []).Add(directive.Alias);
					continue;
			}
		}

		var aliasTargets = commandAliases
			.SelectMany(entry => entry.Value.Select(alias => (Alias: alias, Target: entry.Key.Split('/')[0])))
			.ToDictionary(entry => entry.Alias, entry => entry.Target, StringComparer.OrdinalIgnoreCase);
		var restrictions = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
		foreach (var (name, restriction) in import.Options.Restriction.CommandRestrictions)
		{
			var key = name;
			var found = FindCommand(commandLibrary, name, out var command)
				|| (aliasTargets.TryGetValue(name, out var target) && FindCommand(commandLibrary, target, out command));
			if (!found && overlay?.CommandFor(name) is { } meant && !meant.Contains('/')
				&& FindCommand(commandLibrary, meant, out command))
			{
				(found, key) = (true, meant);
			}

			if (!found)
			{
				notCarried.Add($"The restriction on {name}: SharpMUSH has no command {name}, so it has no effect.");
			}
			else if (dialect != ConfigurationDialect.PennMUSH && WidensCommand(command.LibraryInformation.Attribute, restriction[0]))
			{
				notCarried.Add($"The restriction on {name} ({restriction[0]}): SharpMUSH's {command.LibraryInformation.Attribute.Name} has a restriction of its own, " +
					"which this one would replace rather than add to, so it was not carried over and SharpMUSH's stays.");
				continue;
			}

			restrictions.TryAdd(key, restriction);
		}

		var newFunctionAliases = functionAliases.Values.SelectMany(aliases => aliases).ToHashSet(StringComparer.OrdinalIgnoreCase);
		var functionRestrictions = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
		foreach (var (name, restriction) in import.Options.Restriction.FunctionRestrictions)
		{
			if (functionLibrary.ContainsKey(name) || newFunctionAliases.Contains(name))
			{
				functionRestrictions.TryAdd(name, restriction);
			}
			else if (overlay?.FunctionFor(name) is { } meant && functionLibrary.ContainsKey(meant))
			{
				functionRestrictions.TryAdd(meant, restriction);
			}
			else
			{
				notCarried.Add($"The restriction on {name}(): SharpMUSH has no function {name}(), so it has no effect.");
			}
		}

		return new ConfiguredAliases(
			commandAliases.ToDictionary(entry => entry.Key, entry => entry.Value.ToArray()),
			functionAliases.ToDictionary(entry => entry.Key, entry => entry.Value.ToArray()),
			restrictions,
			functionRestrictions,
			notCarried);
	}

	/// <summary>
	/// Whether <paramref name="restriction"/>, put in place of <paramref name="attribute"/>'s own, could let in someone
	/// the command turns away now: it can unless the command has no lock and is not God's alone, or the words disable
	/// it or leave it to God.
	/// </summary>
	private static bool WidensCommand(Library.Attributes.SharpCommandAttribute attribute, string restriction)
	{
		var words = restriction.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (words.Any(word => word.Equals("nobody", StringComparison.OrdinalIgnoreCase)
				|| word.Equals("god", StringComparison.OrdinalIgnoreCase)))
		{
			return false;
		}

		return !string.IsNullOrWhiteSpace(attribute.CommandLock) || attribute.Behavior.HasFlag(CommandBehavior.God);
	}

	/// <summary>
	/// A line whose target is an alias an earlier line of the same file made names that alias's target, as it does in
	/// the source, where the earlier alias is in the table by then. The live table does not have it yet.
	/// </summary>
	private static AliasDirective ThroughEarlierAlias(AliasDirective directive, Dictionary<string, string> claimed,
		Library.Services.LibraryService<string, CommandDefinition> commandLibrary,
		Library.Services.LibraryService<string, FunctionDefinition> functionLibrary)
	{
		var root = directive.Kind == AliasKind.Command ? directive.Target.Split('/')[0] : directive.Target.TrimEnd('(', ')');
		var live = directive.Kind == AliasKind.Command ? commandLibrary.ContainsKey(root) : functionLibrary.ContainsKey(root);
		return live || !claimed.TryGetValue($"{directive.Kind}:{root}", out var earlier)
			? directive
			: directive with { Target = earlier + directive.Target[root.Length..] };
	}

	/// <summary>
	/// A command in the live table, or a built-in the configuration has disabled, which is out of the table until a change
	/// re-enables it and is still there to take an alias or a restriction.
	/// </summary>
	private bool FindCommand(Library.Services.LibraryService<string, CommandDefinition> library, string name,
		out (CommandDefinition LibraryInformation, bool IsSystem) command)
	{
		if (library.TryGetValue(name, out command))
		{
			return true;
		}

		var builtins = commands.Builtins.Where(entry => entry.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Take(1).ToList();
		command = builtins.Count == 1 ? (builtins[0].Value, true) : default;
		return builtins.Count == 1;
	}

	/// <summary>What a line's resolution came to: the alias table key it joins, a reason it does not, or neither when it is already in force.</summary>
	private sealed record Resolution(string? Target, string? Reason);

	private static readonly Resolution InForce = new(null, null);

	/// <summary>
	/// An alias the live table already has for its target: in force with nothing to keep when it is the built-in's own
	/// name or one SharpMUSH ships, else one an earlier import or <c>@config</c> added, which the options written now
	/// have to name again or it is gone after a restart.
	/// </summary>
	private static Resolution Kept(Dictionary<string, string[]> shipped, string target, string alias)
		=> alias.Equals(target, StringComparison.OrdinalIgnoreCase)
			|| (shipped.TryGetValue(target, out var names) && names.Contains(alias, StringComparer.OrdinalIgnoreCase))
				? InForce
				: new Resolution(target, null);

	private Resolution Command(AliasDirective directive, SoftcodeDialect? overlay,
		Library.Services.LibraryService<string, CommandDefinition> library)
	{
		var (root, switches) = Split(directive.Target);
		if (!FindCommand(library, root, out var found)
			&& overlay?.CommandFor(root) is { } meant)
		{
			var (meantRoot, meantSwitches) = Split(meant);
			(root, switches) = (meantRoot, [.. meantSwitches, .. switches]);
			FindCommand(library, root, out found);
		}

		if (found.LibraryInformation.Attribute is not { } attribute)
		{
			return new Resolution(null, $"SharpMUSH has no command {directive.Target}");
		}

		if (commands.Builtins.FirstOrDefault(entry => ReferenceEquals(entry.Value.Attribute, attribute)).Key is not { } name)
		{
			return new Resolution(null, $"{root} is not a built-in command, and only a built-in takes an alias");
		}

		string[] wanted = [.. found.LibraryInformation.ImpliedSwitches.Select(s => s.ToUpperInvariant()), .. switches.Select(s => s.ToUpperInvariant())];
		var known = attribute.Switches ?? [];
		if (!known.Contains("*") && wanted.FirstOrDefault(s => !known.Contains(s, StringComparer.OrdinalIgnoreCase)) is { } unknown)
		{
			return new Resolution(null, $"SharpMUSH's {name} has no /{unknown.ToLowerInvariant()} switch");
		}

		var target = wanted.Length == 0 ? name : $"{name}/{string.Join('/', wanted)}";

		if (library.TryGetValue(directive.Alias, out var taken))
		{
			return ReferenceEquals(taken.LibraryInformation.Attribute, attribute)
				&& taken.LibraryInformation.ImpliedSwitches.SequenceEqual(wanted, StringComparer.OrdinalIgnoreCase)
					? Kept(AliasOptions.Default.CommandAliases, target, directive.Alias)
					: new Resolution(null, $"{directive.Alias} is already SharpMUSH's {taken.LibraryInformation.Attribute.Name}");
		}

		if (overlay?.CommandFor(directive.Alias) is { } dialectMeaning
			&& dialectMeaning.Equals(target, StringComparison.OrdinalIgnoreCase))
		{
			return InForce;
		}

		return new Resolution(target, null);
	}

	private Resolution Function(AliasDirective directive, SoftcodeDialect? overlay,
		Library.Services.LibraryService<string, FunctionDefinition> library)
	{
		var targetName = directive.Target.TrimEnd('(', ')');
		if (!library.TryGetValue(targetName, out var found)
			&& overlay?.FunctionFor(targetName) is { } meant)
		{
			library.TryGetValue(meant, out found);
		}

		if (found.LibraryInformation.Attribute is not { } attribute)
		{
			return new Resolution(null, $"SharpMUSH has no function {targetName}()");
		}

		if (functions.Builtins.FirstOrDefault(entry => ReferenceEquals(entry.Value.Attribute, attribute)).Key is not { } name)
		{
			return new Resolution(null, $"{targetName}() is not a built-in function, and only a built-in takes an alias");
		}

		var alias = directive.Alias.TrimEnd('(', ')');
		if (library.TryGetValue(alias, out var taken))
		{
			return ReferenceEquals(taken.LibraryInformation.Attribute, attribute)
				? Kept(AliasOptions.Default.FunctionAliases, name, alias)
				: new Resolution(null, $"{alias}() is already SharpMUSH's {taken.LibraryInformation.Attribute.Name.ToLowerInvariant()}()");
		}

		return overlay?.FunctionFor(alias) is { } dialectMeaning
			&& library.TryGetValue(dialectMeaning, out var meaning)
			&& ReferenceEquals(meaning.LibraryInformation.Attribute, attribute)
				? InForce
				: new Resolution(name, null);
	}

	private static (string Root, string[] Switches) Split(string command)
	{
		var parts = command.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		return parts.Length == 0 ? (command, []) : (parts[0], parts[1..]);
	}
}
