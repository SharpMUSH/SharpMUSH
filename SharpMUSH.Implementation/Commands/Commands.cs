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
	
	// Thread-local current instance for static method access
	private static readonly AsyncLocal<Commands?> _currentInstance = new();
	
	// Static properties for backward compatibility - delegate to current instance
	private static IMediator? Mediator => _currentInstance.Value?._mediator;
	private static ILocateService? LocateService => _currentInstance.Value?._locateService;
	private static IAttributeService? AttributeService => _currentInstance.Value?._attributeService;
	private static INotifyService? NotifyService => _currentInstance.Value?._notifyService;
	private static IPermissionService? PermissionService => _currentInstance.Value?._permissionService;
	private static ICommandDiscoveryService? CommandDiscoveryService => _currentInstance.Value?._commandDiscoveryService;
	private static IOptionsWrapper<SharpMUSHOptions>? Configuration => _currentInstance.Value?._configuration;
	private static IPasswordService? PasswordService => _currentInstance.Value?._passwordService;
	private static IConnectionService? ConnectionService => _currentInstance.Value?._connectionService;
	private static IExpandedObjectDataService? ObjectDataService => _currentInstance.Value?._objectDataService;
	private static IManipulateSharpObjectService? ManipulateSharpObjectService => _currentInstance.Value?._manipulateSharpObjectService;
	private static IHttpClientFactory? HttpClientFactory => _currentInstance.Value?._httpClientFactory;
	private static ICommunicationService? CommunicationService => _currentInstance.Value?._communicationService;
	private static IValidateService? ValidateService => _currentInstance.Value?._validateService;
	private static ISqlService? SqlService => _currentInstance.Value?._sqlService;
	private static ILockService? LockService => _currentInstance.Value?._lockService;
	private static IMoveService? MoveService => _currentInstance.Value?._moveService;
	private static ILogger<Commands>? Logger => _currentInstance.Value?._logger;
	private static IHookService? HookService => _currentInstance.Value?._hookService;
	private static IEventService? EventService => _currentInstance.Value?._eventService;
	private static ITelemetryService? TelemetryService => _currentInstance.Value?._telemetryService;
	private static IPrometheusQueryService? PrometheusQueryService => _currentInstance.Value?._prometheusQueryService;
	private static IWarningService? WarningService => _currentInstance.Value?._warningService;
	private static ITextFileService? TextFileService => _currentInstance.Value?._textFileService;
	private static LibraryService<string, CommandDefinition>? CommandLibrary => _currentInstance.Value?._commandLibrary;
	private static LibraryService<string, FunctionDefinition>? FunctionLibrary => _currentInstance.Value?._functionLibrary;

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

		// Set this instance as the current instance for this async context
		_currentInstance.Value = this;

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