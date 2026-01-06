using System.Reflection;
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
	private static IMediator? Mediator { get; set; }
	private static ILocateService? LocateService { get; set; }
	private static IAttributeService? AttributeService { get; set; }
	private static INotifyService? NotifyService { get; set; }
	private static IPermissionService? PermissionService { get; set; }
	private static ICommandDiscoveryService? CommandDiscoveryService { get; set; }
	private static IOptionsWrapper<SharpMUSHOptions>? Configuration { get; set; }
	private static IPasswordService? PasswordService { get; set; }
	private static IConnectionService? ConnectionService { get; set; }
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
	
	private static IPrometheusQueryService? PrometheusQueryService { get; set; }
	
	private static IWarningService? WarningService { get; set; }
	
	private static ITextFileService? TextFileService { get; set; }
	
	private static LibraryService<string, CommandDefinition>? CommandLibrary { get; set; }
	private static LibraryService<string, FunctionDefinition>? FunctionLibrary { get; set; }

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
		Mediator = mediator;
		LocateService = locateService;
		AttributeService = attributeService;
		NotifyService = notifyService;
		PermissionService = permissionService;
		CommandDiscoveryService = commandDiscoveryService;
		Configuration = configuration;
		PasswordService = passwordService;
		ConnectionService = connectionService;
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
		PrometheusQueryService = prometheusQueryService;
		WarningService = warningService;
		TextFileService = textFileService;
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
}