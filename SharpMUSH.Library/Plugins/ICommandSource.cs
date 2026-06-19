using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Library.Plugins;

/// <summary>
/// Contribution surface for engine commands. A plugin entry type implements this (PluginBase does so
/// by default, reflecting the generator-produced <c>SharpMUSH.Implementation.Generated.CommandLibrary.Commands</c>).
/// </summary>
public interface ICommandSource
{
	IEnumerable<CommandDefinition> GetCommands();
}
