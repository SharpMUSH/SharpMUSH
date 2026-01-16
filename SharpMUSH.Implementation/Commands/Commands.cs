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