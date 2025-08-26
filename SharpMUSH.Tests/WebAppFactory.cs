using System.Text;
using Core.Arango;
using Core.Arango.Serialization.Newtonsoft;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using OneOf.Types;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests;

public class WebAppFactory : IAsyncInitializer
{
	[ClassDataSource<ArangoDbTestServer>(Shared = SharedType.PerTestSession)]
	public required ArangoDbTestServer ArangoDbTestServer { get; init; }

	public IServiceProvider Services => _server!.Services;
	private TestWebServer? _server;
	private DBRef _one;

	public IMUSHCodeParser FunctionParser
	{
		get
		{
			var integrationServer = _server!;
			return new MUSHCodeParser(
				(integrationServer.Services.GetService(typeof(ILogger<MUSHCodeParser>)) as ILogger<MUSHCodeParser>)!,
				(IOptionsMonitor<PennMUSHOptions>)integrationServer.Services.GetService(typeof(IOptionsMonitor<PennMUSHOptions>))!,
				(IPasswordService)integrationServer.Services.GetService(typeof(IPasswordService))!,
				(IPermissionService)integrationServer.Services.GetService(typeof(IPermissionService))!,
				(IAttributeService)integrationServer.Services.GetService(typeof(IAttributeService))!,
				(INotifyService)integrationServer.Services.GetService(typeof(INotifyService))!,
				(ILocateService)integrationServer.Services.GetService(typeof(ILocateService))!,
				(IExpandedObjectDataService)integrationServer.Services.GetService(typeof(IExpandedObjectDataService))!,
				(ICommandDiscoveryService)integrationServer.Services.GetService(typeof(ICommandDiscoveryService))!,
				(IConnectionService)integrationServer.Services.GetService(typeof(IConnectionService))!,
				(LibraryService<string, FunctionDefinition>)integrationServer.Services.GetService(typeof(LibraryService<string, FunctionDefinition>))!,
				(LibraryService<string, CommandDefinition>)integrationServer.Services.GetService(typeof(LibraryService<string, CommandDefinition>))!,
				(IMediator)integrationServer.Services.GetService(typeof(IMediator))!,
				state: new ParserState(
					Registers: new([[]]),
					IterationRegisters: [],
					RegexRegisters: [],
					ExecutionStack: [],
					CurrentEvaluation: null,
					ParserFunctionDepth: 0,
					Function: null,
					Command: "think",
					CommandInvoker: _ => ValueTask.FromResult(new Option<CallState>(new None())),
					Switches: [],
					Arguments: [],
					Executor: _one,
					Enactor: _one,
					Caller: _one,
					Handle: 1
				));
		}
	}
	
	public IMUSHCodeParser CommandParser
	{
		get
		{
			var integrationServer = _server!;
			return new MUSHCodeParser(
				(integrationServer.Services.GetService(typeof(ILogger<MUSHCodeParser>)) as ILogger<MUSHCodeParser>)!,
				(IOptionsMonitor<PennMUSHOptions>)integrationServer.Services.GetService(typeof(IOptionsMonitor<PennMUSHOptions>))!,
				(IPasswordService)integrationServer.Services.GetService(typeof(IPasswordService))!,
				(IPermissionService)integrationServer.Services.GetService(typeof(IPermissionService))!,
				(IAttributeService)integrationServer.Services.GetService(typeof(IAttributeService))!,
				(INotifyService)integrationServer.Services.GetService(typeof(INotifyService))!,
				(ILocateService)integrationServer.Services.GetService(typeof(ILocateService))!,
				(IExpandedObjectDataService)integrationServer.Services.GetService(typeof(IExpandedObjectDataService))!,
				(ICommandDiscoveryService)integrationServer.Services.GetService(typeof(ICommandDiscoveryService))!,
				(IConnectionService)integrationServer.Services.GetService(typeof(IConnectionService))!,
				(LibraryService<string, FunctionDefinition>)integrationServer.Services.GetService(typeof(LibraryService<string, FunctionDefinition>))!,
				(LibraryService<string, CommandDefinition>)integrationServer.Services.GetService(typeof(LibraryService<string, CommandDefinition>))!,
				(IMediator)integrationServer.Services.GetService(typeof(IMediator))!,
				state: new ParserState(
					Registers: new([[]]),
					IterationRegisters: [],
					RegexRegisters: [],
					ExecutionStack: [],
					CurrentEvaluation: null,
					ParserFunctionDepth: 0,
					Function: null,
					Command: null,
					CommandInvoker: _ => ValueTask.FromResult(new Option<CallState>(new None())),
					Switches: [],
					Arguments: [],
					Executor: _one,
					Enactor: _one,
					Caller: _one,
					Handle: 1
				));
		}
	}
	
	public async Task InitializeAsync()
	{
		var log = new LoggerConfiguration()
			.Enrich.FromLogContext()
			.WriteTo.Console(theme: AnsiConsoleTheme.Code)
			.MinimumLevel.Debug()
			.CreateLogger();
		
		Log.Logger = log;
		
		var config = new ArangoConfiguration
		{
			ConnectionString = $"Server={ArangoDbTestServer.Instance.GetTransportAddress()};User=root;Realm=;Password=password;",
			Serializer = new ArangoNewtonsoftSerializer(new ArangoNewtonsoftDefaultContractResolver())
		};

		var configFile = Path.Combine(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst");

		_server = new TestWebServer(config, configFile, Substitute.For<INotifyService>());

		var provider = _server.Services;
		var connectionService = provider.GetRequiredService<IConnectionService>();
		var databaseService = provider.GetRequiredService<ISharpDatabase>();
		
		// Migrate the database, which ensures we have a #1 object to bind to.
		await databaseService.Migrate();

		// Retrieve the object with DBRef #1 and bind it to a connection.
		var realOne = await databaseService.GetObjectNodeAsync(new DBRef(1));
		_one = realOne.Object()!.DBRef;
		connectionService.Register(1, _ => ValueTask.CompletedTask,  _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		connectionService.Bind(1, _one);
	}
}