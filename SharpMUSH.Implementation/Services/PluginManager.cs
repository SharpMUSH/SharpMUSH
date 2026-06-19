using System.Reflection;
using System.Text.Json;
using McMaster.NETCore.Plugins;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Services;

/// <summary>
/// Phase 1 plugin loader. Scans <c>plugins/</c> under <see cref="AppContext.BaseDirectory"/>, reads each
/// <c>plugin.json</c> manifest, topologically sorts by declared dependencies (tie-break by priority then
/// id), then loads each plugin in order through <see cref="PluginLoader"/> and registers its
/// <see cref="ICommandSource"/>/<see cref="IFunctionSource"/> contributions into the live
/// <see cref="CommandLibraryService"/>/<see cref="FunctionLibraryService"/> with <c>IsSystem=true</c>
/// (compiled C#, same tier as built-ins). Each plugin is wrapped in try/catch so a single bad DLL never
/// aborts boot. Name collisions with existing entries (engine built-ins loaded first) are logged and skipped.
/// </summary>
public sealed class PluginManager(
	LibraryService<string, CommandDefinition> commandLibrary,
	LibraryService<string, FunctionDefinition> functionLibrary,
	IServiceProvider serviceProvider,
	ILogger<PluginManager> logger) : IPluginManager
{
	/// <summary>
	/// Host-declared contract types that must unify across the plugin isolation boundary. Listing a type
	/// here makes the host's copy authoritative for both sides regardless of plugin csproj hygiene, so
	/// reflected <see cref="CommandDefinition"/>/<see cref="FunctionDefinition"/> values cast cleanly.
	/// </summary>
	internal static readonly Type[] SharedContractTypes =
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
		typeof(SharpMUSH.Library.DiscriminatedUnions.Option<CallState>)
	];

	private static readonly JsonSerializerOptions ManifestJsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	public Task LoadAllAsync(CancellationToken cancellationToken)
	{
		var pluginsRoot = Path.Combine(AppContext.BaseDirectory, "plugins");
		if (!Directory.Exists(pluginsRoot))
		{
			logger.LogDebug("No plugins directory at {PluginsRoot}; nothing to load.", pluginsRoot);
			return Task.CompletedTask;
		}

		var discovered = Discover(pluginsRoot).ToList();
		if (discovered.Count == 0)
		{
			logger.LogDebug("No plugin DLLs discovered under {PluginsRoot}.", pluginsRoot);
			return Task.CompletedTask;
		}

		var ordered = TopologicalSort(discovered);

		foreach (var candidate in ordered)
		{
			cancellationToken.ThrowIfCancellationRequested();
			LoadOne(candidate);
		}

		return Task.CompletedTask;
	}

	/// <summary>A plugin DLL found on disk together with its (manifest-or-fallback) ordering metadata.</summary>
	public sealed record PluginCandidate(
		string DllPath,
		string Id,
		IReadOnlyList<string> Dependencies,
		int Priority);

	/// <summary>
	/// Find every <c>*.dll</c> at the top of the plugins directory and one level down
	/// (<c>plugins/&lt;id&gt;/*.dll</c>). For each, read a sibling <c>plugin.json</c> for ordering metadata,
	/// falling back to a default candidate keyed by the file name when no manifest is present.
	/// </summary>
	internal IEnumerable<PluginCandidate> Discover(string pluginsRoot)
	{
		var dllPaths = Directory.EnumerateFiles(pluginsRoot, "*.dll", SearchOption.TopDirectoryOnly)
			.Concat(Directory.EnumerateDirectories(pluginsRoot)
				.SelectMany(dir => Directory.EnumerateFiles(dir, "*.dll", SearchOption.TopDirectoryOnly)));

		foreach (var dll in dllPaths)
		{
			var manifest = TryReadManifest(dll);
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

	private PluginManifest? TryReadManifest(string dllPath)
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
	public IReadOnlyList<PluginCandidate> TopologicalSort(IReadOnlyList<PluginCandidate> candidates)
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

	private void LoadOne(PluginCandidate candidate)
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
				return;
			}

			if (Activator.CreateInstance(entryType) is not IPlugin plugin)
			{
				logger.LogWarning("Could not instantiate plugin entry type {EntryType} in {DllPath}; skipping.", entryType.FullName, candidate.DllPath);
				return;
			}

			var (commandCount, functionCount) = RegisterPlugin(plugin);

			logger.LogInformation(
				"Loaded plugin '{Id}' v{Version} from {DllPath}: {Commands} command(s), {Functions} function(s).",
				plugin.Id, plugin.Version, candidate.DllPath, commandCount, functionCount);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to load plugin from {DllPath}; skipping it.", candidate.DllPath);
		}
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
