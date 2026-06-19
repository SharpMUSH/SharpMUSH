using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Library.Plugins;

/// <summary>
/// Contribution surface for engine functions. A plugin entry type implements this (PluginBase does so
/// by default, reflecting the generator-produced <c>SharpMUSH.Implementation.Generated.FunctionLibrary.Functions</c>).
/// </summary>
public interface IFunctionSource
{
	IEnumerable<FunctionDefinition> GetFunctions();
}
