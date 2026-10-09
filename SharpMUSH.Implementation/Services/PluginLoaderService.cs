using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using McMaster.NETCore.Plugins;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Plugins;

namespace SharpMUSH.Implementation.Services;

/// <summary>
/// Shared, single-pass plugin loader. Scans the shipped and installed plugin directories (<see cref="PluginDirectories"/>),
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
		// Option<T>'s None case lives in SharpMUSH.Contracts rather than Library; sharing it keeps a
		// plugin's Option<CallState> values the host's.
		typeof(None),
		// Phase 2a contribution surfaces — must unify so the catalog's pattern-matches against the
		// plugin's loaded instance see the host's interface types.
		typeof(IServiceRegistrar),
		typeof(IFlagSource),
		typeof(IMigrationSource),
		typeof(IBridgeSubscriptionSource),
		// Phase 9 web-contribution seam — must unify so the catalog's pattern-match sees the host's type.
		typeof(IEndpointContributor),
		// Portal UI seam — must unify so the catalog's pattern-match sees the host's type, and so the
		// RegisteredApplication values a plugin returns cast cleanly across the isolation boundary.
		typeof(IApplicationSource),
		// RegisteredApplication (and the rest of the browser-safe portal surface) lives in
		// SharpMUSH.Contracts, a separate assembly from IApplicationSource. Listing it shares that
		// assembly too, so contract values a plugin returns keep their type identity in the host.
		typeof(SharpMUSH.Library.Models.Portal.Applications.RegisteredApplication),
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

	/// <summary>
	/// A plugin found on disk together with its (manifest-or-fallback) ordering metadata. <see cref="Problem"/> is set
	/// when the plugin cannot be loaded as found (its entry assembly is ambiguous or missing, or it needs a newer
	/// contract than this server provides); such a candidate is reported, never loaded.
	/// </summary>
	public sealed record PluginCandidate(
		string DllPath,
		string Id,
		IReadOnlyList<string> Dependencies,
		int Priority,
		IReadOnlyList<string>? SharedAssemblies = null,
		PluginOrigin Origin = PluginOrigin.Installed,
		string? Name = null,
		string? Version = null,
		string? Description = null,
		PluginBootStatus? Problem = null,
		string? Reason = null);

	/// <summary>What one boot's load pass did: the plugins it loaded and what became of every plugin it found.</summary>
	public sealed record LoadReport(IReadOnlyList<LoadedPlugin> Loaded, IReadOnlyList<PluginBootEntry> Entries);

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
	/// Discover, order, and load every plugin in <paramref name="directories"/> exactly once: the shipped ones first,
	/// then the installed ones. Returns the instantiated <see cref="IPlugin"/> entries in load order (dependencies
	/// first) and an entry for every plugin found, loaded or not. The returned instances are not yet
	/// <c>Initialize</c>d and their contributions are not yet applied — that is the caller's job (the catalog
	/// applies DI, the manager registers commands/functions, etc.).
	/// </summary>
	/// <param name="directories">Where to look.</param>
	/// <param name="state">Which plugins an administrator turned off; found, reported, not loaded.</param>
	/// <param name="logger">Where problems go.</param>
	public static LoadReport LoadAll(PluginDirectories directories, PluginState state, ILogger logger)
	{
		var found = Discover(directories.BuiltIn, logger, PluginOrigin.BuiltIn)
			.Concat(Discover(directories.Installed, logger, PluginOrigin.Installed))
			.ToList();
		if (found.Count == 0)
		{
			logger.LogDebug("No plugins under {BuiltIn} or {Installed}; nothing to load.", directories.BuiltIn, directories.Installed);
			return new LoadReport([], []);
		}

		var entries = new List<PluginBootEntry>();
		var loadable = new List<PluginCandidate>();
		var claimed = new Dictionary<string, PluginCandidate>(StringComparer.OrdinalIgnoreCase);
		foreach (var candidate in found)
		{
			if (claimed.TryGetValue(candidate.Id, out var first))
			{
				// A shipped plugin is found first, so an installed one can never stand in for it.
				logger.LogWarning("Plugin id '{Id}' at {DllPath} is already used by {First}; ignoring it.",
					candidate.Id, candidate.DllPath, first.DllPath);
				entries.Add(Entry(candidate, PluginBootStatus.Duplicate,
					first.Origin == PluginOrigin.BuiltIn
						? "A plugin that ships with the server already uses this id."
						: $"Another plugin already uses this id ({Path.GetDirectoryName(first.DllPath)})."));
				continue;
			}

			claimed[candidate.Id] = candidate;
			if (candidate.Problem is { } problem)
			{
				logger.LogError("Plugin '{Id}' at {DllPath} is not loaded: {Reason}", candidate.Id, candidate.DllPath, candidate.Reason);
				entries.Add(Entry(candidate, problem, candidate.Reason));
			}
			else if (state.IsDisabled(candidate.Id))
			{
				logger.LogInformation("Plugin '{Id}' is turned off; not loading it.", candidate.Id);
				entries.Add(Entry(candidate, PluginBootStatus.Disabled, null));
			}
			else
			{
				loadable.Add(candidate);
			}
		}

		var loaded = new List<LoadedPlugin>();
		var ordered = TopologicalSort(loadable, logger);
		foreach (var candidate in loadable.Where(c => !ordered.Contains(c)))
		{
			entries.Add(Entry(candidate, PluginBootStatus.Failed, "Its dependencies form a cycle."));
		}

		foreach (var candidate in ordered)
		{
			if (LoadOne(candidate.DllPath, logger, candidate.SharedAssemblies) is not { } result)
			{
				entries.Add(Entry(candidate, PluginBootStatus.Failed, "It threw while loading; the server log has the error."));
				continue;
			}

			if (!string.Equals(result.Plugin.Id, candidate.Id, StringComparison.OrdinalIgnoreCase))
			{
				// Unload, enable and disable all go by the plugin.json id; a plugin that answers to another is unmanageable.
				logger.LogError("Plugin at {DllPath} calls itself '{PluginId}' but plugin.json says '{Id}'; not loading it.",
					candidate.DllPath, result.Plugin.Id, candidate.Id);
				result.Loader.Dispose();
				entries.Add(Entry(candidate, PluginBootStatus.Failed,
					$"The plugin calls itself '{result.Plugin.Id}', but plugin.json says '{candidate.Id}'."));
				continue;
			}

			loaded.Add(result);
			entries.Add(Entry(candidate with { Version = candidate.Version ?? result.Plugin.Version }, PluginBootStatus.Loaded, null));
		}

		return new LoadReport(loaded, entries);
	}

	private static PluginBootEntry Entry(PluginCandidate candidate, PluginBootStatus status, string? reason) =>
		new(candidate.Id, candidate.Name, candidate.Version, candidate.Description, candidate.Origin,
			Path.GetDirectoryName(candidate.DllPath)!, status, reason);

	/// <summary>
	/// Find every plugin in <paramref name="pluginsRoot"/>: a DLL at the top level (a hand-dropped plugin, keyed by
	/// its file name) or a folder one level down (<c>plugins/&lt;id&gt;/</c>). A folder's <c>plugin.json</c> gives its
	/// id and ordering metadata and, when the folder carries more than one DLL, names the entry assembly. Folders
	/// whose name starts with a dot (upload staging) are not plugins.
	/// </summary>
	public static IEnumerable<PluginCandidate> Discover(string pluginsRoot, ILogger logger,
		PluginOrigin origin = PluginOrigin.Installed)
	{
		if (!Directory.Exists(pluginsRoot))
		{
			yield break;
		}

		foreach (var dll in Directory.EnumerateFiles(pluginsRoot, "*.dll", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
		{
			yield return new PluginCandidate(dll, Path.GetFileNameWithoutExtension(dll), [], 0, Origin: origin);
		}

		var folders = Directory.EnumerateDirectories(pluginsRoot)
			.Where(folder => !Path.GetFileName(folder).StartsWith('.'))
			.Order(StringComparer.Ordinal);
		foreach (var folder in folders)
		{
			if (FromFolder(folder, logger, origin) is { } candidate)
			{
				yield return candidate;
			}
		}
	}

	/// <summary>The plugin in <paramref name="folder"/>, or null when the folder holds no DLL at all.</summary>
	private static PluginCandidate? FromFolder(string folder, ILogger logger, PluginOrigin origin)
	{
		var dlls = Directory.EnumerateFiles(folder, "*.dll", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal).ToList();
		var manifest = TryReadManifest(Path.Combine(folder, PluginManifest.FileName), logger);
		if (dlls.Count == 0 && manifest is null)
		{
			return null;
		}

		var id = manifest?.Id ?? Path.GetFileName(folder);
		var candidate = new PluginCandidate(dlls.FirstOrDefault() ?? Path.Combine(folder, id + ".dll"), id,
			manifest?.Dependencies ?? [], manifest?.Priority ?? 0, manifest?.SharedAssemblies, origin,
			manifest?.Name, manifest?.Version, manifest?.Description);

		string? entry;
		if (manifest?.Entry is { Length: > 0 } named)
		{
			entry = dlls.FirstOrDefault(d => string.Equals(Path.GetFileName(d), named, StringComparison.OrdinalIgnoreCase));
			if (entry is null)
			{
				return candidate with { Problem = PluginBootStatus.Failed, Reason = $"plugin.json names {named} as its entry, and the folder has no such file." };
			}
		}
		else if (dlls.Count == 1)
		{
			entry = dlls[0];
		}
		else
		{
			return candidate with
			{
				Problem = PluginBootStatus.Failed,
				Reason = dlls.Count == 0
					? "The folder has no DLL."
					: $"The folder has {dlls.Count} DLLs and plugin.json does not say which one is the plugin (\"entry\")."
			};
		}

		candidate = candidate with { DllPath = entry };
		if (manifest?.MinServerVersion is { Length: > 0 } minimum)
		{
			if (!VersionConstraint.TryParse(minimum, out var constraint))
			{
				return candidate with { Problem = PluginBootStatus.Incompatible, Reason = $"plugin.json's minServerVersion '{minimum}' is not a version constraint." };
			}

			if (!PluginContractVersion.Satisfies(constraint))
			{
				return candidate with
				{
					Problem = PluginBootStatus.Incompatible,
					Reason = $"It needs plugin contract {minimum}; this server provides {PluginContractVersion.Current}."
				};
			}
		}

		return candidate;
	}

	/// <summary>The <c>plugin.json</c> at <paramref name="manifestPath"/>, or null when there is none or it does not parse.</summary>
	public static PluginManifest? ReadManifest(string manifestPath, ILogger logger) => TryReadManifest(manifestPath, logger);

	private static PluginManifest? TryReadManifest(string manifestPath, ILogger logger)
	{
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
	public static LoadedPlugin? LoadOne(string dllPath, ILogger logger,
		IReadOnlyList<string>? sharedAssemblyNames = null)
	{
		PluginLoader? loader = null;
		try
		{
			// Assembly NAMES the host must share into this plugin's ALC, beyond the sharedTypes net: whatever
			// the plugin's manifest declares. Sharing by name makes the host's already-loaded copy
			// authoritative, so a host service whose signatures reference those types casts cleanly inside the
			// plugin. We never set PreferSharedTypes=true (too broad).
			var sharedNames = (sharedAssemblyNames ?? [])
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			// Load collectibly: a collectible ALC costs nothing extra while it stays loaded, but it is the
			// only kind that can later be unloaded. We instantiate the entry type, read which contribution
			// interfaces it implements, and derive the unloadable verdict from that. Load-once plugins keep
			// the same collectible loader (we simply never unload them); command/function-only plugins are
			// the ones the manager may actually unload. The sharedTypes net is preserved verbatim (it unifies
			// the contract assemblies); the configure leg additionally shares the DB-client assemblies BY NAME.
			loader = PluginLoader.CreateFromAssemblyFile(
				dllPath,
				isUnloadable: true,
				sharedTypes: SharedContractTypes,
				configure: config =>
				{
					foreach (var name in sharedNames)
					{
						config.SharedAssemblies.Add(new AssemblyName(name));
					}
				});

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
	/// <see cref="IFlagSource"/>, <see cref="IBridgeSubscriptionSource"/>, <see cref="IEndpointContributor"/>,
	/// <see cref="IApplicationSource"/>.
	/// </summary>
	public static bool IsUnloadablePlugin(IPlugin plugin)
	{
		var contributesCommandsOrFunctions = plugin is ICommandSource or IFunctionSource;
		var contributesLoadOnceState =
			plugin is IServiceRegistrar
			|| plugin is IMigrationSource
			|| plugin is IFlagSource
			|| plugin is IBridgeSubscriptionSource
			// A mapped endpoint (hub/route) is part of the built pipeline and cannot be unmapped at runtime.
			|| plugin is IEndpointContributor
			// A contributed UI app overlays the registry (and its controller is mapped into the pipeline);
			// removing it cleanly is the same load-once problem as a mapped endpoint.
			|| plugin is IApplicationSource;

		return contributesCommandsOrFunctions && !contributesLoadOnceState;
	}
}
