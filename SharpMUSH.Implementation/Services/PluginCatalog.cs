using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Plugins;

namespace SharpMUSH.Implementation.Services;

/// <summary>
/// Single, pre-build catalog of loaded plugins and their contributions. This is the heart of the
/// Phase 2a two-phase boot.
///
/// <para><b>The two-phase problem.</b> DI registration happens during container construction, and
/// <c>Migrate()</c> with its flag seeding is awaited by <c>Program</c> right after the host is built,
/// before any hosted service runs — so before the post-build Phase 1 <see cref="PluginManager"/>.
/// Plugins must therefore be discovered in a <i>pre-build</i> pass.</para>
///
/// <para><b>Load once.</b> <see cref="Build"/> is called once from <c>Startup.ConfigureServices</c>,
/// before the engine's services and the <c>ISharpDatabase</c> registration. It runs
/// <see cref="PluginLoaderService.LoadAll"/> (the single DLL-load pass), applies every
/// <see cref="IServiceRegistrar"/> straight into the host <see cref="IServiceCollection"/>, and stashes
/// the loaded plugins plus their migration/flag/bridge contributions here. The catalog is registered as a
/// singleton; the DB factory, <c>NatsBridgeService</c>, and the post-build <see cref="PluginManager"/> all
/// read it — the manager registers the catalog's already-loaded command/function sources rather than
/// re-loading any DLL.</para>
/// </summary>
public sealed class PluginCatalog
{
	private readonly List<IPlugin> _plugins = [];
	private readonly List<IFlagSource> _flagSources = [];
	private readonly List<IMigrationSource> _migrationSources = [];
	private readonly List<IBridgeSubscriptionSource> _bridgeSources = [];
	private readonly List<IEndpointContributor> _endpointContributors = [];
	private readonly List<IApplicationSource> _applicationSources = [];
	private readonly List<ICommandInterceptor> _commandInterceptors = [];
	private readonly List<IConnectionHook> _connectionHooks = [];
	private readonly List<IObjectLifecycleHook> _objectLifecycleHooks = [];

	private readonly List<PluginBootEntry> _entries = [];

	private PluginCatalog(PluginDirectories? directories = null)
	{
		Directories = directories;
	}

	/// <summary>Where this start looked for plugins; null for a catalog built without a disk scan.</summary>
	public PluginDirectories? Directories { get; }

	/// <summary>Every plugin this start found, loaded or not, and what became of it.</summary>
	public IReadOnlyList<PluginBootEntry> Entries => _entries;

	/// <summary>Every loaded plugin instance, in load order (dependencies first).</summary>
	public IReadOnlyList<IPlugin> Plugins => _plugins;

	/// <summary>Flag contributions collected from plugins implementing <see cref="IFlagSource"/>.</summary>
	public IReadOnlyList<IFlagSource> FlagSources => _flagSources;

	/// <summary>Migration contributions collected from plugins implementing <see cref="IMigrationSource"/>.</summary>
	public IReadOnlyList<IMigrationSource> MigrationSources => _migrationSources;

	/// <summary>Bridge subscription contributions from plugins implementing <see cref="IBridgeSubscriptionSource"/>.</summary>
	public IReadOnlyList<IBridgeSubscriptionSource> BridgeSources => _bridgeSources;

	/// <summary>Phase 9: endpoint contributions from plugins implementing <see cref="IEndpointContributor"/>, in load order.</summary>
	public IReadOnlyList<IEndpointContributor> EndpointContributors => _endpointContributors;

	/// <summary>Portal UI contributions from plugins implementing <see cref="IApplicationSource"/>, in load order. Overlaid (read-only) onto the Area-21 application registry while the plugin is loaded.</summary>
	public IReadOnlyList<IApplicationSource> ApplicationSources => _applicationSources;

	/// <summary>Phase 2b: command interceptors from plugins implementing <see cref="ICommandInterceptor"/>, in load order.</summary>
	public IReadOnlyList<ICommandInterceptor> CommandInterceptors => _commandInterceptors;

	/// <summary>Phase 2b: connection hooks from plugins implementing <see cref="IConnectionHook"/>, in load order.</summary>
	public IReadOnlyList<IConnectionHook> ConnectionHooks => _connectionHooks;

	/// <summary>Phase 2b: object lifecycle hooks from plugins implementing <see cref="IObjectLifecycleHook"/>, in load order.</summary>
	public IReadOnlyList<IObjectLifecycleHook> ObjectLifecycleHooks => _objectLifecycleHooks;

	/// <summary>The flattened set of every plugin flag, in plugin load order.</summary>
	public IReadOnlyList<PluginFlag> AllFlags =>
		_flagSources.SelectMany(s =>
		{
			try { return s.Flags; }
			catch { return []; }
		}).ToList();

	/// <summary>
	/// Load every plugin once, apply each <see cref="IServiceRegistrar"/> into <paramref name="services"/>,
	/// and collect the migration/flag/bridge contributions. Call this once, pre-build, from
	/// <c>Startup.ConfigureServices</c>. Per-plugin failures are isolated and logged.
	/// </summary>
	public static PluginCatalog Build(IServiceCollection services, PluginDirectories directories, ILogger logger)
	{
		var catalog = new PluginCatalog(directories);

		var report = PluginLoaderService.LoadAll(directories, PluginState.Read(directories.StateFile), logger);
		catalog._entries.AddRange(report.Entries);
		foreach (var (plugin, dllPath) in report.Loaded)
		{
			try
			{
				catalog._plugins.Add(plugin);

				if (plugin is IServiceRegistrar registrar)
				{
					registrar.RegisterServices(services);
				}

				if (plugin is IFlagSource flagSource)
				{
					catalog._flagSources.Add(flagSource);
				}

				if (plugin is IMigrationSource migrationSource)
				{
					catalog._migrationSources.Add(migrationSource);
				}

				if (plugin is IBridgeSubscriptionSource bridgeSource)
				{
					catalog._bridgeSources.Add(bridgeSource);
				}

				// Phase 9: ASP.NET endpoint contributions (hubs/routes mapped post-build in ConfigureApp).
				if (plugin is IEndpointContributor endpointContributor)
				{
					catalog._endpointContributors.Add(endpointContributor);
				}

				// Portal UI contributions (Area-21 applications overlaid onto the registry while loaded).
				if (plugin is IApplicationSource applicationSource)
				{
					catalog._applicationSources.Add(applicationSource);
				}

				// Phase 2b: engine-extension hooks (the C# analog of softcode @hook).
				if (plugin is ICommandInterceptor commandInterceptor)
				{
					catalog._commandInterceptors.Add(commandInterceptor);
				}

				if (plugin is IConnectionHook connectionHook)
				{
					catalog._connectionHooks.Add(connectionHook);
				}

				if (plugin is IObjectLifecycleHook objectLifecycleHook)
				{
					catalog._objectLifecycleHooks.Add(objectLifecycleHook);
				}

				logger.LogInformation(
					"Catalogued plugin '{Id}' v{Version} from {DllPath} (services:{Svc} flags:{Flag} migrations:{Mig} bridge:{Bridge} apps:{App} cmdHook:{Cmd} connHook:{Conn} objHook:{Obj}).",
					plugin.Id, plugin.Version, dllPath,
					plugin is IServiceRegistrar, plugin is IFlagSource, plugin is IMigrationSource,
					plugin is IBridgeSubscriptionSource, plugin is IApplicationSource,
					plugin is ICommandInterceptor, plugin is IConnectionHook, plugin is IObjectLifecycleHook);
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Plugin '{Id}' threw while cataloguing its contributions; skipping it.", plugin.Id);
			}
		}

		return catalog;
	}

	/// <summary>An empty catalog, for hosts (or tests) that do not load plugins.</summary>
	public static PluginCatalog Empty() => new();

	/// <summary>
	/// Build a catalog directly from already-instantiated plugins, classifying each into the same
	/// contribution buckets <see cref="Build"/> uses but without any DLL load or DI application. For tests
	/// that need a populated catalog (e.g. an <see cref="IApplicationSource"/> overlay) without the
	/// pre-build disk scan.
	/// </summary>
	public static PluginCatalog ForPlugins(IEnumerable<IPlugin> plugins)
	{
		var catalog = new PluginCatalog();
		foreach (var plugin in plugins)
		{
			catalog._plugins.Add(plugin);
			catalog._entries.Add(new PluginBootEntry(plugin.Id, null, plugin.Version, null, PluginOrigin.BuiltIn,
				string.Empty, PluginBootStatus.Loaded, null));
			if (plugin is IFlagSource flagSource) catalog._flagSources.Add(flagSource);
			if (plugin is IMigrationSource migrationSource) catalog._migrationSources.Add(migrationSource);
			if (plugin is IBridgeSubscriptionSource bridgeSource) catalog._bridgeSources.Add(bridgeSource);
			if (plugin is IEndpointContributor endpointContributor) catalog._endpointContributors.Add(endpointContributor);
			if (plugin is IApplicationSource applicationSource) catalog._applicationSources.Add(applicationSource);
			if (plugin is ICommandInterceptor commandInterceptor) catalog._commandInterceptors.Add(commandInterceptor);
			if (plugin is IConnectionHook connectionHook) catalog._connectionHooks.Add(connectionHook);
			if (plugin is IObjectLifecycleHook objectLifecycleHook) catalog._objectLifecycleHooks.Add(objectLifecycleHook);
		}

		return catalog;
	}
}
