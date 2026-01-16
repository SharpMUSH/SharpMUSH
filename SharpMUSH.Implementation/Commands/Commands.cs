using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands : ILibraryProvider<CommandDefinition>
{
	// Instance fields - each Commands instance has its own dependencies
	private readonly IMediator _mediator;
	private readonly ILocateService _locateService;
	private readonly IAttributeService _attributeService;
	private readonly INotifyService _notifyService;
	private readonly IPermissionService _permissionService;
	private readonly ICommandDiscoveryService _commandDiscoveryService;
	private readonly IOptionsWrapper<SharpMUSHOptions> _configuration;
	private readonly IPasswordService _passwordService;
	private readonly IConnectionService _connectionService;
	private readonly IExpandedObjectDataService _objectDataService;
	private readonly IManipulateSharpObjectService _manipulateSharpObjectService;
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly ICommunicationService _communicationService;
	private readonly IValidateService _validateService;
	private readonly ISqlService _sqlService;
	private readonly ILockService _lockService;
	private readonly IMoveService _moveService;
	private readonly ILogger<Commands> _logger;
	private readonly IHookService _hookService;
	private readonly IEventService _eventService;
	private readonly ITelemetryService _telemetryService;
	private readonly IPrometheusQueryService _prometheusQueryService;
	private readonly IWarningService _warningService;
	private readonly ITextFileService _textFileService;
	private readonly LibraryService<string, FunctionDefinition> _functionLibrary;
	
	private readonly CommandLibraryService _commandLibrary = [];
	
	/// <summary>
	/// Thread-static field to store the current Commands instance for this thread.
	/// Set by CommandParse before executing commands to provide the scoped instance.
	/// </summary>
	[ThreadStatic]
	private static Commands? _currentInstance;
	
	/// <summary>
	/// Sets the current Commands instance for the current thread.
	/// Called by CommandParse before executing commands.
	/// </summary>
	internal static void SetCurrentInstance(Commands instance)
	{
		_currentInstance = instance;
	}
	
	private static Commands? CurrentInstance => _currentInstance;
	
	// Static properties for backward compatibility - delegate to current instance
	private static IMediator? Mediator => CurrentInstance?._mediator;
	private static ILocateService? LocateService => CurrentInstance?._locateService;
	private static IAttributeService? AttributeService => CurrentInstance?._attributeService;
	private static INotifyService? NotifyService => CurrentInstance?._notifyService;
	private static IPermissionService? PermissionService => CurrentInstance?._permissionService;
	private static ICommandDiscoveryService? CommandDiscoveryService => CurrentInstance?._commandDiscoveryService;
	private static IOptionsWrapper<SharpMUSHOptions>? Configuration => CurrentInstance?._configuration;
	private static IPasswordService? PasswordService => CurrentInstance?._passwordService;
	private static IConnectionService? ConnectionService => CurrentInstance?._connectionService;
	private static IExpandedObjectDataService? ObjectDataService => CurrentInstance?._objectDataService;
	private static IManipulateSharpObjectService? ManipulateSharpObjectService => CurrentInstance?._manipulateSharpObjectService;
	private static IHttpClientFactory? HttpClientFactory => CurrentInstance?._httpClientFactory;
	private static ICommunicationService? CommunicationService => CurrentInstance?._communicationService;
	private static IValidateService? ValidateService => CurrentInstance?._validateService;
	private static ISqlService? SqlService => CurrentInstance?._sqlService;
	private static ILockService? LockService => CurrentInstance?._lockService;
	private static IMoveService? MoveService => CurrentInstance?._moveService;
	private static ILogger<Commands>? Logger => CurrentInstance?._logger;
	private static IHookService? HookService => CurrentInstance?._hookService;
	private static IEventService? EventService => CurrentInstance?._eventService;
	private static ITelemetryService? TelemetryService => CurrentInstance?._telemetryService;
	private static IPrometheusQueryService? PrometheusQueryService => CurrentInstance?._prometheusQueryService;
	private static IWarningService? WarningService => CurrentInstance?._warningService;
	private static ITextFileService? TextFileService => CurrentInstance?._textFileService;
	private static LibraryService<string, CommandDefinition>? CommandLibrary => CurrentInstance?._commandLibrary;
	private static LibraryService<string, FunctionDefinition>? FunctionLibrary => CurrentInstance?._functionLibrary;

	public LibraryService<string, CommandDefinition> Get() => _commandLibrary;

	public Commands(IMediator mediator,
		ILocateService locateService,
		IAttributeService attributeService,
		INotifyService notifyService,
		IPermissionService permissionService,
		ICommandDiscoveryService commandDiscoveryService,
		IOptionsWrapper<SharpMUSHOptions> configuration,
		IPasswordService passwordService,
		IConnectionService connectionService,
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
		IPrometheusQueryService prometheusQueryService,
		IWarningService warningService,
		ITextFileService textFileService,
		LibraryService<string, FunctionDefinition> functionLibrary)
	{
		_mediator = mediator;
		_locateService = locateService;
		_attributeService = attributeService;
		_notifyService = notifyService;
		_permissionService = permissionService;
		_commandDiscoveryService = commandDiscoveryService;
		_configuration = configuration;
		_passwordService = passwordService;
		_connectionService = connectionService;
		_objectDataService = objectDataService;
		_httpClientFactory = httpClientFactory;
		_manipulateSharpObjectService = manipulateSharpObjectService;
		_communicationService = communicationService;
		_validateService = validateService;
		_sqlService = sqlService;
		_lockService = lockService;
		_moveService = moveService;
		_logger = logger;
		_hookService = hookService;
		_eventService = eventService;
		_telemetryService = telemetryService;
		_prometheusQueryService = prometheusQueryService;
		_warningService = warningService;
		_textFileService = textFileService;
		_functionLibrary = functionLibrary;

		foreach (var command in Generated.CommandLibrary.Commands)
		{
			_commandLibrary.Add(command.Key, (command.Value, true));

			foreach (var alias in Configurable.CommandAliases.TryGetValue(command.Key, out var aliasList) ? aliasList : [])
			{
				_commandLibrary.Add(alias, (command.Value, true));
			}
		}
	}
}