using Serilog;
using System.Collections.Immutable;
using SharpMUSH.Library.Services;
using SharpMUSH.Database;
using NSubstitute;
using Core.Arango.Serialization.Newtonsoft;
using Core.Arango;
using SharpMUSH.IntegrationTests;
using Testcontainers.ArangoDb;
using SharpMUSH.Library.Models;

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
				.WithImage("arangodb:3.11.8")
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

		public static Implementation.Parser TestParser(
			IPasswordService? pws = null,
			IPermissionService? ps = null,
			ISharpDatabase? ds = null,
			INotifyService? ns = null,
			IQueueService? qs = null,
			IConnectionService? cs = null)
		{
			// This needs adjustments, as the Database won't agree with the Milliseconds.
			var one = new DBRef(1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
			var simpleConnectionService = new ConnectionService();
			simpleConnectionService.Register("1", (x) => Task.CompletedTask);
			simpleConnectionService.Bind("1", one);

			return new(
					pws ?? Substitute.For<IPasswordService>(),
					ps ?? Substitute.For<IPermissionService>(),
					ds ?? Substitute.For<ISharpDatabase>(),
					ns ?? Substitute.For<INotifyService>(),
					qs ?? Substitute.For<IQueueService>(),
					cs ?? simpleConnectionService,
					state: new Implementation.Parser.ParserState(
						Registers: ImmutableDictionary<string, MarkupString.MarkupStringModule.MarkupString>.Empty,
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
