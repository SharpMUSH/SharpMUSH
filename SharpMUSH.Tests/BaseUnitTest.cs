using Serilog;
using System.Collections.Immutable;
using SharpMUSH.Library.Services;
using NSubstitute;
using Core.Arango.Serialization.Newtonsoft;
using Core.Arango;
using SharpMUSH.IntegrationTests;
using Testcontainers.ArangoDb;
using SharpMUSH.Library.Models;
using System.Text;
using SharpMUSH.Library;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Implementation;
using SharpMUSH.Library.Extensions;

namespace SharpMUSH.Tests
{
	public class BaseUnitTest
	{
		public BaseUnitTest() =>
			Log.Logger = new LoggerConfiguration()
												.WriteTo.Console()
												.MinimumLevel.Debug()
												.CreateLogger();

		public static async Task<ISharpDatabase> IntegrationServer()
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

			var TestServer = new Infrastructure(config);
			var database = TestServer.Services.GetService(typeof(ISharpDatabase)) as ISharpDatabase;

			await database!.Migrate();

			return database;
		}

		public static IBooleanExpressionParser BooleanExpressionTestParser(ISharpDatabase database)
			=> new BooleanExpressionParser(database);

		public static IMUSHCodeParser TestParser(
			IPasswordService? pws = null,
			IPermissionService? ps = null, // Permission Service needs the parser... this is circular. So we need to use the Mediator Pattern.
			ISharpDatabase? ds = null,
			INotifyService? ns = null,
			IQueueService? qs = null,
			IConnectionService? cs = null)
		{
			var one = new DBRef(1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
			if (ds != null)
			{
				var realOne = ds!.GetObjectNode(new DBRef(1));
				one = realOne.Object()!.DBRef;
			}

			// This needs adjustments, as the Database won't agree with the Milliseconds.
			var simpleConnectionService = new ConnectionService();
			simpleConnectionService.Register("1", (x) => Task.CompletedTask, () => Encoding.UTF8);
			simpleConnectionService.Bind("1", one);
			var stack = new Stack<ImmutableDictionary<string, MarkupString.MarkupStringModule.MarkupString>>();
			stack.Push(ImmutableDictionary<string, MarkupString.MarkupStringModule.MarkupString>.Empty);

			return new MUSHCodeParser(
					pws ?? Substitute.For<IPasswordService>(),
					ps ?? Substitute.For<IPermissionService>(),
					ds ?? Substitute.For<ISharpDatabase>(),
					ns ?? Substitute.For<INotifyService>(),
					qs ?? Substitute.For<IQueueService>(),
					cs ?? simpleConnectionService,
					state: new ParserState(
						Registers: stack,
						CurrentEvaluation: null,
						Function: null,
						Command: "think",
						Arguments: [],
						Executor: one,
						Enactor: one,
						Caller: one,
						Handle: "1"
					));
		}
	}
}
