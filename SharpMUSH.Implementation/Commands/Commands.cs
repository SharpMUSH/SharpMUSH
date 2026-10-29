using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Push;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands : ILibraryProvider<CommandDefinition>
{
	private IMediator Mediator { get; }
	/// <summary>
	/// The parent/zone cycle guard — the one thing a command needed a store for. It is the guard, not
	/// the store, so no command holds a write surface that bypasses the Mediator (engine data trunk §1, §2).
	/// </summary>
	private IRelationshipCycleChecker RelationshipCycles { get; }
	private ILocateService LocateService { get; }
	private IAttributeService AttributeService { get; }
	private INotifyService NotifyService { get; }
	private IPermissionService PermissionService { get; }
	private IChannelPermissionService ChannelPermissions { get; }
	private ICommandDiscoveryService CommandDiscoveryService { get; }
	private IOptionsWrapper<SharpMUSHOptions> Configuration { get; }
	private IPasswordService PasswordService { get; }
	private IConnectionService ConnectionService { get; }

	private ApplicationDraftService ApplicationDrafts { get; }
	private IOttStore OttStore { get; }
	private NativeLoginService NativeLogins { get; }
	private ILoginFailureTracker LoginFailures { get; }
	private IAccountService AccountService { get; }
	private IAccountSessionStore AccountSessionStore { get; }
	private IExpandedObjectDataService ObjectDataService { get; }
	private ILayoutThemeService LayoutThemeService { get; }
	private IHouseLookService HouseLook { get; }
	private IObjectNameService ObjectNameService { get; }
	private IFlagAndPowerService FlagAndPowerService { get; }
	private IObjectRelationshipService ObjectRelationshipService { get; }
	private IHttpClientFactory HttpClientFactory { get; }

	private ICommunicationService CommunicationService { get; }

	private IValidateService ValidateService { get; }

	private ISqlService SqlService { get; }

	private ILockService LockService { get; }

	private IMoveService MoveService { get; }

	private IDidItService DidItService { get; }

	private ILookService LookService { get; }

	private IObjectDestructionService ObjectDestructionService { get; }

	private ILogger<Commands> Logger { get; }

	private IHookService HookService { get; }

	private IEventService EventService { get; }

	private IConnectionAnnounceService ConnectionAnnounceService { get; }

	private ITelemetryService TelemetryService { get; }

	private IWarningService WarningService { get; }

	private ITextFileService TextFileService { get; }

	private IHelpTopicResolver HelpTopicResolver { get; }

	private IMessageBus MessageBus { get; }

	private IGameBroadcastService GameBroadcastService { get; }

	private ILocalizationService LocalizationService { get; }

	private ConfigurationReloadService ConfigReloadService { get; }

	private IBanEnforcer BanEnforcer { get; }

	private IWorldBackupService WorldBackupService { get; }

	private IBackupRunner BackupRunner { get; }

	private IServerRestart ServerRestart { get; }

	/// <summary>Lazy: the plugin administration reaches the package installer, which runs softcode through the parser.</summary>
	private Lazy<IPluginAdministration> PluginAdministration { get; }

	private IStorageCapacityService StorageCapacity { get; }

	private IAttributeIndexBuilder AttributeIndexBuilder { get; }

	private IHistoryRetentionService HistoryRetention { get; }

	private IBooleanExpressionParser BooleanExpressionParser { get; }

	private IPageLogService PageLog { get; }

	private IAuditLog Audit { get; }

	private IRecoverListService RecoverList { get; }

	private IMsspReportService MsspReport { get; }

	private IConnectionTrafficService ConnectionTraffic { get; }

	private IGameMessageService MessageService { get; }
	private IConfigOptionWriter ConfigWriter { get; }

	private ISecretStore Secrets { get; }

	private IWebPushService WebPush { get; }

	private IPortalPresence PortalPresence { get; }

	private IPipeOutputCapture PipeOutputCapture { get; }

	private LibraryService<string, CommandDefinition> CommandLibrary { get; }
	private ILibraryProvider<FunctionDefinition> Functions { get; }
	private LibraryService<string, FunctionDefinition> FunctionLibrary { get; }

	private readonly CommandLibraryService _commandLibrary = [];

	public LibraryService<string, CommandDefinition> Get() => _commandLibrary;

	public IReadOnlyDictionary<string, CommandDefinition> Builtins { get; }

	public Commands(IMediator mediator,
		IRelationshipCycleChecker relationshipCycles,
		ILocateService locateService,
		IAttributeService attributeService,
		INotifyService notifyService,
		IPermissionService permissionService,
		IChannelPermissionService channelPermissions,
		ICommandDiscoveryService commandDiscoveryService,
		IOptionsWrapper<SharpMUSHOptions> configuration,
		IPasswordService passwordService,
		IConnectionService connectionService,
		ApplicationDraftService applicationDrafts,
		IOttStore ottStore,
		IAccountService accountService,
		IAccountSessionStore accountSessionStore,
		IExpandedObjectDataService objectDataService,
		ILayoutThemeService layoutThemeService,
		IHouseLookService houseLook,
		IObjectNameService objectNameService,
		IFlagAndPowerService flagAndPowerService,
		IObjectRelationshipService objectRelationshipService,
		IHttpClientFactory httpClientFactory,
		ICommunicationService communicationService,
		IValidateService validateService,
		ISqlService sqlService,
		ILockService lockService,
		IMoveService moveService,
		IDidItService didItService,
		ILookService lookService,
		IObjectDestructionService objectDestructionService,
		ILogger<Commands> logger,
		IHookService hookService,
		IEventService eventService,
		IConnectionAnnounceService connectionAnnounceService,
		ITelemetryService telemetryService,
		IWarningService warningService,
		ITextFileService textFileService,
		IHelpTopicResolver helpTopicResolver,
		IMessageBus messageBus,
		ILocalizationService localizationService,
		IGameBroadcastService gameBroadcastService,
		ConfigurationReloadService configReloadService,
		IBanEnforcer banEnforcer,
		IWorldBackupService worldBackupService,
		IBackupRunner backupRunner,
		IServerRestart serverRestart,
		Lazy<IPluginAdministration> pluginAdministration,
		IStorageCapacityService storageCapacity,
		IAttributeIndexBuilder attributeIndexBuilder,
		IHistoryRetentionService historyRetention,
		IBooleanExpressionParser booleanExpressionParser,
		IPageLogService pageLog,
		IAuditLog audit,
		IRecoverListService recoverList,
		IMsspReportService msspReport,
		IConnectionTrafficService connectionTraffic,
		IGameMessageService gameMessages,
		IConfigOptionWriter configWriter,
		ISecretStore secrets,
		IWebPushService webPush,
		IPortalPresence portalPresence,
		ILibraryProvider<FunctionDefinition> functions,
		NativeLoginService nativeLogins,
		ILoginFailureTracker loginFailures,
		IPipeOutputCapture pipeOutputCapture)
	{
		Mediator = mediator;
		RelationshipCycles = relationshipCycles;
		LocateService = locateService;
		AttributeService = attributeService;
		NotifyService = notifyService;
		PermissionService = permissionService;
		ChannelPermissions = channelPermissions;
		CommandDiscoveryService = commandDiscoveryService;
		Configuration = configuration;
		PasswordService = passwordService;
		ConnectionService = connectionService;
		ApplicationDrafts = applicationDrafts;
		OttStore = ottStore;
		NativeLogins = nativeLogins;
		LoginFailures = loginFailures;
		AccountService = accountService;
		AccountSessionStore = accountSessionStore;
		ObjectDataService = objectDataService;
		LayoutThemeService = layoutThemeService;
		HouseLook = houseLook;
		HttpClientFactory = httpClientFactory;
		ObjectNameService = objectNameService;
		FlagAndPowerService = flagAndPowerService;
		ObjectRelationshipService = objectRelationshipService;
		CommunicationService = communicationService;
		ValidateService = validateService;
		SqlService = sqlService;
		LockService = lockService;
		MoveService = moveService;
		DidItService = didItService;
		LookService = lookService;
		ObjectDestructionService = objectDestructionService;
		Logger = logger;
		HookService = hookService;
		EventService = eventService;
		ConnectionAnnounceService = connectionAnnounceService;
		TelemetryService = telemetryService;
		WarningService = warningService;
		TextFileService = textFileService;
		HelpTopicResolver = helpTopicResolver;
		MessageBus = messageBus;
		LocalizationService = localizationService;
		GameBroadcastService = gameBroadcastService;
		ConfigReloadService = configReloadService;
		BanEnforcer = banEnforcer;
		WorldBackupService = worldBackupService;
		BackupRunner = backupRunner;
		ServerRestart = serverRestart;
		PluginAdministration = pluginAdministration;
		StorageCapacity = storageCapacity;
		AttributeIndexBuilder = attributeIndexBuilder;
		HistoryRetention = historyRetention;
		BooleanExpressionParser = booleanExpressionParser;
		PageLog = pageLog;
		Audit = audit;
		RecoverList = recoverList;
		MsspReport = msspReport;
		ConnectionTraffic = connectionTraffic;
		MessageService = gameMessages;
		ConfigWriter = configWriter;
		Secrets = secrets;
		WebPush = webPush;
		PortalPresence = portalPresence;
		PipeOutputCapture = pipeOutputCapture;
		Functions = functions;
		FunctionLibrary = functions.Get();

		Builtins = Generated.CommandLibrary.Create(this);
		foreach (var command in Builtins)
		{
			_commandLibrary.Add(command.Key, (command.Value, true));

			foreach (var alias in Configuration.CurrentValue.Alias.CommandAliases.TryGetValue(command.Key, out var aliasList) ? aliasList : [])
			{
				_commandLibrary.Add(alias, (command.Value, true));
			}
		}

		AddSwitchAliases(Configuration.CurrentValue.Alias.CommandAliases);

		// Store reference to this command library for @command introspection
		CommandLibrary = _commandLibrary;
		RememberBuiltinRestrictions();
	}

	/// <summary>
	/// Registers the aliases whose target names switches (<c>@FORCE/INPLACE</c> to <c>@sudo</c>): each alias runs the
	/// command with those switches put before the ones typed. A target whose command or switch does not exist is
	/// skipped with a warning, as is an alias already taken.
	/// </summary>
	private void AddSwitchAliases(Dictionary<string, string[]> aliases)
	{
		foreach (var (target, names) in aliases)
		{
			if (SwitchAliasDefinition(target) is not { } definition)
			{
				continue;
			}

			foreach (var name in names)
			{
				if (!_commandLibrary.TryAdd(name, (definition, true)))
				{
					Logger.LogWarning("Command alias {Alias} for {Target} is not registered: {Alias} is already a command", name, target, name);
				}
			}
		}
	}

	/// <summary>The definition an alias of <paramref name="target"/> runs, or <c>null</c> when it names no switch or no such command.</summary>
	internal CommandDefinition? SwitchAliasDefinition(string target)
	{
		var parts = target.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (parts.Length < 2)
		{
			return null;
		}

		if (!_commandLibrary.TryGetValue(parts[0].ToUpperInvariant(), out var command))
		{
			Logger.LogWarning("Command alias target {Target} names no command", target);
			return null;
		}

		var switches = parts[1..].Select(s => s.ToUpperInvariant()).ToArray();
		// "*" takes any switch, as @lock's lock types do.
		var known = command.LibraryInformation.Attribute.Switches ?? [];
		if (!known.Contains("*") && switches.FirstOrDefault(s => !known.Contains(s, StringComparer.OrdinalIgnoreCase)) is { } unknown)
		{
			Logger.LogWarning("Command alias target {Target} names switch {Switch}, which {Command} does not have", target, unknown, parts[0]);
			return null;
		}

		return command.LibraryInformation with { ImpliedSwitches = [.. command.LibraryInformation.ImpliedSwitches, .. switches] };
	}

	/// <summary>
	/// Rejects an invocation carrying fewer arguments than the command declares in
	/// <see cref="SharpCommandAttribute.MinArgs"/>: the player is told what was missing and where its help is, and
	/// the command returns the arity error the way functions do.
	/// <para>Commands declare MinArgs but the engine never enforced it, so a handler that indexed
	/// <c>Arguments["0"]</c> without checking threw <see cref="KeyNotFoundException"/> on a bare
	/// invocation. Enforcing it centrally in the dispatcher was measured and rejected: it pre-empts
	/// the specific usage messages that ~40 commands (@enable, @disable, @verb, @command, @respond,
	/// @atrchown, @include and others) already render for themselves, which is strictly worse for the
	/// player. This is the per-command opt-in instead — call it from handlers that would otherwise
	/// index an argument they never checked for.</para>
	/// </summary>
	/// <returns>The rejection to return, or <c>null</c> when the arity is satisfied.</returns>
	private async ValueTask<CallState?> RejectIfTooFewArguments(IMUSHCodeParser parser,
		SharpCommandAttribute attribute)
	{
		var count = parser.CurrentState.Arguments.Count;
		if (count >= attribute.MinArgs)
		{
			return null;
		}

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.TooFewCommandArgumentsFormat),
			executor, attribute.Name.ToLowerInvariant(), attribute.MinArgs, count);
		return new CallState(string.Format(ErrorMessages.Returns.TooFewCommandArguments,
			attribute.Name, attribute.MinArgs, count));
	}

	/// <summary>
	/// Rejects an invocation carrying fewer than <paramref name="minimum"/> arguments with the command's
	/// own usage message rather than the generic arity one. This is the shape for a guard that belongs to
	/// one switch (<c>@suggest/add</c>, <c>@quota/set</c>) or that PennMUSH words specifically
	/// (<c>@dolist</c>'s "What do you want to do with the list?").
	/// </summary>
	/// <param name="parser">The parser whose current arguments are counted.</param>
	/// <param name="minimum">The fewest arguments the command (or switch) accepts.</param>
	/// <param name="notified">Who is told; the executor, as PennMUSH's <c>notify(executor, ...)</c>.</param>
	/// <param name="usageKey">The localized notification key, as <c>nameof(ErrorMessages.Notifications.X)</c>.</param>
	/// <param name="errorReturn">The value the command returns when rejected.</param>
	/// <returns>The rejection to return, or <c>null</c> when the arity is satisfied.</returns>
	private async ValueTask<CallState?> RejectIfTooFewArguments(IMUSHCodeParser parser, int minimum,
		AnySharpObject notified, string usageKey, string errorReturn)
	{
		if (parser.CurrentState.Arguments.Count >= minimum)
		{
			return null;
		}

		await NotifyService.NotifyLocalized(notified, usageKey, notified);
		return new CallState(errorReturn);
	}
}
