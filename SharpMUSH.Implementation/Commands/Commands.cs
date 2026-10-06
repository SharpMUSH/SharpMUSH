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
	private IOttStore OttStore { get; }
	private IAccountService AccountService { get; }
	private IAccountSessionStore AccountSessionStore { get; }
	private IExpandedObjectDataService ObjectDataService { get; }
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

	private IStorageCapacityService StorageCapacity { get; }

	private IHistoryRetentionService HistoryRetention { get; }

	private IBooleanExpressionParser BooleanExpressionParser { get; }

	private IPageLogService PageLog { get; }

	private IAuditLog Audit { get; }

	private IMsspReportService MsspReport { get; }

	private IGameMessageService MessageService { get; }

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
		IOttStore ottStore,
		IAccountService accountService,
		IAccountSessionStore accountSessionStore,
		IExpandedObjectDataService objectDataService,
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
		IStorageCapacityService storageCapacity,
		IHistoryRetentionService historyRetention,
		IBooleanExpressionParser booleanExpressionParser,
		IPageLogService pageLog,
		IAuditLog audit,
		IMsspReportService msspReport,
		IGameMessageService gameMessages,
		ILibraryProvider<FunctionDefinition> functions)
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
		OttStore = ottStore;
		AccountService = accountService;
		AccountSessionStore = accountSessionStore;
		ObjectDataService = objectDataService;
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
		StorageCapacity = storageCapacity;
		HistoryRetention = historyRetention;
		BooleanExpressionParser = booleanExpressionParser;
		PageLog = pageLog;
		Audit = audit;
		MsspReport = msspReport;
		MessageService = gameMessages;
		Functions = functions;
		FunctionLibrary = functions.Get();

		Builtins = Generated.CommandLibrary.Create(this);
		foreach (var command in Builtins)
		{
			_commandLibrary.Add(command.Key, (command.Value, true));

			foreach (var alias in Configurable.CommandAliases.TryGetValue(command.Key, out var aliasList) ? aliasList : [])
			{
				_commandLibrary.Add(alias, (command.Value, true));
			}
		}

		// Store reference to this command library for @command introspection
		CommandLibrary = _commandLibrary;
		RememberBuiltinRestrictions();
	}

	/// <summary>
	/// Rejects an invocation carrying fewer arguments than the command declares in
	/// <see cref="SharpCommandAttribute.MinArgs"/>, reporting the arity the way functions already do.
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

		var message = string.Format(ErrorMessages.Returns.TooFewCommandArguments,
			attribute.Name, attribute.MinArgs, count);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		await NotifyService.Notify(executor, message, executor);
		return new CallState(message);
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
