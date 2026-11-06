using SharpMUSH.Configuration;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.DatabaseConversion.Rhost;
using SharpMUSH.Library.Services.DatabaseConversion.TinyMux;

namespace SharpMUSH.Library.Services.DatabaseConversion;

public partial class PennMUSHDatabaseConverter
{
	/// <summary>A flag or power as the configured aliases are checked against it.</summary>
	private sealed record AliasedDefinition(string Name, string[] Aliases, bool System);

	/// <summary>
	/// Gives the flags and powers the configuration's <c>flag_alias</c> and <c>power_alias</c> lines name
	/// (<see cref="PennMUSHDatabase.ConfiguredAliases"/>) those aliases, once the source's definitions are in.
	/// </summary>
	/// <remarks>
	/// <para>A line names the flag or power by the source's name for it, which is found here as the import maps that
	/// name. An alias that already finds the same flag or power here, by name or as an abbreviation, is left out
	/// without a note: nothing is lost. The rest are reported: one naming nothing here, one whose flag became a role,
	/// a power or a permission (which take no alias), one another definition already answers to, and one for a
	/// built-in flag or power, whose names are SharpMUSH's own and are written back as shipped on every start.</para>
	/// </remarks>
	private async Task ImportConfiguredAliasesAsync(PennMUSHDatabase pennDatabase, PennMUSHConversionContext context,
		CancellationToken cancellationToken)
	{
		if (pennDatabase.ConfiguredAliases.Count == 0)
		{
			return;
		}

		var flags = await _mediator.CreateStream(new GetAllObjectFlagsQuery(), cancellationToken)
			.Select(flag => new AliasedDefinition(flag.Name, flag.Aliases ?? [], flag.System))
			.ToListAsync(cancellationToken);
		var powers = await _mediator.CreateStream(new GetPowersQuery(), cancellationToken)
			.Select(power => new AliasedDefinition(power.Name, power.Aliases, power.System))
			.ToListAsync(cancellationToken);
		var added = new Dictionary<(AliasKind, string), List<string>>();

		foreach (var directive in pennDatabase.ConfiguredAliases)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var isFlag = directive.Kind == AliasKind.Flag;
			var kind = isFlag ? "flag" : "power";
			var definitions = isFlag ? flags : powers;

			var named = isFlag
				? FlagNamed(pennDatabase.Dialect, directive.Target)
				: PowerNamed(pennDatabase.Dialect, directive.Target);
			if (named is { Reason: { } reason })
			{
				NotCarried(reason);
				continue;
			}

			if (Answering(definitions, added, directive.Kind, named.Name!) is not { } definition)
			{
				NotCarried($"SharpMUSH has no {kind} {named.Name}");
				continue;
			}

			if (Answering(definitions, added, directive.Kind, directive.Alias) is { } holder)
			{
				if (!holder.Name.Equals(definition.Name, StringComparison.OrdinalIgnoreCase))
				{
					NotCarried($"{directive.Alias} is already the {holder.Name} {kind} here");
				}

				continue;
			}

			var abbreviated = isFlag
				? (await _mediator.Send(new GetObjectFlagQuery(directive.Alias), cancellationToken))?.Name
				: (await _mediator.Send(new GetPowerQuery(directive.Alias), cancellationToken))?.Name;
			if (definition.Name.Equals(abbreviated, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (definition.System)
			{
				NotCarried($"{definition.Name} is a built-in {kind}, which keeps SharpMUSH's own names" +
					(abbreviated is null ? "" : $", and {directive.Alias} finds {abbreviated} here"));
				continue;
			}

			(added.TryGetValue((directive.Kind, definition.Name), out var list) ? list : added[(directive.Kind, definition.Name)] = [])
				.Add(directive.Alias);

			void NotCarried(string why)
				=> context.Warn(isFlag ? ImportWarningKind.Flags : ImportWarningKind.Powers,
					$"Configuration line {directive.Line}: {why}, so it was not carried over");
		}

		foreach (var ((kind, name), aliases) in added)
		{
			var written = kind == AliasKind.Flag
				? await AddFlagAliasesAsync(name, aliases, cancellationToken)
				: await AddPowerAliasesAsync(name, aliases, cancellationToken);
			if (written)
			{
				context.Notes.Add($"{(kind == AliasKind.Flag ? "Flag" : "Power")} {name}: also answers to {string.Join(", ", aliases)}, from the configuration");
			}
			else
			{
				context.Warn(kind == AliasKind.Flag ? ImportWarningKind.Flags : ImportWarningKind.Powers,
					$"{(kind == AliasKind.Flag ? "Flag" : "Power")} {name}: the aliases {string.Join(", ", aliases)} from the configuration could not be written");
			}
		}
	}

	/// <summary>The definition that answers to <paramref name="name"/> exactly, by its name, an alias it has, or one given it above.</summary>
	private static AliasedDefinition? Answering(List<AliasedDefinition> definitions,
		Dictionary<(AliasKind, string), List<string>> added, AliasKind kind, string name)
		=> definitions.FirstOrDefault(definition => definition.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
			?? definitions.FirstOrDefault(definition => definition.Aliases.Contains(name, StringComparer.OrdinalIgnoreCase)
				|| (added.GetValueOrDefault((kind, definition.Name))?.Contains(name, StringComparer.OrdinalIgnoreCase) ?? false));

	private async Task<bool> AddFlagAliasesAsync(string name, List<string> aliases, CancellationToken cancellationToken)
		=> await _mediator.Send(new GetObjectFlagQuery(name), cancellationToken) is { } flag
			&& await _mediator.Send(new UpdateObjectFlagCommand(flag.Name, [.. flag.Aliases ?? [], .. aliases], flag.Symbol,
				flag.SetPermissions, flag.UnsetPermissions, flag.TypeRestrictions), cancellationToken);

	private async Task<bool> AddPowerAliasesAsync(string name, List<string> aliases, CancellationToken cancellationToken)
		=> await _mediator.Send(new GetPowerQuery(name), cancellationToken) is { } power
			&& await _mediator.Send(new UpdatePowerCommand(power.Name, [.. power.Aliases, .. aliases], power.Symbol,
				power.SetPermissions, power.UnsetPermissions, power.TypeRestrictions), cancellationToken);

	/// <summary>The flag a source's name for one is here, or why it has none.</summary>
	private sealed record NamedDefinition(string? Name, string? Reason);

	private static NamedDefinition FlagNamed(DatabaseDialect dialect, string source)
	{
		switch (dialect)
		{
			case DatabaseDialect.TinyMUX:
				var mux = TinyMuxNames.Flags1.Values.Concat(TinyMuxNames.Flags2.Values).Concat(TinyMuxNames.Flags3.Values)
					.FirstOrDefault(mapping => mapping.Source.Equals(source, StringComparison.OrdinalIgnoreCase));
				return mux switch
				{
					null => new(source, null),
					{ Flags: [var flag, ..] } => new(flag, null),
					{ Power: { } power } => new(null, $"TinyMUX's {mux.Source} flag is the {power} power here, which a flag alias cannot name"),
					_ => new(null, $"the import does not keep TinyMUX's {mux.Source} flag ({mux.Dropped})")
				};
			case DatabaseDialect.RhostMUSH:
				var rhost = RhostNameTables.Bits.FirstOrDefault(bit => bit.Name.Equals(source, StringComparison.OrdinalIgnoreCase));
				return rhost switch
				{
					null => new(source, null),
					{ Flags: [var flag, ..] } => new(flag, null),
					{ Custom: { } custom } => new(custom, null),
					{ Role: { } role } => new(null, $"RhostMUSH's {rhost.Name} flag is the {role} role here, which takes no alias"),
					{ Powers: [var power, ..] } => new(null, $"RhostMUSH's {rhost.Name} flag is the {power} power here, which a flag alias cannot name"),
					_ => new(null, $"the import does not keep RhostMUSH's {rhost.Name} flag ({rhost.Skip})")
				};
			default:
				return new(source, null);
		}
	}

	private static NamedDefinition PowerNamed(DatabaseDialect dialect, string source)
	{
		if (dialect != DatabaseDialect.TinyMUX)
		{
			return new(source, null);
		}

		var mux = TinyMuxNames.Powers1.Values.Concat(TinyMuxNames.Powers2.Values)
			.FirstOrDefault(mapping => mapping.Source.Equals(source, StringComparison.OrdinalIgnoreCase));
		return mux switch
		{
			null => new(source, null),
			{ Power: { } power } => new(power, null),
			_ => new(null, $"TinyMUX's {mux.Source} power is the {mux.Permission} permission here, which takes no alias")
		};
	}
}
