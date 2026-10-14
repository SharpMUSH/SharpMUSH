using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.CodeAnalysis.Catalog;

public static class AnalysisCatalogAnalyzer
{
	private sealed class Options(SharpMUSHOptions options) : IOptionsWrapper<SharpMUSHOptions> { public SharpMUSHOptions CurrentValue => options; }
	private sealed class Empty : IServiceProvider { public object? GetService(Type type) => null; }
	public static IMushCodeAnalyzer Create(AnalysisCatalogSnapshot snapshot, SharpMUSHOptions options)
	{
		var functions = new FunctionLibraryService();
		var commands = new CommandLibraryService();
		foreach (var entry in snapshot.Entries)
		{
			// Owner-local functions are invoked through localfun, not as ordinary global calls.
			if (entry.Origin == "owner-local") continue;
			if (entry.Kind == "function")
			{
				var attribute = new SharpFunctionAttribute
				{
					Name = entry.Name, MinArgs = entry.MinArgs, MaxArgs = entry.MaxArgs ?? int.MaxValue,
					Flags = Enum.Parse<FunctionFlags>(entry.Flags), ParameterNames = entry.ParameterNames.ToArray(), Restrict = entry.Restrictions.ToArray()
				};
				var definition = new FunctionDefinition(attribute, _ => throw new NotSupportedException("Analysis cannot evaluate softcode."));
				foreach (var name in entry.Aliases.Prepend(entry.Name)) functions.Add(name, (definition, entry.Origin == "compiled"));
			}
			else if (entry.Kind == "command")
			{
				var attribute = new SharpCommandAttribute
				{
					Name = entry.Name, MinArgs = entry.MinArgs, MaxArgs = entry.MaxArgs ?? int.MaxValue,
					Behavior = Enum.Parse<CommandBehavior>(entry.Flags), CommandLock = entry.Restrictions.FirstOrDefault() ?? string.Empty,
					ParameterNames = entry.ParameterNames.ToArray(), Switches = entry.Switches.ToArray()
				};
				var definition = new CommandDefinition(attribute, _ => throw new NotSupportedException("Analysis cannot evaluate softcode."));
				foreach (var name in entry.Aliases.Prepend(entry.Name)) commands.Add(name, (definition, entry.Origin == "compiled"));
			}
		}
		return new MushCodeAnalyzer(new MUSHCodeParser(NullLogger<MUSHCodeParser>.Instance, functions, commands, new Options(options), new Empty()));
	}
}
