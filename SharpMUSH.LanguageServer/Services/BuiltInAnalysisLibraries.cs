using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Generated;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Services;

namespace SharpMUSH.LanguageServer.Services;

/// <summary>Built-in metadata and shipped aliases, without runtime implementations or game services.</summary>
internal static class BuiltInAnalysisLibraries
{
	public static (FunctionLibraryService Functions, CommandLibraryService Commands) Create()
	{
		var functions = new FunctionLibraryService();
		var commands = new CommandLibraryService();
		var aliases = AliasOptions.Default;

		foreach (var (name, attribute) in FunctionLibrary.CreateMetadata())
		{
			var definition = new FunctionDefinition(attribute,
				static _ => throw new NotSupportedException("Standalone analysis cannot evaluate functions."));
			functions.Add(name, (definition, true));
			functions.ReserveSystemName(name);
			foreach (var alias in aliases.FunctionAliases.GetValueOrDefault(name) ?? [])
			{
				functions.Add(alias, (definition, true));
				functions.ReserveSystemName(alias);
			}
		}

		foreach (var (name, attribute) in CommandLibrary.CreateMetadata())
		{
			var definition = new CommandDefinition(attribute,
				static _ => throw new NotSupportedException("Standalone analysis cannot evaluate commands."));
			commands.Add(name, (definition, true));
			foreach (var alias in aliases.CommandAliases.GetValueOrDefault(name) ?? [])
				commands.Add(alias, (definition, true));
		}

		return (functions, commands);
	}
}
