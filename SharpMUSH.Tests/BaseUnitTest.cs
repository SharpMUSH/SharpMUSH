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

namespace AntlrCSharp.Tests
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
			ISharpDatabase? ds = null) 
			=> new(
					pws ?? Substitute.For<IPasswordService>(),
					ps ?? Substitute.For<IPermissionService>(),
					ds ?? Substitute.For<ISharpDatabase>(),
					state: new Implementation.Parser.ParserState(
						Registers: ImmutableDictionary<string, MarkupString.MarkupStringModule.MarkupString>.Empty,
						CurrentEvaluation: null,
						Function: null,
						Command: "think",
						Arguments: [],
						Executor: new DBRef(1),
						Enactor: new DBRef(1),
						Caller: new DBRef(1)
					));
	}
}
