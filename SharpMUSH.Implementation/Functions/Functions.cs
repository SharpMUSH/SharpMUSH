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
	
	/// <summary>
	/// Thread-static field to store the current Functions instance for this thread.
	/// Set by CommandParse before executing functions to provide the scoped instance.
	/// </summary>
	[ThreadStatic]
	private static Functions? _currentInstance;
	
	/// <summary>
	/// Sets the current Functions instance for the current thread.
	/// Called by CommandParse before executing functions.
	/// </summary>
	internal static void SetCurrentInstance(Functions instance)
	{
		_currentInstance = instance;
	}
	
	private static Functions? CurrentInstance => _currentInstance;
	
	// Static properties for backward compatibility - delegate to current instance
	private static IMediator? Mediator => CurrentInstance?._mediator;
	private static ILocateService? LocateService => CurrentInstance?._locateService;
	private static IAttributeService? AttributeService => CurrentInstance?._attributeService;
	private static INotifyService? NotifyService => CurrentInstance?._notifyService;
	private static IPermissionService? PermissionService => CurrentInstance?._permissionService;
	private static ICommandDiscoveryService? CommandDiscoveryService => CurrentInstance?._commandDiscoveryService;
	private static IOptionsWrapper<SharpMUSHOptions>? Configuration => CurrentInstance?._configuration;
	private static IOptionsWrapper<ColorsOptions>? ColorConfiguration => CurrentInstance?._colorConfiguration;
	private static IPasswordService? PasswordService => CurrentInstance?._passwordService;
	private static IConnectionService? ConnectionService => CurrentInstance?._connectionService;
	private static IExpandedObjectDataService? ObjectDataService => CurrentInstance?._objectDataService;
	private static IManipulateSharpObjectService? ManipulateSharpObjectService => CurrentInstance?._manipulateSharpObjectService;
	private static ICommunicationService? CommunicationService => CurrentInstance?._communicationService;
	private static IValidateService? ValidateService => CurrentInstance?._validateService;
	private static ISortService? SortService => CurrentInstance?._sortService;
	private static ILockService? LockService => CurrentInstance?._lockService;
	private static ISqlService? SqlService => CurrentInstance?._sqlService;
	private static ITelemetryService? TelemetryService => CurrentInstance?._telemetryService;
	private static IMoveService? MoveService => CurrentInstance?._moveService;
	private static IEventService? EventService => CurrentInstance?._eventService;
	private static IBooleanExpressionParser? BooleanExpressionParser => CurrentInstance?._booleanExpressionParser;
	private static ITextFileService? TextFileService => CurrentInstance?._textFileService;
	private static ILogger<Functions>? Logger => CurrentInstance?._logger;

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

		foreach (var command in Generated.FunctionLibrary.GetFunctions(this))
		{
			_functionLibrary.Add(command.Key, (command.Value, true));

			foreach (var alias in Configurable.FunctionAliases.TryGetValue(command.Key, out var aliasList) ? aliasList : [])
			{
				_functionLibrary.Add(alias, (command.Value, true));
			}
		}
	}
}