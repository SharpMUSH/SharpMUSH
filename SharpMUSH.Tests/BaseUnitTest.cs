using Serilog;
using SharpMUSH.Library.Services;
using NSubstitute;
using Core.Arango.Serialization.Newtonsoft;
using Core.Arango;
using SharpMUSH.IntegrationTests;
using Testcontainers.ArangoDb;
using SharpMUSH.Library.Models;
using System.Text;
using MediatR;
using SharpMUSH.Library;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Implementation;
using SharpMUSH.Library.Extensions;

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
		
		return (database!,testServer);
	}

	public static IBooleanExpressionParser BooleanExpressionTestParser(ISharpDatabase database)
		=> new BooleanExpressionParser(database);

	public static IMUSHCodeParser TestParser(
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
		var one = new DBRef(1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
		if (ds is not null)
		{
			var realOne = ds!.GetObjectNode(new DBRef(1));
			one = realOne.Object()!.DBRef;
		}

		// This needs adjustments, as the Database won't agree with the Milliseconds.
		var simpleConnectionService = new ConnectionService();
		simpleConnectionService.Register("1", x => ValueTask.CompletedTask, () => Encoding.UTF8);
		simpleConnectionService.Bind("1", one);

		return new MUSHCodeParser(
			pws ?? Substitute.For<IPasswordService>(),
			ps ?? Substitute.For<IPermissionService>(),
			ds ?? Substitute.For<ISharpDatabase>(),
			at ?? Substitute.For<IAttributeService>(),
			ns ?? Substitute.For<INotifyService>(),
			ls ?? Substitute.For<ILocateService>(),
			cd ?? Substitute.For<ICommandDiscoveryService>(),
			qs ?? Substitute.For<ITaskScheduler>(),
			cs ?? simpleConnectionService,
			ms ?? Substitute.For<IMediator>(),
			state: new ParserState(
				Registers: new([[]]),
				IterationRegisters: new(),
				RegexRegisters: new(),
				CurrentEvaluation: null,
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