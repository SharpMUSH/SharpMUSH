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
using Testcontainers.ArangoDb;

namespace SharpMUSH.Tests;

public class BaseUnitTest
{
		public BaseUnitTest() =>
			Log.Logger = new LoggerConfiguration()
				.WriteTo.Console()
				.MinimumLevel.Debug()
				.CreateLogger();

		public static async Task<(ISharpDatabase Database, Infrastructure Infrastructure)> IntegrationServer()
		{
				var container = new ArangoDbBuilder()
					.WithImage("arangodb:latest")
					.WithPassword("password")
					.Build();

				await container.StartAsync()
					.ConfigureAwait(false);

				var config = new ArangoConfiguration()
				{
						ConnectionString = $"Server={container.GetTransportAddress()};User=root;Realm=;Password=password;",
						Serializer = new ArangoNewtonsoftSerializer(new ArangoNewtonsoftDefaultContractResolver())
				};

				var testServer = new Infrastructure(config);
				var database = testServer.Services.GetService(typeof(ISharpDatabase)) as ISharpDatabase;

				try
				{
						await database!.Migrate();
				}
				catch (Exception ex)
				{
						Log.Fatal(ex, "Failed to migrate database");
				}

				return (database!, testServer);
		}

		public static IBooleanExpressionParser BooleanExpressionTestParser(ISharpDatabase database)
			=> new BooleanExpressionParser(database);

		public static async Task<IMUSHCodeParser> TestParser(
			IPasswordService? pws = null,
			IPermissionService? ps = null, // Permission Service needs the parser... this is circular. So we need to use the Mediator Pattern.
			ISharpDatabase? ds = null,
			IAttributeService? at = null,
			INotifyService? ns = null,
			ILocateService? ls = null,
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
					pws ?? (IPasswordService)integrationServer.Services.GetService(typeof(IPasswordService))!,
					ps ?? (IPermissionService)integrationServer.Services.GetService(typeof(IPermissionService))!,
					at ?? (IAttributeService)integrationServer.Services.GetService(typeof(IAttributeService))!,
					ns ?? (INotifyService)integrationServer.Services.GetService(typeof(INotifyService))!,
					ls ?? (ILocateService)integrationServer.Services.GetService(typeof(ILocateService))!,
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