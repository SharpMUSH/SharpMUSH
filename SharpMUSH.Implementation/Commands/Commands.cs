using System.Reflection;
using Mediator;
using SharpMUSH.Configuration.Options;
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
		IValidateService validateService)
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