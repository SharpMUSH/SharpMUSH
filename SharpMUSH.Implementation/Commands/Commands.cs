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
	private static IMediator? Mediator { get; set; }
	private static ISharpDatabase? Database { get; set; }
	private static ILocateService? LocateService { get; set; }
	private static IAttributeService? AttributeService { get; set; }
	private static INotifyService? NotifyService { get; set; }
	private static IPermissionService? PermissionService { get; set; }
	private static ICommandDiscoveryService? CommandDiscoveryService { get; set; }
	private static IOptionsWrapper<SharpMUSHOptions>? Configuration { get; set; }
	private static IPasswordService? PasswordService { get; set; }
	private static IConnectionService? ConnectionService { get; set; }
	private static IOttStore? OttStore { get; set; }
	private static IAccountService? AccountService { get; set; }
	private static IAccountSessionStore? AccountSessionStore { get; set; }
	private static IExpandedObjectDataService? ObjectDataService { get; set; }
	private static IManipulateSharpObjectService? ManipulateSharpObjectService { get; set; }
	private static IHttpClientFactory? HttpClientFactory { get; set; }

	private static ICommunicationService? CommunicationService { get; set; }

	private static IValidateService? ValidateService { get; set; }

	private static ISqlService? SqlService { get; set; }

	private static ILockService? LockService { get; set; }

	private static IMoveService? MoveService { get; set; }

	private static ILogger<Commands>? Logger { get; set; }

	private static IHookService? HookService { get; set; }

	private static IEventService? EventService { get; set; }

	private static ITelemetryService? TelemetryService { get; set; }

	private static IWarningService? WarningService { get; set; }

	private static ITextFileService? TextFileService { get; set; }

	private static IMessageBus? MessageBus { get; set; }

	private static IGameBroadcastService? GameBroadcastService { get; set; }

	private static ILocalizationService? LocalizationService { get; set; }

	private static ConfigurationReloadService? ConfigReloadService { get; set; }

	private static IBanEnforcer? BanEnforcer { get; set; }

	private static LibraryService<string, CommandDefinition>? CommandLibrary { get; set; }
	private static LibraryService<string, FunctionDefinition>? FunctionLibrary { get; set; }

	private readonly CommandLibraryService _commandLibrary = [];

	public LibraryService<string, CommandDefinition> Get() => _commandLibrary;

	public Commands(IMediator mediator,
		ISharpDatabase database,
		ILocateService locateService,
		IAttributeService attributeService,
		INotifyService notifyService,
		IPermissionService permissionService,
		ICommandDiscoveryService commandDiscoveryService,
		IOptionsWrapper<SharpMUSHOptions> configuration,
		IPasswordService passwordService,
		IConnectionService connectionService,
		IOttStore ottStore,
		IAccountService accountService,
		IAccountSessionStore accountSessionStore,
		IExpandedObjectDataService objectDataService,
		IManipulateSharpObjectService manipulateSharpObjectService,
		IHttpClientFactory httpClientFactory,
		ICommunicationService communicationService,
		IValidateService validateService,
		ISqlService sqlService,
		ILockService lockService,
		IMoveService moveService,
		ILogger<Commands> logger,
		IHookService hookService,
		IEventService eventService,
		ITelemetryService telemetryService,
		IWarningService warningService,
		ITextFileService textFileService,
		IMessageBus messageBus,
		ILocalizationService localizationService,
		IGameBroadcastService gameBroadcastService,
		ConfigurationReloadService configReloadService,
		IBanEnforcer banEnforcer,
		LibraryService<string, FunctionDefinition> functionLibrary)
	{
		Mediator = mediator;
		Database = database;
		LocateService = locateService;
		AttributeService = attributeService;
		NotifyService = notifyService;
		PermissionService = permissionService;
		CommandDiscoveryService = commandDiscoveryService;
		Configuration = configuration;
		PasswordService = passwordService;
		ConnectionService = connectionService;
		OttStore = ottStore;
		AccountService = accountService;
		AccountSessionStore = accountSessionStore;
		ObjectDataService = objectDataService;
		HttpClientFactory = httpClientFactory;
		ManipulateSharpObjectService = manipulateSharpObjectService;
		CommunicationService = communicationService;
		ValidateService = validateService;
		SqlService = sqlService;
		LockService = lockService;
		MoveService = moveService;
		Logger = logger;
		HookService = hookService;
		EventService = eventService;
		TelemetryService = telemetryService;
		WarningService = warningService;
		TextFileService = textFileService;
		MessageBus = messageBus;
		LocalizationService = localizationService;
		GameBroadcastService = gameBroadcastService;
		ConfigReloadService = configReloadService;
		BanEnforcer = banEnforcer;
		FunctionLibrary = functionLibrary;

		foreach (var command in Generated.CommandLibrary.Commands)
		{
			_commandLibrary.Add(command.Key, (command.Value, true));

			foreach (var alias in Configurable.CommandAliases.TryGetValue(command.Key, out var aliasList) ? aliasList : [])
			{
				_commandLibrary.Add(alias, (command.Value, true));
			}
		}

		// Store reference to this command library for @command introspection
		CommandLibrary = _commandLibrary;
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
	private static async ValueTask<CallState?> RejectIfTooFewArguments(IMUSHCodeParser parser,
		SharpCommandAttribute attribute)
	{
		var count = parser.CurrentState.Arguments.Count;
		if (count >= attribute.MinArgs)
		{
			return null;
		}

		var message = string.Format(ErrorMessages.Returns.TooFewCommandArguments,
			attribute.Name, attribute.MinArgs, count);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		await NotifyService!.Notify(executor, message, executor);
		return new CallState(message);
	}
}
