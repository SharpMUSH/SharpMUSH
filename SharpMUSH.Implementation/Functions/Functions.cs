using System.Reflection;
using Mediator;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

// TODO: FunctionProvider Interface.
public partial class Functions : ILibraryProvider<FunctionDefinition>
{
	private static IMediator? Mediator { get; set; }
	private static ILocateService? LocateService { get; set; }
	private static IAttributeService? AttributeService { get; set; }
	private static INotifyService? NotifyService { get; set; }
	private static IPermissionService? PermissionService {get;set;}
	private static ICommandDiscoveryService? CommandDiscoveryService {get;set; }
	private static IOptionsMonitor<SharpMUSHOptions>? Configuration { get; set; }
	private static IOptionsMonitor<ColorsOptions>? ColorConfiguration { get; set; }
	private static IPasswordService? PasswordService { get; set; }
	private static IConnectionService? ConnectionService { get; set; }
	private static IExpandedObjectDataService? ObjectDataService { get; set; }

	private readonly CommandLibraryService _commandLibrary = [];
	
	private readonly FunctionLibraryService _functionLibrary = [];
	
	public LibraryService<string, FunctionDefinition> Get() => _functionLibrary;

	public Functions(IMediator mediator, 
		ILocateService locateService, 
		IAttributeService attributeService,
		INotifyService notifyService, 
		IPermissionService permissionService, 
		ICommandDiscoveryService commandDiscoveryService,
		IOptionsMonitor<SharpMUSHOptions> configuration,
		IOptionsMonitor<ColorsOptions> colorOptions,
		IPasswordService passwordService,
		IConnectionService connectionService,
		IExpandedObjectDataService objectDataService)
	{
		Mediator = mediator;
		LocateService = locateService;
		AttributeService = attributeService;	
		NotifyService = notifyService;
		PermissionService = permissionService;
		CommandDiscoveryService = commandDiscoveryService;
		Configuration = configuration;
		ColorConfiguration = colorOptions;
		PasswordService = passwordService;
		ConnectionService = connectionService;
		ObjectDataService = objectDataService;
		
		var knownBuiltInMethods = typeof(Functions)
			.GetMethods()
			.Select(m => (Method: m,
				Attribute: m.GetCustomAttribute<SharpFunctionAttribute>(false)))
			.Where(x => x.Attribute is not null)
			.SelectMany(y =>
				(Configurable.FunctionAliases.TryGetValue(y.Attribute!.Name, out var aliases)
					? aliases.Select(alias =>
						new KeyValuePair<string, (MethodInfo Method, SharpFunctionAttribute Attribute)>(alias,
							(y.Method, y.Attribute!)))
					: [])
				.Append(new KeyValuePair<string, (MethodInfo Method, SharpFunctionAttribute Attribute)>(y.Attribute.Name,
					(y.Method, y.Attribute!))))
			.ToDictionary();
		
		foreach (var knownMethod in knownBuiltInMethods)
		{
			_functionLibrary.Add(knownMethod.Key,
				((knownMethod.Value.Attribute,
					async p => await (ValueTask<CallState>)knownMethod.Value.Method.Invoke(null, [p, knownMethod.Value.Attribute])!), true));
		}
	}
}