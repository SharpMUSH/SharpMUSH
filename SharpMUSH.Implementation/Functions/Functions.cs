using Mediator;
using Microsoft.Extensions.DependencyInjection;
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
	/// <summary>Evaluate an argument in this function's current (possibly localized) scope without
	/// charging its evaluation to the function's own-time metric.</summary>
	private static async ValueTask<CallState?> ParseRawArgument(IMUSHCodeParser parser, MString text)
	{
		using var paused = InvocationClock.PauseRunning();
		return await parser.FunctionParse(text);
	}

	private IMediator Mediator { get; }
	/// <summary>
	/// The parent/zone cycle guard — the one thing a function needed a store for. It is the guard, not
	/// the store, so no function holds a write surface that bypasses the Mediator (engine data trunk §1, §2).
	/// </summary>
	private IRelationshipCycleChecker RelationshipCycles { get; }
	private ILocateService LocateService { get; }
	private IAttributeService AttributeService { get; }
	private INotifyService NotifyService { get; }
	private IPermissionService PermissionService { get; }
	private IChannelPermissionService ChannelPermissions { get; }
	private ICommandDiscoveryService CommandDiscoveryService { get; }
	private IOptionsWrapper<SharpMUSHOptions> Configuration { get; }
	private IOptionsWrapper<ColorsOptions> ColorConfiguration { get; }
	private IPasswordService PasswordService { get; }
	private IConnectionService ConnectionService { get; }
	private IExpandedObjectDataService ObjectDataService { get; }
	private ILayoutThemeService LayoutThemeService { get; }
	private IHouseLookService HouseLook { get; }
	private IObjectNameService ObjectNameService { get; }
	private IFlagAndPowerService FlagAndPowerService { get; }
	private IObjectRelationshipService ObjectRelationshipService { get; }
	private ICommunicationService CommunicationService { get; }
	private IValidateService ValidateService { get; }
	private ISortService SortService { get; }
	private ILockService LockService { get; }
	private ISqlService SqlService { get; }
	private ITelemetryService TelemetryService { get; }
	private IMoveService MoveService { get; }
	private IEventService EventService { get; }
	private IAccountService AccountService { get; }
	private IDidItService DidItService { get; }
	private IBooleanExpressionParser BooleanExpressionParser { get; }
	private ITextFileService TextFileService { get; }
	private ILogger<Functions> Logger { get; }
	private IMessageBus MessageBus { get; }
	private IActivityReportService ActivityReports { get; }

	private readonly FunctionLibraryService _functionLibrary = [];

	public LibraryService<string, FunctionDefinition> Get() => _functionLibrary;

	public IReadOnlyDictionary<string, FunctionDefinition> Builtins { get; }

	public Functions(
		ILogger<Functions> logger,
		IMediator mediator,
		IMessageBus messageBus,
		IRelationshipCycleChecker relationshipCycles,
		ILocateService locateService,
		IAttributeService attributeService,
		INotifyService notifyService,
		IPermissionService permissionService,
		IChannelPermissionService channelPermissions,
		ICommandDiscoveryService commandDiscoveryService,
		IOptionsWrapper<SharpMUSHOptions> configuration,
		IOptionsWrapper<ColorsOptions> colorOptions,
		IPasswordService passwordService,
		IConnectionService connectionService,
		IObjectNameService objectNameService,
		IFlagAndPowerService flagAndPowerService,
		IObjectRelationshipService objectRelationshipService,
		IExpandedObjectDataService objectDataService,
		ILayoutThemeService layoutThemeService,
		IHouseLookService houseLook,
		ISortService sortService,
		IValidateService validateService,
		ICommunicationService communicationService,
		ILockService lockService,
		ISqlService sqlService,
		ITelemetryService telemetryService,
		IMoveService moveService,
		IEventService eventService,
		IBooleanExpressionParser booleanExpressionParser,
		ITextFileService textFileService,
		IDidItService didItService,
		IAccountService accountService,
		IActivityReportService activityReports)
	{
		Logger = logger;
		Mediator = mediator;
		MessageBus = messageBus;
		RelationshipCycles = relationshipCycles;
		LocateService = locateService;
		AttributeService = attributeService;
		NotifyService = notifyService;
		PermissionService = permissionService;
		ChannelPermissions = channelPermissions;
		CommandDiscoveryService = commandDiscoveryService;
		Configuration = configuration;
		ColorConfiguration = colorOptions;
		PasswordService = passwordService;
		ConnectionService = connectionService;
		ObjectNameService = objectNameService;
		FlagAndPowerService = flagAndPowerService;
		ObjectRelationshipService = objectRelationshipService;
		ObjectDataService = objectDataService;
		LayoutThemeService = layoutThemeService;
		HouseLook = houseLook;
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
		DidItService = didItService;
		AccountService = accountService;
		ActivityReports = activityReports;

		Builtins = Generated.FunctionLibrary.Create(this).ToDictionary(pair => pair.Key, pair =>
			pair.Value with { RestrictedOperation = RestrictedOperations.Contains(pair.Key) ? pair.Key.ToLowerInvariant() : null });
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

	/// <summary>
	/// The compatibility options as the running parser sees them, read on every call so a change
	/// made with @config takes effect at once.
	/// </summary>
	private static CompatibilityOptions Compatibility(IMUSHCodeParser parser)
		=> parser.ServiceProvider.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Compatibility;
}