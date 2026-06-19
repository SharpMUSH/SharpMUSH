using System.Reflection;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Library.Plugins;

/// <summary>
/// Convenience base class for plugin authors. A plugin is written exactly like in-tree code:
/// add <c>[SharpCommand]</c>/<c>[SharpFunction]</c> methods (with the generator referenced as an
/// analyzer) and one <c>[SharpPlugin] sealed class MyPlugin : PluginBase</c> overriding <see cref="Id"/>.
///
/// The default <see cref="GetCommands"/>/<see cref="GetFunctions"/> reflect the generator-produced
/// <c>SharpMUSH.Implementation.Generated.CommandLibrary.Commands</c> and
/// <c>SharpMUSH.Implementation.Generated.FunctionLibrary.Functions</c> static fields in the plugin's
/// own assembly. Because <see cref="CommandDefinition"/>/<see cref="FunctionDefinition"/> are declared
/// as shared types by the host loader, the reflected values unify with the host's types and cast cleanly.
/// </summary>
public abstract class PluginBase : IPlugin, ICommandSource, IFunctionSource
{
	private const string CommandLibraryTypeName = "SharpMUSH.Implementation.Generated.CommandLibrary";
	private const string FunctionLibraryTypeName = "SharpMUSH.Implementation.Generated.FunctionLibrary";
	private const string CommandsFieldName = "Commands";
	private const string FunctionsFieldName = "Functions";

	/// <inheritdoc />
	public abstract string Id { get; }

	/// <inheritdoc />
	public virtual string Version => "1.0.0";

	/// <inheritdoc />
	public virtual IReadOnlyList<string> Dependencies => [];

	/// <inheritdoc />
	public virtual int Priority => 0;

	/// <inheritdoc />
	public virtual void Initialize(IServiceProvider services)
	{
		// No-op by default. Plugin commands/functions resolve services at call time via
		// parser.ServiceProvider.GetRequiredService<T>(); override only for one-time setup.
	}

	/// <inheritdoc />
	public virtual IEnumerable<CommandDefinition> GetCommands()
		=> ReadGeneratedDictionary<CommandDefinition>(CommandLibraryTypeName, CommandsFieldName);

	/// <inheritdoc />
	public virtual IEnumerable<FunctionDefinition> GetFunctions()
		=> ReadGeneratedDictionary<FunctionDefinition>(FunctionLibraryTypeName, FunctionsFieldName);

	private IEnumerable<T> ReadGeneratedDictionary<T>(string typeName, string fieldName)
	{
		var assembly = GetType().Assembly;
		var libraryType = assembly.GetType(typeName, throwOnError: false);
		if (libraryType is null)
		{
			// No [SharpCommand]/[SharpFunction] methods in this plugin (or generator not referenced).
			return [];
		}

		var field = libraryType.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
		if (field?.GetValue(null) is not System.Collections.IDictionary dictionary)
		{
			return [];
		}

		var results = new List<T>();
		foreach (var value in dictionary.Values)
		{
			if (value is T typed)
			{
				results.Add(typed);
			}
		}

		return results;
	}
}
