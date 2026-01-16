using DotNext.Collections.Generic;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions : ILibraryProvider<FunctionDefinition>
{
	// Instance fields - each Functions instance has its own dependencies
	private readonly IMediator _mediator;
	private readonly ILocateService _locateService;
	private readonly IAttributeService _attributeService;
	private readonly INotifyService _notifyService;
	private readonly IPermissionService _permissionService;
	private readonly ICommandDiscoveryService _commandDiscoveryService;
	private readonly IOptionsWrapper<SharpMUSHOptions> _configuration;
	private readonly IOptionsWrapper<ColorsOptions> _colorConfiguration;
	private readonly IPasswordService _passwordService;
	private readonly IConnectionService _connectionService;
	private readonly IExpandedObjectDataService _objectDataService;
	private readonly IManipulateSharpObjectService _manipulateSharpObjectService;
	private readonly ICommunicationService _communicationService;
	private readonly IValidateService _validateService;
	private readonly ISortService _sortService;
	private readonly ILockService _lockService;
	private readonly ISqlService _sqlService;
	private readonly ITelemetryService _telemetryService;
	private readonly IMoveService _moveService;
	private readonly IEventService _eventService;
	private readonly IBooleanExpressionParser _booleanExpressionParser;
	private readonly ITextFileService _textFileService;
	private readonly ILogger<Functions> _logger;

	private readonly FunctionLibraryService _functionLibrary = [];
	
	// Thread-local current instance for static method access
	private static readonly AsyncLocal<Functions?> _currentInstance = new();
	
	/// <summary>
	/// Sets the current Functions instance for the current async context.
	/// This must be called before executing any functions to ensure static function methods
	/// access the correct instance.
	/// </summary>
	public static void SetCurrentInstance(Functions instance) => _currentInstance.Value = instance;
	
	// Static properties for backward compatibility - delegate to current instance
	private static IMediator? Mediator => _currentInstance.Value?._mediator;
	private static ILocateService? LocateService => _currentInstance.Value?._locateService;
	private static IAttributeService? AttributeService => _currentInstance.Value?._attributeService;
	private static INotifyService? NotifyService => _currentInstance.Value?._notifyService;
	private static IPermissionService? PermissionService => _currentInstance.Value?._permissionService;
	private static ICommandDiscoveryService? CommandDiscoveryService => _currentInstance.Value?._commandDiscoveryService;
	private static IOptionsWrapper<SharpMUSHOptions>? Configuration => _currentInstance.Value?._configuration;
	private static IOptionsWrapper<ColorsOptions>? ColorConfiguration => _currentInstance.Value?._colorConfiguration;
	private static IPasswordService? PasswordService => _currentInstance.Value?._passwordService;
	private static IConnectionService? ConnectionService => _currentInstance.Value?._connectionService;
	private static IExpandedObjectDataService? ObjectDataService => _currentInstance.Value?._objectDataService;
	private static IManipulateSharpObjectService? ManipulateSharpObjectService => _currentInstance.Value?._manipulateSharpObjectService;
	private static ICommunicationService? CommunicationService => _currentInstance.Value?._communicationService;
	private static IValidateService? ValidateService => _currentInstance.Value?._validateService;
	private static ISortService? SortService => _currentInstance.Value?._sortService;
	private static ILockService? LockService => _currentInstance.Value?._lockService;
	private static ISqlService? SqlService => _currentInstance.Value?._sqlService;
	private static ITelemetryService? TelemetryService => _currentInstance.Value?._telemetryService;
	private static IMoveService? MoveService => _currentInstance.Value?._moveService;
	private static IEventService? EventService => _currentInstance.Value?._eventService;
	private static IBooleanExpressionParser? BooleanExpressionParser => _currentInstance.Value?._booleanExpressionParser;
	private static ITextFileService? TextFileService => _currentInstance.Value?._textFileService;
	private static ILogger<Functions>? Logger => _currentInstance.Value?._logger;

	public LibraryService<string, FunctionDefinition> Get() => _functionLibrary;

	public Functions(
		ILogger<Functions> logger,
		IMediator mediator,
		ILocateService locateService,
		IAttributeService attributeService,
		INotifyService notifyService,
		IPermissionService permissionService,
		ICommandDiscoveryService commandDiscoveryService,
		IOptionsWrapper<SharpMUSHOptions> configuration,
		IOptionsWrapper<ColorsOptions> colorOptions,
		IPasswordService passwordService,
		IConnectionService connectionService,
		IManipulateSharpObjectService manipulateSharpObjectService,
		IExpandedObjectDataService objectDataService,
		ISortService sortService,
		IValidateService validateService,
		ICommunicationService communicationService,
		ILockService lockService,
		ISqlService sqlService,
		ITelemetryService telemetryService,
		IMoveService moveService,
		IEventService eventService,
		IBooleanExpressionParser booleanExpressionParser,
		ITextFileService textFileService)
	{
		_logger = logger;
		_mediator = mediator;
		_locateService = locateService;
		_attributeService = attributeService;
		_notifyService = notifyService;
		_permissionService = permissionService;
		_commandDiscoveryService = commandDiscoveryService;
		_configuration = configuration;
		_colorConfiguration = colorOptions;
		_passwordService = passwordService;
		_connectionService = connectionService;
		_manipulateSharpObjectService = manipulateSharpObjectService;
		_objectDataService = objectDataService;
		_sortService = sortService;
		_validateService = validateService;
		_communicationService = communicationService;
		_lockService = lockService;
		_sqlService = sqlService;
		_telemetryService = telemetryService;
		_moveService = moveService;
		_eventService = eventService;
		_booleanExpressionParser = booleanExpressionParser;
		_textFileService = textFileService;

		foreach (var command in Generated.FunctionLibrary.Functions)
		{
			_functionLibrary.Add(command.Key, (command.Value, true));

			foreach (var alias in Configurable.FunctionAliases.TryGetValue(command.Key, out var aliasList) ? aliasList : [])
			{
				_functionLibrary.Add(alias, (command.Value, true));
			}
		}
	}
}