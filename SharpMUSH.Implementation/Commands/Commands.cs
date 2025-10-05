using System.Reflection;
using Mediator;
using Microsoft.Extensions.Options;
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
	private static IPermissionService? PermissionService {get;set;}
	private static ICommandDiscoveryService? CommandDiscoveryService {get;set;}
	private static IOptionsMonitor<SharpMUSHOptions>? Configuration { get; set; }
	private static IPasswordService? PasswordService { get; set; }
	private static IConnectionService? ConnectionService { get; set; }
	private static IExpandedObjectDataService? ObjectDataService { get; set; }
	private static IHttpClientFactory? HttpClientFactory { get; set; }
	
	private readonly CommandLibraryService _commandLibrary = [];

	public LibraryService<string, CommandDefinition> Get() => _commandLibrary;

	public Commands(IMediator mediator, 
		ILocateService locateService, 
		IAttributeService attributeService,
		INotifyService notifyService, 
		IPermissionService permissionService, 
		ICommandDiscoveryService commandDiscoveryService, 
		IOptionsMonitor<SharpMUSHOptions> configuration, 
		IPasswordService passwordService,
		IConnectionService connectionService,
		IExpandedObjectDataService objectDataService,
		IHttpClientFactory httpClientFactory)
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

		var knownBuiltInMethods =
			typeof(Commands)
				.GetMethods()
				.Select(m => (Method: m,
					Attribute: m.GetCustomAttribute<SharpCommandAttribute>(false)))
				.Where(x => x.Attribute is not null)
				.SelectMany(y =>
					(Configurable.CommandAliases.TryGetValue(y.Attribute!.Name, out var aliases)
						? aliases.Select(alias =>
							new KeyValuePair<string, (MethodInfo Method, SharpCommandAttribute Attribute)>(alias,
								(y.Method, y.Attribute!)))
						: [])
					.Append(new KeyValuePair<string, (MethodInfo Method, SharpCommandAttribute Attribute)>(y.Attribute.Name,
						(y.Method, y.Attribute!))))
				.ToDictionary();
		
		foreach (var knownMethod in knownBuiltInMethods)
		{
			_commandLibrary.Add(knownMethod.Key,
				((knownMethod.Value.Attribute,
					async p => await (ValueTask<Option<CallState>>)knownMethod.Value.Method.Invoke(null, [p, knownMethod.Value.Attribute])!), true));
		}
	}
}