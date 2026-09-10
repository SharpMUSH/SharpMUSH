using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions : ILibraryProvider<FunctionDefinition>
{
	private IMediator Mediator { get; }
	/// <summary>
	/// The object store, and only that: the cycle guards in <see cref="HelperFunctions"/> are the
	/// sole reason a function reaches a store at all, and they take an <see cref="IObjectStore"/>.
	/// Holding the whole <see cref="ISharpDatabase"/> composite here would hand every function a
	/// write surface that bypasses the Mediator (engine data trunk §1, §2).
	/// </summary>
	private IObjectStore Database { get; }
	private ILocateService LocateService { get; }
	private IAttributeService AttributeService { get; }
	private INotifyService NotifyService { get; }
	private IPermissionService PermissionService { get; }
	private ICommandDiscoveryService CommandDiscoveryService { get; }
	private IOptionsWrapper<SharpMUSHOptions> Configuration { get; }
	private IOptionsWrapper<ColorsOptions> ColorConfiguration { get; }
	private IPasswordService PasswordService { get; }
	private IConnectionService ConnectionService { get; }
	private IExpandedObjectDataService ObjectDataService { get; }
	private IManipulateSharpObjectService ManipulateSharpObjectService { get; }
	private ICommunicationService CommunicationService { get; }
	private IValidateService ValidateService { get; }
	private ISortService SortService { get; }
	private ILockService LockService { get; }
	private ISqlService SqlService { get; }
	private ITelemetryService TelemetryService { get; }
	private IMoveService MoveService { get; }
	private IEventService EventService { get; }
	private IBooleanExpressionParser BooleanExpressionParser { get; }
	private ITextFileService TextFileService { get; }
	private ILogger<Functions> Logger { get; }
	private IMessageBus MessageBus { get; }

	private readonly FunctionLibraryService _functionLibrary = [];

	public LibraryService<string, FunctionDefinition> Get() => _functionLibrary;

	public IReadOnlyDictionary<string, FunctionDefinition> Builtins { get; }

	public Functions(
		ILogger<Functions> logger,
		IMediator mediator,
		IMessageBus messageBus,
		IObjectStore database,
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
		Logger = logger;
		Mediator = mediator;
		MessageBus = messageBus;
		Database = database;
		LocateService = locateService;
		AttributeService = attributeService;
		NotifyService = notifyService;
		PermissionService = permissionService;
		CommandDiscoveryService = commandDiscoveryService;
		Configuration = configuration;
		ColorConfiguration = colorOptions;
		PasswordService = passwordService;
		ConnectionService = connectionService;
		ManipulateSharpObjectService = manipulateSharpObjectService;
		ObjectDataService = objectDataService;
		SortService = sortService;
		ValidateService = validateService;
		CommunicationService = communicationService;
		LockService = lockService;
		SqlService = sqlService;
		TelemetryService = telemetryService;
		MoveService = moveService;
		EventService = eventService;
		BooleanExpressionParser = booleanExpressionParser;
		TextFileService = textFileService;

		Builtins = Generated.FunctionLibrary.Create(this);
		foreach (var command in Builtins)
		{
			_functionLibrary.Add(command.Key, (command.Value, true));
			_functionLibrary.ReserveSystemName(command.Key);

			foreach (var alias in Configurable.FunctionAliases.TryGetValue(command.Key, out var aliasList) ? aliasList : [])
			{
				_functionLibrary.Add(alias, (command.Value, true));
				_functionLibrary.ReserveSystemName(alias);
			}
		}
	}
}