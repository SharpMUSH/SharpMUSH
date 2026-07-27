using McMaster.NETCore.Plugins;
using Microsoft.Extensions.Logging;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Services;

/// <summary>
/// Post-build plugin registrar and Phase 3 runtime unload/reload driver. Reads the already-loaded plugins
/// from the pre-build <see cref="PluginCatalog"/> (the single DLL-load pass happened at
/// <c>Startup.ConfigureServices</c> time) and registers each plugin's <see cref="ICommandSource"/>/
/// <see cref="IFunctionSource"/> contributions into the live <see cref="CommandLibraryService"/>/
/// <see cref="FunctionLibraryService"/> with <c>IsSystem=true</c> (compiled C#, same tier as built-ins).
///
/// <para>For each registered plugin the manager also <b>tracks</b> the <see cref="PluginLoader"/> handle (so
/// the collectible ALC can be torn down later) and the exact set of command/function names it added (so they
/// can be removed on unload). The per-parse command trie rebuilds from the live library, so removing the
/// library entries is sufficient — no trie surgery is needed.</para>
///
/// <para><b>Unloadable vs load-once.</b> Only a plugin that contributes <i>only</i> commands/functions can be
/// unloaded/reloaded at runtime: its work is fully captured by the two libraries the manager owns. A plugin
/// that also contributes DI services, migrations, flags, or a NATS bridge subscription is <i>load-once</i> —
/// that state is captured by the container, the database, or the bridge and cannot be reversed without a
/// server restart — so <see cref="UnloadAsync"/>/<see cref="ReloadAsync"/> refuse it with a clear message.</para>
///
/// Each plugin is wrapped in try/catch so a single bad plugin never aborts boot. Name collisions with
/// existing entries (engine built-ins loaded first) are logged and skipped (the existing definition wins).
/// </summary>
public sealed class PluginManager(
	PluginCatalog catalog,
	LibraryService<string, CommandDefinition> commandLibrary,
	LibraryService<string, FunctionDefinition> functionLibrary,
	IServiceProvider serviceProvider,
	ILogger<PluginManager> logger,
	IPluginChangeNotifier? changeNotifier = null) : IPluginManager
{
	/// <summary>What the manager remembers about a registered plugin so it can unload/reload it.</summary>
	private sealed class TrackedPlugin(
		IPlugin plugin,
		PluginLoader? loader,
		string? dllPath,
		bool isUnloadable)
	{
		public IPlugin Plugin { get; set; } = plugin;

		/// <summary>The collectible loader to dispose on unload, or <c>null</c> for a non-DLL (test) plugin.</summary>
		public PluginLoader? Loader { get; set; } = loader;

		/// <summary>The DLL to reload from, or <c>null</c> for a non-DLL (test) plugin.</summary>
		public string? DllPath { get; } = dllPath;

		public bool IsUnloadable { get; } = isUnloadable;

		/// <summary>Command names this plugin added to the live library (so they can be removed on unload).</summary>
		public List<string> CommandNames { get; } = [];

		/// <summary>Function names this plugin added to the live library (so they can be removed on unload).</summary>
		public List<string> FunctionNames { get; } = [];
	}

	private readonly Dictionary<string, TrackedPlugin> _tracked = new(StringComparer.OrdinalIgnoreCase);
	private readonly object _gate = new();

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
	/// with <c>IsSystem=true</c>, tracking its loader and the names it added so it can later be unloaded.
	/// Wrapped so a throwing plugin (in Initialize or in a source enumeration) is isolated and logged rather
	/// than aborting the load. Returns the count of entries actually added.
	/// </summary>
	public (int Commands, int Functions) RegisterPlugin(IPlugin plugin)
	{
		try
		{
			plugin.Initialize(serviceProvider);

			var handle = PluginLoaderService.TryGetHandle(plugin);
			var tracked = new TrackedPlugin(
				plugin,
				handle?.Loader,
				handle?.DllPath,
				handle?.IsUnloadable ?? PluginLoaderService.IsUnloadablePlugin(plugin));

			var commandCount = RegisterCommands(plugin, tracked);
			var functionCount = RegisterFunctions(plugin, tracked);

			lock (_gate)
			{
				// Last registration wins on a duplicate id (e.g. a reload replacing the tracked entry).
				_tracked[plugin.Id] = tracked;
			}

			return (commandCount, functionCount);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Plugin '{Id}' threw during initialization/registration; skipping it.", plugin.Id);
			return (0, 0);
		}
	}

	/// <inheritdoc />
	public async Task<OneOf<Success, Error<string>>> UnloadAsync(string pluginId)
	{
		var result = Unload(pluginId, forReload: false);
		if (result.IsT0)
		{
			// A plugin DLL really left the running set: tell connected browsers to force a hard refresh, the
			// only way to reclaim any compiled component assembly the WASM client may have loaded.
			await NotifyPluginsChangedAsync();
		}

		return result;
	}

	/// <inheritdoc />
	public async Task<OneOf<Success, Error<string>>> ReloadAsync(string pluginId)
	{
		string dllPath;
		lock (_gate)
		{
			if (!_tracked.TryGetValue(pluginId, out var existing))
			{
				return new Error<string>($"Plugin '{pluginId}' is not loaded; nothing to reload.");
			}

			if (!existing.IsUnloadable)
			{
				return LoadOnceRefusal(pluginId);
			}

			if (existing.DllPath is null)
			{
				return new Error<string>(
					$"Plugin '{pluginId}' was not loaded from disk (no DLL path); it cannot be reloaded.");
			}

			dllPath = existing.DllPath;
		}

		// Unload first (removes its library entries and disposes its collectible ALC), then reload from disk.
		var unload = Unload(pluginId, forReload: true);
		if (unload.IsT1)
		{
			return unload;
		}

		var reloaded = PluginLoaderService.LoadOne(dllPath, logger);
		if (reloaded is null)
		{
			// The plugin really left the running set (unloaded, failed to come back): signal a refresh so any
			// browser-loaded compiled component is reclaimed.
			await NotifyPluginsChangedAsync();
			return new Error<string>(
				$"Plugin '{pluginId}' failed to reload from '{dllPath}'; it is now unloaded.");
		}

		var (commandCount, functionCount) = RegisterPlugin(reloaded.Plugin);
		logger.LogInformation(
			"Reloaded plugin '{Id}' v{Version}: {Commands} command(s), {Functions} function(s).",
			reloaded.Plugin.Id, reloaded.Plugin.Version, commandCount, functionCount);

		// A reload swapped the running DLL: force a browser refresh so the client re-fetches the catalog and
		// reloads any compiled component freshly.
		await NotifyPluginsChangedAsync();
		return new Success();
	}

	/// <summary>
	/// Fire the generic "plugins changed" signal (if a notifier is wired), isolated so a transport failure
	/// can never abort the unload/reload that triggered it. No-op when no notifier is registered (unit tests).
	/// </summary>
	private async Task NotifyPluginsChangedAsync()
	{
		if (changeNotifier is null)
		{
			return;
		}

		try
		{
			await changeNotifier.NotifyPluginsChangedAsync();
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Plugin-change notification failed; the unload/reload itself succeeded.");
		}
	}

	/// <summary>
	/// Remove a tracked plugin's command/function entries from the live libraries and dispose its collectible
	/// loader. Refuses unknown or load-once plugins. <paramref name="forReload"/> only affects log wording.
	/// </summary>
	private OneOf<Success, Error<string>> Unload(string pluginId, bool forReload)
	{
		TrackedPlugin tracked;
		lock (_gate)
		{
			if (!_tracked.TryGetValue(pluginId, out var found))
			{
				return new Error<string>($"Plugin '{pluginId}' is not loaded; nothing to unload.");
			}

			if (!found.IsUnloadable)
			{
				return LoadOnceRefusal(pluginId);
			}

			tracked = found;
			_tracked.Remove(pluginId);
		}

		// Remove only the entries this plugin actually added (collision-skipped names were never recorded),
		// and only if they still point at this plugin's definitions — never clobber a built-in or another
		// plugin that has since taken the name. The per-parse command trie rebuilds from the live library,
		// so removing these entries is all that is needed; no trie surgery.
		foreach (var name in tracked.CommandNames)
		{
			commandLibrary.Remove(name);
		}

		foreach (var name in tracked.FunctionNames)
		{
			functionLibrary.Remove(name);
		}

		// Drop strong references and tear down the collectible ALC so it becomes GC-eligible.
		var loader = tracked.Loader;
		tracked.Loader = null;
		tracked.Plugin = null!;
		loader?.Dispose();

		logger.LogInformation(
			"{Action} plugin '{Id}': removed {Commands} command(s), {Functions} function(s); collectible context disposed.",
			forReload ? "Unloaded (for reload)" : "Unloaded",
			pluginId, tracked.CommandNames.Count, tracked.FunctionNames.Count);

		return new Success();
	}

	private static Error<string> LoadOnceRefusal(string pluginId) =>
		new($"Plugin '{pluginId}' is load-once: it contributes DI services, database migrations, flags, " +
		    "or a NATS bridge subscription whose effects are captured by the container, the database, or the " +
		    "bridge and cannot be undone at runtime. Restart the server to unload or reload it.");

	private int RegisterCommands(IPlugin plugin, TrackedPlugin tracked)
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
				tracked.CommandNames.Add(name);
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

	private int RegisterFunctions(IPlugin plugin, TrackedPlugin tracked)
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
				tracked.FunctionNames.Add(name);
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
