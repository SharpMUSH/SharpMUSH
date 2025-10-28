﻿using System.Reflection;
using DotNext.Collections.Generic;
using Mediator;
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
	private static IMediator? Mediator { get; set; }
	private static ILocateService? LocateService { get; set; }
	private static IAttributeService? AttributeService { get; set; }
	private static INotifyService? NotifyService { get; set; }
	private static IPermissionService? PermissionService {get;set;}
	private static ICommandDiscoveryService? CommandDiscoveryService {get;set; }
	private static IOptionsWrapper<SharpMUSHOptions>? Configuration { get; set; }
	private static IOptionsWrapper<ColorsOptions>? ColorConfiguration { get; set; }
	private static IPasswordService? PasswordService { get; set; }
	private static IConnectionService? ConnectionService { get; set; }
	private static IExpandedObjectDataService? ObjectDataService { get; set; }
	private static IManipulateSharpObjectService? ManipulateSharpObjectService { get; set; }
	private static ICommunicationService? CommunicationService { get; set; }
	private static ISortService? SortService { get; set; }

	private readonly FunctionLibraryService _functionLibrary = [];
	
	public LibraryService<string, FunctionDefinition> Get() => _functionLibrary;

	public Functions(IMediator mediator, 
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
		ICommunicationService communicationService)
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
		ManipulateSharpObjectService = manipulateSharpObjectService;
		ObjectDataService = objectDataService;
		SortService = sortService;
		CommunicationService = communicationService;
		
		foreach (var command in Generated.FunctionLibrary.Functions)
		{
			_functionLibrary.Add(command.Key, (command.Value, true));
			
			foreach(var alias in Configurable.FunctionAliases.TryGetValue(command.Key, out var aliasList) ? aliasList : [])
			{
				_functionLibrary.Add(alias, (command.Value, true));
			}
		}
	}
}