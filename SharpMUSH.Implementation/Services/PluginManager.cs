using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Services;

/// <summary>
/// Post-build plugin registrar. Reads the already-loaded plugins from the pre-build
/// <see cref="PluginCatalog"/> (the single DLL-load pass happened at <c>Startup.ConfigureServices</c>
/// time) and registers each plugin's <see cref="ICommandSource"/>/<see cref="IFunctionSource"/>
/// contributions into the live <see cref="CommandLibraryService"/>/<see cref="FunctionLibraryService"/>
/// with <c>IsSystem=true</c> (compiled C#, same tier as built-ins).
///
/// <para>This no longer loads any DLL: command/function registration must wait until the libraries and
/// the root <see cref="IServiceProvider"/> exist (post-build, via <c>PluginBootstrapService</c>), whereas
/// the catalog's DI/migration/flag/bridge seams had to be applied during container construction. Loading
/// once in the catalog and replaying here avoids a second McMaster load of the same assemblies.</para>
///
/// Each plugin is wrapped in try/catch so a single bad plugin never aborts boot. Name collisions with
/// existing entries (engine built-ins loaded first) are logged and skipped (the existing definition wins).
/// </summary>
public sealed class PluginManager(
	PluginCatalog catalog,
	LibraryService<string, CommandDefinition> commandLibrary,
	LibraryService<string, FunctionDefinition> functionLibrary,
	IServiceProvider serviceProvider,
	ILogger<PluginManager> logger) : IPluginManager
{
	public Task LoadAllAsync(CancellationToken cancellationToken)
	{
		foreach (var plugin in catalog.Plugins)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var (commandCount, functionCount) = RegisterPlugin(plugin);
			logger.LogInformation(
				"Registered plugin '{Id}' v{Version}: {Commands} command(s), {Functions} function(s).",
				plugin.Id, plugin.Version, commandCount, functionCount);
		}

		return Task.CompletedTask;
	}

	/// <summary>
	/// Run a plugin's <see cref="IPlugin.Initialize"/> then register its command/function contributions
	/// with <c>IsSystem=true</c>. Wrapped so a throwing plugin (in Initialize or in a source enumeration)
	/// is isolated and logged rather than aborting the load. Returns the count of entries actually added.
	/// </summary>
	public (int Commands, int Functions) RegisterPlugin(IPlugin plugin)
	{
		try
		{
			plugin.Initialize(serviceProvider);
			var commandCount = RegisterCommands(plugin);
			var functionCount = RegisterFunctions(plugin);
			return (commandCount, functionCount);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Plugin '{Id}' threw during initialization/registration; skipping it.", plugin.Id);
			return (0, 0);
		}
	}

	private int RegisterCommands(IPlugin plugin)
	{
		if (plugin is not ICommandSource source)
		{
			return 0;
		}

		var added = 0;
		foreach (var definition in source.GetCommands())
		{
			var name = definition.Attribute.Name;
			if (commandLibrary.TryAdd(name, (definition, true)))
			{
				added++;
			}
			else
			{
				logger.LogError(
					"Plugin '{Id}' command '{Name}' collides with an existing command; keeping the existing definition.",
					plugin.Id, name);
			}
		}

		return added;
	}

	private int RegisterFunctions(IPlugin plugin)
	{
		if (plugin is not IFunctionSource source)
		{
			return 0;
		}

		var added = 0;
		foreach (var definition in source.GetFunctions())
		{
			var name = definition.Attribute.Name;
			if (functionLibrary.TryAdd(name, (definition, true)))
			{
				added++;
			}
			else
			{
				logger.LogError(
					"Plugin '{Id}' function '{Name}' collides with an existing function; keeping the existing definition.",
					plugin.Id, name);
			}
		}

		return added;
	}
}
