using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using McMaster.NETCore.Plugins;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Plugins;

namespace SharpMUSH.Implementation.Services;

/// <summary>
/// Shared, single-pass plugin loader. Scans <c>plugins/</c> under <see cref="AppContext.BaseDirectory"/>,
/// reads each <c>plugin.json</c> manifest, topologically sorts by declared dependencies (tie-break by
/// priority then id), then loads each plugin <b>once</b> through a McMaster <see cref="PluginLoader"/> with
/// the host-declared <see cref="SharedContractTypes"/> and instantiates its <c>[SharpPlugin] IPlugin</c>.
///
/// This is the one place a DLL is loaded. The pre-build <see cref="PluginCatalog"/> drives this at
/// <c>Startup.ConfigureServices</c> time; the post-build <see cref="PluginManager"/> then reads the
/// already-loaded plugins from the catalog rather than loading again. Every plugin is isolated in
/// try/catch so a single bad DLL never aborts boot.
/// </summary>
public static class PluginLoaderService
{
	/// <summary>
	/// Host-declared contract types that must unify across the plugin isolation boundary. Listing a type
	/// here makes the host's copy authoritative for both sides regardless of plugin csproj hygiene, so
	/// reflected <see cref="CommandDefinition"/>/<see cref="FunctionDefinition"/> values (and the Phase 2a
	/// contribution interfaces) cast cleanly.
	/// </summary>
	public static readonly Type[] SharedContractTypes =
	[
		typeof(IPlugin),
		typeof(SharpPluginAttribute),
		typeof(ICommandSource),
		typeof(IFunctionSource),
		typeof(CommandDefinition),
		typeof(FunctionDefinition),
		typeof(SharpCommandAttribute),
		typeof(SharpFunctionAttribute),
		typeof(IMUSHCodeParser),
		typeof(CallState),
		typeof(PluginManifest),
		typeof(PluginBase),
		typeof(Option<CallState>),
		// Phase 2a contribution surfaces — must unify so the catalog's pattern-matches against the
		// plugin's loaded instance see the host's interface types.
		typeof(IServiceRegistrar),
		typeof(IFlagSource),
		typeof(IMigrationSource),
		typeof(IBridgeSubscriptionSource),
		typeof(PluginFlag)
	];

	private static readonly JsonSerializerOptions ManifestJsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	/// <summary>
	/// Side-table that ties each loaded <see cref="IPlugin"/> instance to the McMaster
	/// <see cref="PluginLoader"/> handle it came from (plus the DLL path and the unloadable verdict). Keyed
	/// by the plugin instance, it lets the post-build <see cref="PluginManager"/> recover the loader for a
	/// plugin the <see cref="PluginCatalog"/> handed it as a bare <see cref="IPlugin"/> — without the
	/// catalog (owned elsewhere) having to surface loaders. A <see cref="ConditionalWeakTable{TKey,TValue}"/>
	/// keeps the handle alive exactly as long as the plugin instance is reachable and needs no cleanup or
	/// cross-boot static state. The manager pins both the plugin and its handle while registered, so the
	/// collectible ALC only becomes collectible once the manager drops it on unload.
	/// </summary>
	private static readonly ConditionalWeakTable<IPlugin, PluginHandle> Handles = new();

	/// <summary>A plugin DLL found on disk together with its (manifest-or-fallback) ordering metadata.</summary>
	public sealed record PluginCandidate(
		string DllPath,
		string Id,
		IReadOnlyList<string> Dependencies,
		int Priority);

	/// <summary>An instantiated plugin together with the DLL it was loaded from.</summary>
	public sealed record LoadedPlugin(IPlugin Plugin, string DllPath)
	{
		/// <summary>
		/// The live McMaster loader handle for this plugin's collectible-or-not ALC. Held so the loader is
		/// not disposed at the end of <see cref="LoadAll"/>; the manager owns its lifetime thereafter.
		/// Non-positional so the catalog's <c>var (plugin, dllPath)</c> deconstruct keeps working unchanged.
		/// </summary>
		public required PluginLoader Loader { get; init; }

		/// <summary>
		/// True when this plugin contributes <b>only</b> command/function (and Phase-2b hook) sources and
		/// none of the load-once contribution seams, so its collectible ALC can be unloaded at runtime.
		/// </summary>
		public required bool IsUnloadable { get; init; }
	}

	/// <summary>The loader handle, DLL path and unloadable verdict recorded against a loaded plugin instance.</summary>
	public sealed record PluginHandle(PluginLoader Loader, string DllPath, bool IsUnloadable);

	/// <summary>
	/// Recover the <see cref="PluginHandle"/> recorded for an already-loaded plugin instance, or
	/// <c>null</c> if it was not loaded through <see cref="LoadAll"/>/<see cref="LoadOne"/> (e.g. a fake test
	/// plugin). Lets the <see cref="PluginManager"/> find a plugin's loader for unload/reload.
	/// </summary>
	public static PluginHandle? TryGetHandle(IPlugin plugin) =>
		Handles.TryGetValue(plugin, out var handle) ? handle : null;

	/// <summary>
	/// Discover, order, and load every plugin under <c>plugins/</c> exactly once. Returns the
	/// instantiated <see cref="IPlugin"/> entries in load order (dependencies first). The returned
	/// instances are not yet <c>Initialize</c>d and their contributions are not yet applied — that is the
	/// caller's job (the catalog applies DI, the manager registers commands/functions, etc.).
	/// </summary>
	public static IReadOnlyList<LoadedPlugin> LoadAll(ILogger logger)
	{
		var pluginsRoot = Path.Combine(AppContext.BaseDirectory, "plugins");
		if (!Directory.Exists(pluginsRoot))
		{
			logger.LogDebug("No plugins directory at {PluginsRoot}; nothing to load.", pluginsRoot);
			return [];
		}

		var discovered = Discover(pluginsRoot, logger).ToList();
		if (discovered.Count == 0)
		{
			logger.LogDebug("No plugin DLLs discovered under {PluginsRoot}.", pluginsRoot);
			return [];
		}

		var ordered = TopologicalSort(discovered, logger);

		var loaded = new List<LoadedPlugin>();
		foreach (var candidate in ordered)
		{
			var result = LoadOne(candidate.DllPath, logger);
			if (result is not null)
			{
				loaded.Add(result);
			}
		}

		return loaded;
	}

	/// <summary>
	/// Find every <c>*.dll</c> at the top of the plugins directory and one level down
	/// (<c>plugins/&lt;id&gt;/*.dll</c>). For each, read a sibling <c>plugin.json</c> for ordering metadata,
	/// falling back to a default candidate keyed by the file name when no manifest is present.
	/// </summary>
	public static IEnumerable<PluginCandidate> Discover(string pluginsRoot, ILogger logger)
	{
		var dllPaths = Directory.EnumerateFiles(pluginsRoot, "*.dll", SearchOption.TopDirectoryOnly)
			.Concat(Directory.EnumerateDirectories(pluginsRoot)
				.SelectMany(dir => Directory.EnumerateFiles(dir, "*.dll", SearchOption.TopDirectoryOnly)));

		foreach (var dll in dllPaths)
		{
			var manifest = TryReadManifest(dll, logger);
			if (manifest is not null)
			{
				yield return new PluginCandidate(dll, manifest.Id, manifest.Dependencies, manifest.Priority);
			}
			else
			{
				// No manifest: still loadable, keyed by file name; ordering metadata defaults.
				yield return new PluginCandidate(dll, Path.GetFileNameWithoutExtension(dll), [], 0);
			}
		}
	}

	private static PluginManifest? TryReadManifest(string dllPath, ILogger logger)
	{
		var manifestPath = Path.Combine(Path.GetDirectoryName(dllPath)!, "plugin.json");
		if (!File.Exists(manifestPath))
		{
			return null;
		}

		try
		{
			var json = File.ReadAllText(manifestPath);
			var manifest = JsonSerializer.Deserialize<PluginManifest>(json, ManifestJsonOptions);
			if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id))
			{
				logger.LogWarning("Plugin manifest {ManifestPath} is empty or missing an Id; ignoring it.", manifestPath);
				return null;
			}

			return manifest with { Dependencies = manifest.Dependencies ?? [] };
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to read plugin manifest {ManifestPath}; falling back to defaults.", manifestPath);
			return null;
		}
	}

	/// <summary>
	/// Order plugins so that every plugin loads after all of its declared <see cref="PluginManifest.Dependencies"/>.
	/// Ties (no dependency relationship) break by <see cref="PluginManifest.Priority"/> then id. Plugins
	/// participating in a dependency cycle are detected, logged, and skipped (the rest still load).
	/// </summary>
	public static IReadOnlyList<PluginCandidate> TopologicalSort(IReadOnlyList<PluginCandidate> candidates, ILogger logger)
	{
		var byId = new Dictionary<string, PluginCandidate>(StringComparer.OrdinalIgnoreCase);
		foreach (var candidate in candidates)
		{
			// First definition of an id wins; a duplicate id is logged and ignored.
			if (!byId.TryAdd(candidate.Id, candidate))
			{
				logger.LogWarning("Duplicate plugin id '{Id}' at {DllPath}; ignoring the duplicate.", candidate.Id, candidate.DllPath);
			}
		}

		var deterministic = byId.Values
			.OrderBy(c => c.Priority)
			.ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
			.ToList();

		var result = new List<PluginCandidate>();
		// 0 = unvisited, 1 = visiting (on the current DFS stack), 2 = done.
		var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

		bool Visit(PluginCandidate candidate)
		{
			state[candidate.Id] = 1;
			foreach (var dependencyId in candidate.Dependencies.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
			{
				if (!byId.TryGetValue(dependencyId, out var dependency))
				{
					logger.LogWarning(
						"Plugin '{Id}' declares dependency '{Dependency}' which was not found; loading '{Id}' anyway.",
						candidate.Id, dependencyId, candidate.Id);
					continue;
				}

				var depState = state.GetValueOrDefault(dependency.Id);
				if (depState == 1)
				{
					logger.LogError(
						"Dependency cycle detected involving plugins '{Id}' and '{Dependency}'; skipping the cyclic plugins.",
						candidate.Id, dependency.Id);
					return false;
				}

				if (depState == 0 && !Visit(dependency))
				{
					return false;
				}
			}

			state[candidate.Id] = 2;
			result.Add(candidate);
			return true;
		}

		foreach (var candidate in deterministic)
		{
			if (state.GetValueOrDefault(candidate.Id) != 0)
			{
				continue;
			}

			if (!Visit(candidate))
			{
				// Mark every still-visiting node as skipped so it is never appended.
				foreach (var key in state.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToList())
				{
					state[key] = 2;
				}
			}
		}

		return result;
	}

	/// <summary>
	/// Load a single plugin DLL through McMaster with the shared contract types and instantiate its
	/// <c>[SharpPlugin] IPlugin</c>. The collectibility of the underlying <see cref="System.Runtime.Loader.AssemblyLoadContext"/>
	/// is decided <b>per plugin</b> by <see cref="IsUnloadablePlugin"/>: a plugin that contributes only
	/// command/function (and Phase-2b hook) sources gets a collectible ALC and can be unloaded at runtime;
	/// one that also contributes DI/migration/flag/bridge state is loaded non-collectibly (load-once). The
	/// loader handle is kept alive (never disposed here) and recorded against the plugin instance via
	/// <see cref="Handles"/> so the manager can later unload it. Returns <c>null</c> (and logs) on any
	/// failure so the caller keeps loading.
	/// </summary>
	public static LoadedPlugin? LoadOne(string dllPath, ILogger logger)
	{
		PluginLoader? loader = null;
		try
		{
			// Load collectibly: a collectible ALC costs nothing extra while it stays loaded, but it is the
			// only kind that can later be unloaded. We instantiate the entry type, read which contribution
			// interfaces it implements, and derive the unloadable verdict from that. Load-once plugins keep
			// the same collectible loader (we simply never unload them); command/function-only plugins are
			// the ones the manager may actually unload.
			loader = PluginLoader.CreateFromAssemblyFile(
				dllPath,
				isUnloadable: true,
				sharedTypes: SharedContractTypes);

			var plugin = Instantiate(loader, dllPath, logger);
			if (plugin is null)
			{
				loader.Dispose();
				return null;
			}

			var unloadable = IsUnloadablePlugin(plugin);
			Handles.AddOrUpdate(plugin, new PluginHandle(loader, dllPath, unloadable));
			return new LoadedPlugin(plugin, dllPath) { Loader = loader, IsUnloadable = unloadable };
		}
		catch (Exception ex)
		{
			loader?.Dispose();
			logger.LogError(ex, "Failed to load plugin from {DllPath}; skipping it.", dllPath);
			return null;
		}
	}

	/// <summary>
	/// Instantiate the single <c>[SharpPlugin] IPlugin</c> entry type from a loader's default assembly, or
	/// <c>null</c> (with a log) when the DLL has no valid entry type.
	/// </summary>
	private static IPlugin? Instantiate(PluginLoader loader, string dllPath, ILogger logger)
	{
		var assembly = loader.LoadDefaultAssembly();

		var entryType = assembly.GetTypes()
			.FirstOrDefault(t => t is { IsClass: true, IsAbstract: false }
				&& t.GetCustomAttribute<SharpPluginAttribute>() is not null
				&& typeof(IPlugin).IsAssignableFrom(t));

		if (entryType is null)
		{
			logger.LogWarning("Plugin DLL {DllPath} has no [SharpPlugin] IPlugin entry type; skipping.", dllPath);
			return null;
		}

		if (Activator.CreateInstance(entryType) is not IPlugin plugin)
		{
			logger.LogWarning("Could not instantiate plugin entry type {EntryType} in {DllPath}; skipping.", entryType.FullName, dllPath);
			return null;
		}

		return plugin;
	}

	/// <summary>
	/// The per-plugin unloadable verdict. A plugin is unloadable iff it contributes <b>only</b> runtime-
	/// removable surfaces — <see cref="ICommandSource"/>/<see cref="IFunctionSource"/> (Phase-2b hook
	/// sources, when present, are equally removable) — and <b>none</b> of the load-once seams whose effects
	/// are captured by the DI container, the database, the flag set, or the NATS bridge and therefore cannot
	/// be torn down without a server restart: <see cref="IServiceRegistrar"/>, <see cref="IMigrationSource"/>,
	/// <see cref="IFlagSource"/>, <see cref="IBridgeSubscriptionSource"/>.
	/// </summary>
	public static bool IsUnloadablePlugin(IPlugin plugin)
	{
		var contributesCommandsOrFunctions = plugin is ICommandSource or IFunctionSource;
		var contributesLoadOnceState =
			plugin is IServiceRegistrar
			|| plugin is IMigrationSource
			|| plugin is IFlagSource
			|| plugin is IBridgeSubscriptionSource;

		return contributesCommandsOrFunctions && !contributesLoadOnceState;
	}
}
