using Core.Arango;
using Core.Arango.Serialization.Newtonsoft;
using Mediator;
using Serilog;
using SharpMUSH.Implementation;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using Testcontainers.ArangoDb;

namespace SharpMUSH.Tests;

public class BaseUnitTest
{
	public BaseUnitTest() =>
		Log.Logger = new LoggerConfiguration()
			.WriteTo.Console()
			.MinimumLevel.Debug()
			.CreateLogger();

	protected static ArangoDbContainer? Container;
	protected static Infrastructure? Infrastructure;
	protected static ISharpDatabase? Database;

	[After(Assembly)]
	public static async Task DisposeAssembly()
	{
		await Task.CompletedTask;
		Infrastructure!.Dispose();
	}

	[Before(Assembly)]
	public static async Task SetupAssembly()
	{
		Container = new ArangoDbBuilder()
			.WithImage("arangodb:latest")
			.WithPassword("password")
			.Build();

		await Container.StartAsync()
			.ConfigureAwait(false);

		var config = new ArangoConfiguration()
		{
			ConnectionString = $"Server={Container!.GetTransportAddress()};User=root;Realm=;Password=password;",
			Serializer = new ArangoNewtonsoftSerializer(new ArangoNewtonsoftDefaultContractResolver())
		};
		
		var configFile = Path.Combine(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst");
		Infrastructure = new Infrastructure(config, configFile);

		Database = Infrastructure!.Services.GetService(typeof(ISharpDatabase)) as ISharpDatabase;

		try
		{
			await Database!.Migrate();
		}
		catch (Exception ex)
		{
			Log.Fatal(ex, "Failed to migrate database");
		}
	}

	public static async Task<(ISharpDatabase Database, Infrastructure Infrastructure)> IntegrationServer()
	{
		await Task.CompletedTask;

		return (Database!, Infrastructure!);
	}

	public static IBooleanExpressionParser BooleanExpressionTestParser(ISharpDatabase database)
		=> new BooleanExpressionParser(database);

	public static async Task<IMUSHCodeParser> TestParser(
		IOptionsMonitor<PennMUSHOptions>? opts = null,
		IPasswordService? pws = null,
		IPermissionService? ps = null, // Permission Service needs the parser... this is circular. So we need to use the Mediator Pattern.
		ISharpDatabase? ds = null,
		IAttributeService? at = null,
		INotifyService? ns = null,
		ILocateService? ls = null,
		IExpandedObjectDataService? eo = null,
		ICommandDiscoveryService? cd = null,
		ITaskScheduler? qs = null,
		IConnectionService? cs = null,
		IMediator? ms = null)
	{
		
		var (database, integrationServer) = await IntegrationServer();

		var realOne = await database.GetObjectNodeAsync(new DBRef(1));
		var one = realOne.Object()!.DBRef;

		var simpleConnectionService = new ConnectionService();
		simpleConnectionService.Register("1", x => ValueTask.CompletedTask, () => Encoding.UTF8);
		simpleConnectionService.Bind("1", one);

		return new MUSHCodeParser(
			(integrationServer.Services.GetService(typeof(ILogger<MUSHCodeParser>)) as ILogger<MUSHCodeParser>)!,
			opts ?? (IOptionsMonitor<PennMUSHOptions>)integrationServer.Services.GetService(typeof(IOptionsMonitor<PennMUSHOptions>))!,
			pws ?? (IPasswordService)integrationServer.Services.GetService(typeof(IPasswordService))!,
			ps ?? (IPermissionService)integrationServer.Services.GetService(typeof(IPermissionService))!,
			at ?? (IAttributeService)integrationServer.Services.GetService(typeof(IAttributeService))!,
			ns ?? (INotifyService)integrationServer.Services.GetService(typeof(INotifyService))!,
			ls ?? (ILocateService)integrationServer.Services.GetService(typeof(ILocateService))!,
			eo ?? (IExpandedObjectDataService)integrationServer.Services.GetService(typeof(IExpandedObjectDataService))!,
			cd ?? (ICommandDiscoveryService)integrationServer.Services.GetService(typeof(ICommandDiscoveryService))!,
			qs ?? (ITaskScheduler)integrationServer.Services.GetService(typeof(ITaskScheduler))!,
			simpleConnectionService,
			ms ?? (IMediator)integrationServer.Services.GetService(typeof(IMediator))!,
			state: new ParserState(
				Registers: new([[]]),
				IterationRegisters: new(),
				RegexRegisters: new(),
				CurrentEvaluation: null,
				0,
				Function: null,
				Command: "think",
				Switches: [],
				Arguments: [],
				Executor: one,
				Enactor: one,
				Caller: one,
				Handle: "1"
			));
	}
}