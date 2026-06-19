using System.Reflection;
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

	/// <summary>A plugin DLL found on disk together with its (manifest-or-fallback) ordering metadata.</summary>
	public sealed record PluginCandidate(
		string DllPath,
		string Id,
		IReadOnlyList<string> Dependencies,
		int Priority);

	/// <summary>An instantiated plugin together with the DLL it was loaded from.</summary>
	public sealed record LoadedPlugin(IPlugin Plugin, string DllPath);

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
			var plugin = LoadOne(candidate, logger);
			if (plugin is not null)
			{
				loaded.Add(new LoadedPlugin(plugin, candidate.DllPath));
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
	/// <c>[SharpPlugin] IPlugin</c>. Returns <c>null</c> (and logs) on any failure so the caller keeps loading.
	/// </summary>
	private static IPlugin? LoadOne(PluginCandidate candidate, ILogger logger)
	{
		try
		{
			var loader = PluginLoader.CreateFromAssemblyFile(
				candidate.DllPath,
				isUnloadable: false,
				sharedTypes: SharedContractTypes);

			var assembly = loader.LoadDefaultAssembly();

			var entryType = assembly.GetTypes()
				.FirstOrDefault(t => t is { IsClass: true, IsAbstract: false }
					&& t.GetCustomAttribute<SharpPluginAttribute>() is not null
					&& typeof(IPlugin).IsAssignableFrom(t));

			if (entryType is null)
			{
				logger.LogWarning("Plugin DLL {DllPath} has no [SharpPlugin] IPlugin entry type; skipping.", candidate.DllPath);
				return null;
			}

			if (Activator.CreateInstance(entryType) is not IPlugin plugin)
			{
				logger.LogWarning("Could not instantiate plugin entry type {EntryType} in {DllPath}; skipping.", entryType.FullName, candidate.DllPath);
				return null;
			}

			return plugin;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to load plugin from {DllPath}; skipping it.", candidate.DllPath);
			return null;
		}
	}
}
