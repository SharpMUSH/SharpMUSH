using Asp.Versioning;
using Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Mcp;
using SharpMUSH.Server.Middleware;
using SharpMUSH.Server.RateLimiting;
using SharpMUSH.Server.Services;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using System.Globalization;
using System.Threading.RateLimiting;
using OpenTelemetry.ResourceDetectors.Container;
using Quartz;
using Serilog;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Database;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Implementation;
using SharpMUSH.Implementation.Commands;
using SharpMUSH.Implementation.Functions;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Behaviors;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.Interfaces;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
using SharpMUSH.Messaging.NATS;
using Microsoft.Extensions.Caching.Memory;
using ZiggyCreatures.Caching.Fusion;
using TaskScheduler = SharpMUSH.Library.Services.TaskScheduler;
namespace SharpMUSH.Server.Registration;

/// <summary>
/// The game engine and what it runs on: the services the parser, commands and functions resolve,
/// the options and logging stack, the message bus, the caches and the job scheduler.
/// </summary>
internal static class EngineRegistration
{
	/// <summary>Game services: permissions, movement, attributes, speech, locks, packages, plugins, the wiki.</summary>
	public static IServiceCollection AddSharpMushEngine(
		this IServiceCollection services, IConfiguration configuration, string natsUrl)
	{
		services.AddSingleton<PasswordHasher<string>, PasswordHasher<string>>(_ => new PasswordHasher<string>()
		/*
		 * PennMUSH Password Compatibility - IMPLEMENTED
		 *
		 * SharpMUSH uses PBKDF2 with HMAC-SHA512, 128-bit salt, 256-bit subkey, 100000 iterations
		 * for new passwords.
		 *
		 * PennMUSH uses SHA1 in password_hash, stored as: V:ALGO:HASH:TIMESTAMP
		 * - V: Version number (Currently 2)
		 * - ALGO: Digest algorithm (Default is SHA1)
		 * - HASH: Salted hash (first 2 chars are salt prepended to plaintext before hashing)
		 * - TIMESTAMP: Unix timestamp when password was set
		 *
		 * Salt characters: abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789
		 *
		 * The PasswordService now supports both formats:
		 * - Verification: Detects PennMUSH format and uses SHA1/SHA256 verification as needed
		 * - New passwords: Always use modern PBKDF2 (more secure)
		 *
		 * Users with imported PennMUSH passwords should reset their passwords for better security,
		 * but can still log in with their old passwords until they do.
		 */
		);
		services.AddSingleton<IPasswordService, PasswordService>();
		services.AddSingleton<SharpMUSH.Library.Reality.RealityPolicy>();
		services.AddSingleton<SharpMUSH.Library.Reality.RealityAdministration>();
		services.AddSingleton<SharpMUSH.Library.Reality.IRealityPolicy>(sp => sp.GetRequiredService<SharpMUSH.Library.Reality.RealityPolicy>());
		services.AddSingleton<IPermissionService, PermissionService>();
		services.AddSingleton<QueueDiagnosticsRecorder>();
		services.AddSingleton<IQueueDiagnosticsRecorder>(sp => sp.GetRequiredService<QueueDiagnosticsRecorder>());
		services.AddSingleton<ITelemetryInvocationObserver>(sp => sp.GetRequiredService<QueueDiagnosticsRecorder>());
		services.AddSingleton<IQueueDiagnosticsService, QueueDiagnosticsService>();
		services.AddHostedService<Services.QueueProfileCollector>();
		services.AddSingleton<ITelemetryService, TelemetryService>();

		services.AddSingleton<IConnectionStateStore>(sp =>
		{
			var logger = sp.GetRequiredService<ILogger<NatsConnectionStateStore>>();
			return NatsConnectionStateStore.CreateAsync(natsUrl, logger).GetAwaiter().GetResult();
		});

		services.AddSingleton<ILocalizationService, LocalizationService>();
		services.AddSingleton<INotifyService, NotifyService>();
		services.AddSingleton<ILocateService, LocateService>();
		services.AddSingleton<IMoveService, MoveService>();
		services.AddSingleton<IDidItService, DidItService>();
		services.AddSingleton<ILookService, LookService>();
		services.AddSingleton<IObjectDestructionService, ObjectDestructionService>();
		services.AddSingleton<IExpandedObjectDataService, ExpandedObjectDataService>();
		services.AddSingleton<IAttributeService, AttributeService>();
		services.AddSingleton<IEngineCommandInvoker, EngineCommandInvoker>();
		services.AddSingleton<IManipulateSharpObjectService, ManipulateSharpObjectService>();
		services.AddSingleton<ITaskScheduler, TaskScheduler>();
		services.AddSingleton<IQueueControlService, QueueControlService>();
		services.AddSingleton<IConnectionService, ConnectionService>();
		services.AddSingleton<IInputSessionService, InputSessionService>();
		services.AddHostedService<Services.InputSessionTimeoutService>();
		services.AddSingleton<IOttStore, InMemoryOttStore>();
		services.AddSingleton<HubConnectionRegistry>();
		services.AddSingleton<IVisibleWorldProjection, VisibleWorldProjection>();
		services.AddSingleton<IRoomEventDispatcher, RoomEventDispatcher>();
		services.AddSingleton<IAccountSessionStore, DatabaseAccountSessionStore>();
		services.AddSingleton<IAccountService, AccountService>();
		// Unconditional (not gated on JWT config) — AuthController's account-login/register and
		// AdminAccountsController's Wizard gate need it even when JWT auth isn't configured.
		services.AddSingleton<AccountClaimsService>();
		// Kept separate from AccountClaimsService so AccountService — which computes nothing but
		// must invalidate whenever it links or unlinks a character — can depend on it without
		// closing a cycle back through IAccountService. Library stays off Server.
		services.AddSingleton<IAccountClaimsInvalidator, AccountClaimsInvalidator>();
		// Task 15: gates AuthController/SetupController/GameHub on sitelock rules (!connect/!create/!guest).
		services.AddSingleton<SitelockGuard>();
		services.AddSingleton<BanEnforcementService>();
		// Library-layer call sites (AccountService, SitelockController) depend on IBanEnforcer, not
		// the concrete Server-layer BanEnforcementService, so Library stays off Server.
		services.AddSingleton<IBanEnforcer>(sp => sp.GetRequiredService<BanEnforcementService>());
		services.AddHostedService<BootstrapService>();
		services.AddSingleton<SetupService>();
		services.AddHostedService<RoleSeedService>();
		services.AddSingleton<ISqlService, SqlService>();
		services.AddSingleton<IPackageManifestService, PackageManifestService>();
		services.AddSingleton<IPackagePlanService, PackagePlanService>();
		// Parser-layer runner for package AINSTALL/AUPDATE softcode; required by PackageInstallService.
		services.AddSingleton<IPackageLifecycleRunner, SharpMUSH.Implementation.Services.PackageLifecycleRunner>();
		// Phase-4 managed-package (compiled C# plugin DLL) installer + its server-side trust allow-list.
		// The allow-list is read from the "ManagedPackages" config section (AllowAll / AllowList) and is
		// the standing half of the trust gate; the per-apply allow_managed_code flag is the other half.
		services.AddSingleton(_ =>
		{
			var section = configuration.GetSection("ManagedPackages");
			var allowAll = section.GetValue("AllowAll", false);
			var allowList = section.GetSection("AllowList").Get<string[]>() ?? [];
			return new ManagedPackageTrustOptions(allowAll, allowList);
		});
		services.AddSingleton<IManagedPackageInstaller>(sp => new ManagedPackageInstaller(
			sp.GetRequiredService<IPluginManager>(),
			sp.GetRequiredService<ManagedPackageTrustOptions>(),
			sp.GetRequiredService<ILogger<ManagedPackageInstaller>>()));
		// Serves a managed plugin's compiled UI assembly bytes to the WASM client, re-verifying them against
		// the Phase-4 install-time SHA-256 sidecar before serving. The PluginsUiController gates it on
		// allow_browser_code; this provider enforces the hash/traversal guards regardless.
		services.AddSingleton<IPluginUiAssemblyProvider>(sp =>
			new FileSystemPluginUiAssemblyProvider(
				sp.GetRequiredService<ILogger<FileSystemPluginUiAssemblyProvider>>()));
		services.AddSingleton<IPackageInstallService, PackageInstallService>();
		services.AddSingleton<IPackageAuthoringService, PackageAuthoringService>();
		services.AddSingleton<IPackageSourceService>(sp =>
			new Services.GitPackageSourceService(sp.GetRequiredService<IPackageManifestService>()));
		services.AddSingleton<ICommunicationService, CommunicationService>();
		services.AddSingleton<SpeechService>();
		services.AddSingleton<ILockService, LockService>();
		services.AddSingleton<IGameBroadcastService, GameBroadcastService>();
		services.AddSingleton<IConnectionAnnounceService, ConnectionAnnounceService>();
		services.AddSingleton<IBooleanExpressionParser, BooleanExpressionParser>();
		services.AddSingleton<ICommandDiscoveryService, CommandDiscoveryService>();
		services.AddSingleton<ISortService, SortService>();
		services.AddSingleton<IHookService, HookService>();
		services.AddSingleton<IEventService, EventService>();
		// Inbound HTTP: run http_handler <METHOD> attributes as commands (see help sharphttp).
		services.AddSingleton<IHttpOutputCapture, HttpOutputCapture>();
		services.AddSingleton<IHttpHandlerCommandDispatcher, HttpHandlerCommandService>();
		services.AddSingleton<IWarningService, WarningService>();
		services.AddSingleton<IChannelBufferService, InMemoryChannelBufferService>();
		services.AddSingleton<IListenPatternMatcher, ListenPatternMatcher>();
		services.AddSingleton<IListenerRoutingService, ListenerRoutingService>();
		services.AddSingleton<PennMUSHDatabaseParser>();
		services.AddSingleton<IPennMUSHDatabaseConverter, PennMUSHDatabaseConverter>();

		// Wiki subsystem — WikiStoreService over the database provider's IWikiStore (see RegisterDatabaseProvider).
		services.AddSingleton<WikiMarkdigPipeline>();

		// Locale fallback rules (pure) and the one localized-read service every reader path goes through.
		services.AddSingleton<IWikiLocaleResolver, WikiLocaleResolver>();
		services.AddSingleton<IWikiLocalizationService, WikiLocalizationService>();

		// Package, application, layout and role registries are the database provider too
		// (see RegisterDatabaseProvider).
		services.AddSingleton<IPermissionResolver, PermissionResolver>();
		services.AddSingleton<IAdministrativeCapabilityService, AdministrativeCapabilityService>();
		services.AddSingleton<SharpMUSH.Library.Services.RecurringJobs.IRecurringJobService, SharpMUSH.Library.Services.RecurringJobs.RecurringJobService>();
		services.AddSingleton<SharpMUSH.Library.Services.Snapshots.IObjectSnapshotService, SharpMUSH.Library.Services.Snapshots.ObjectSnapshotService>();
		services.AddTransient<Microsoft.AspNetCore.Authentication.IClaimsTransformation, FreshPermissionClaimsTransformation>();
		services.AddSingleton<IWikiAssetService, Server.Services.FileSystemWikiAssetService>();

		// Scene subsystem — ISceneService is NO LONGER implemented by core providers. It is registered by
		// the Scene plugin's IServiceRegistrar (ScenePlugin.RegisterServices -> services.AddSceneSystem),
		// which builds its storage over the host-shared Lightning storage accessor registered above and
		// wraps it with any registered behaviors. Removing the plugin leaves core with no scene storage.

		// Pre-render cache for bot-facing static HTML (backed by the shared IMemoryCache from FusionCache setup).
		services.AddMemoryCache();
		services.AddSingleton<Server.Services.IPrerenderCacheService, Server.Services.PrerenderCacheService>();

		services.AddSingleton<ITextFileService, Implementation.Services.TextFileService>();
		services.AddSingleton<ILocalizedTextFileService, Implementation.Services.LocalizedTextFileService>();
		// Shared by the in-game HELP/NEWS/AHELP commands and the portal's /api/help endpoints, so a
		// topic resolves identically at a telnet prompt and in a browser.
		services.AddSingleton<IHelpTopicResolver, Implementation.Services.HelpTopicResolver>();

		services.AddSingleton<ILibraryProvider<FunctionDefinition>, Functions>();
		services.AddSingleton<ILibraryProvider<CommandDefinition>, Commands>();
		// In-memory registry of global user-defined functions (@function). Not persisted:
		// durability comes from re-running @function on boot via the @STARTUP attribute pass.
		services.AddSingleton<IUserDefinedFunctionService, UserDefinedFunctionService>();
		services.AddSingleton(x => x.GetService<ILibraryProvider<FunctionDefinition>>()!.Get());
		services.AddSingleton(x => x.GetService<ILibraryProvider<CommandDefinition>>()!.Get());

		// Generic "plugins changed" notifier: after a plugin unload/reload, the PluginManager fires this to
		// broadcast ReceivePluginsChanged to every connected portal client, which forces a hard browser refresh
		// (the only way to reclaim a compiled component assembly loaded into the WASM runtime). Registered
		// before the PluginManager so its optional IPluginChangeNotifier ctor param resolves.
		services.AddSingleton<IPluginChangeNotifier, Server.Services.SignalRPluginChangeNotifier>();
		// C# plugin loader: discovers plugins/ DLLs at boot and registers their [SharpCommand]/[SharpFunction]
		// into the live command/function libraries with IsSystem=true (see PluginBootstrapService below).
		services.AddSingleton<IPluginManager, Implementation.Services.PluginManager>();
		// Phase 2b engine-extension hooks: the dispatcher the engine consults at its command/object seams,
		// reading the hook buckets the PluginCatalog collected. (Connection hooks are wired as
		// IConnectionService.ListenState listeners by PluginBootstrapService.)
		services.AddSingleton<IPluginHookDispatcher, Implementation.Services.PluginHookDispatcher>();

		return services;
	}

	/// <summary>Strongly-typed options, the Mediator pipeline, the parser and the shared HTTP clients.</summary>
	public static IServiceCollection AddSharpMushOptions(this IServiceCollection services, string colorFile)
	{
		services.AddSingleton<IOptionsFactory<SharpMUSHOptions>, OptionsService>();
		services.AddSingleton<IOptionsFactory<ColorsOptions>, ReadColorsOptionsFactory>();
		services.AddSingleton<ConfigurationReloadService>();
		services.AddSingleton<IOptionsChangeTokenSource<SharpMUSHOptions>>(sp =>
			sp.GetRequiredService<ConfigurationReloadService>());
		services.AddSingleton<ObjectVersions>();
		services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(CacheInvalidationBehavior<,>));
		services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(QueryCachingBehavior<,>));
		services.AddSingleton(typeof(IStreamPipelineBehavior<,>), typeof(StreamQueryCachingBehavior<,>));
		services.AddSingleton<IMUSHCodeParser, MUSHCodeParser>();
		// Lazy<T> for the services only reachable through a cycle: the lock service owns the expression
		// parser, and the locate and attribute services reach the lock service through permissions.
		services.AddTransient(typeof(Lazy<>), typeof(Library.Services.LazyService<>));
		services.AddSingleton<ILockEvaluationServices, Implementation.Services.LockEvaluationServices>();
		// Shared MUSH code intelligence — the single source of truth behind both the
		// Language Server (for editors) and the in-server MCP tools (for agents/tooling).
		services.AddSingleton<IMushCodeAnalyzer, MushCodeAnalyzer>();
		services.AddSingleton<IValidateService, ValidateService>();
		services.AddKeyedSingleton(nameof(colorFile), colorFile);
		services.AddOptions<SharpMUSHOptions>().ValidateOnStart();
		// ValidateSharpOptions, not the generated validator directly: it delegates to the generated one and
		// adds the checks the generator cannot express (currently Wiki.DefaultLocale being a real culture).
		// Registering the generated validator here would silently skip those.
		//
		// Singleton, because the only thing that resolves it is the singleton OptionsService — see its
		// remarks for why that factory runs the validators itself. A scoped validator is a captive
		// dependency of a singleton factory, and nothing noticed while nothing ran it.
		services.AddSingleton<IValidateOptions<SharpMUSHOptions>, ValidateSharpOptions>();
		services.AddOptions<ColorsOptions>().ValidateOnStart();
		services.AddSingleton<IOptionsWrapper<SharpMUSHOptions>, Library.Services.OptionsWrapper<SharpMUSHOptions>>();
		services.AddSingleton<IOptionsWrapper<ColorsOptions>, Library.Services.OptionsWrapper<ColorsOptions>>();
		services.AddHttpClient();
		services.AddHttpClient("api")
			.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
			{
				AutomaticDecompression = System.Net.DecompressionMethods.GZip
																 | System.Net.DecompressionMethods.Deflate
																 | System.Net.DecompressionMethods.Brotli
			});
		services.AddMediator();

		return services;
	}

	/// <summary>Serilog, read from configuration, replacing the default providers.</summary>
	public static IServiceCollection AddSharpMushLogging(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddLogging(logging =>
		{
			logging.ClearProviders();

			var loggerConfig = new LoggerConfiguration()
				.ReadFrom.Configuration(configuration);

			logging.AddSerilog(loggerConfig.CreateLogger());
		});

		return services;
	}

	/// <summary>NATS, joined as the main process rather than as a bridge client.</summary>
	public static IServiceCollection AddSharpMushMessaging(this IServiceCollection services, string natsUrl)
	{
		services.AddNatsMainProcessMessaging(
			options => { options.Url = natsUrl; },
			x =>
			{
				x.AddConsumer<Consumers.TelnetInputConsumer, TelnetInputMessage>();
				x.AddConsumer<Consumers.WebSocketInputConsumer, WebSocketInputMessage>();
				x.AddConsumer<Consumers.GMCPSignalConsumer, GMCPSignalMessage>();
				x.AddConsumer<Consumers.MSDPUpdateConsumer, MSDPUpdateMessage>();
				x.AddConsumer<Consumers.NAWSUpdateConsumer, NAWSUpdateMessage>();
				x.AddConsumer<Consumers.ConnectionEstablishedConsumer, ConnectionEstablishedMessage>();
				x.AddConsumer<Consumers.ConnectionClosedConsumer, ConnectionClosedMessage>();
				x.AddConsumer<Consumers.SessionResumeConsumer, SessionResumeRequestMessage>();
				x.AddConsumer<Consumers.PuebloNegotiatedConsumer, PuebloNegotiatedMessage>();
				x.AddConsumer<Consumers.MxpNegotiatedConsumer, MxpNegotiatedMessage>();
				x.AddConsumer<Consumers.TerminalTypeNegotiatedConsumer, TerminalTypeNegotiatedMessage>();
				x.AddConsumer<Consumers.TelnetNegotiatedConsumer, TelnetNegotiatedMessage>();
			});

		// The engine cache. Its own bounded memory cache rather than the registered one (which the
		// prerender service shares), and the per-query profiles applied by the caching behaviours -
		// see CacheEntryProfile for the fail-safe rule. The registered default is the Tagged profile,
		// the one that can never serve stale: an ad-hoc caller that only sets a duration (the account
		// claims cache, invalidated by tag when a ban or role change lands) inherits everything else
		// from here, and must not inherit fail-safe. Only the caching behaviour hands out the Object

		return services;
	}

	/// <summary>
	/// FusionCache — the shared cache and the dedicated compiled-expression one — and Quartz. Quartz
	/// runs a single thread on purpose: serial execution is what gives the command queue PennMUSH's
	/// FIFO ordering.
	/// </summary>
	public static IServiceCollection AddSharpMushCachingAndScheduling(this IServiceCollection services)
	{
		// profile, and only to queries whose invalidation is by key.
		services.AddFusionCache()
			.TryWithAutoSetup()
			.WithMemoryCache(_ => new MemoryCache(new MemoryCacheOptions
			{
				SizeLimit = CacheEntryProfiles.MemoryCacheSizeLimit,
				CompactionPercentage = CacheEntryProfiles.MemoryCacheCompactionPercentage,
			}))
			.WithDefaultEntryOptions(CacheEntryProfiles.Tagged);

		// Dedicated cache for compiled boolean-lock expressions.
		// Uses a size-limited memory cache (max 1024 entries, 25% compaction) so rarely-used
		// entries are evicted first under memory pressure while hot entries stay resident.
		services.AddFusionCache(Startup.CompiledExpressionsCacheName)
			.WithMemoryCache(_ => new MemoryCache(new MemoryCacheOptions
			{
				SizeLimit = 1024,
				CompactionPercentage = 0.25,
			}))
			.AsKeyedServiceByCacheName();
		services.AddQuartz(x =>
		{
			x.UseInMemoryStore();
			// Serial execution ensures FIFO queue ordering, matching PennMUSH behavior
			// where the command queue processes one entry at a time.
			x.UseDefaultThreadPool(tp => tp.MaxConcurrency = 1);
		});
		// Single web credential: the account-session token. AccountSession is the default
		// scheme in production; DebugAuth remains the dev default (auto-admin). MushBasic

		return services;
	}
}
