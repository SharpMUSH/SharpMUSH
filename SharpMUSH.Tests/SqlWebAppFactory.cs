using System.Text;
using Core.Arango;
using Core.Arango.Serialization.Newtonsoft;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server;
using TUnit.Core.Interfaces;
using OneOf.Types;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;

namespace SharpMUSH.Tests;

public class SqlWebAppFactory : IAsyncInitializer
{
	[ClassDataSource<ArangoDbTestServer>(Shared = SharedType.PerTestSession)]
	public required ArangoDbTestServer ArangoDbTestServer { get; init; }

	[ClassDataSource<MySqlTestServer>(Shared = SharedType.PerTestSession)]
	public required MySqlTestServer MySqlTestServer { get; init; }

	public IServiceProvider Services => _server!.Services;
	private TestWebApplicationBuilderFactory<Program>? _server;
	private DBRef _one;

	public IMUSHCodeParser CommandParser
	{
		get
		{
			var integrationServer = _server!;
			return new MUSHCodeParser(
				integrationServer.Services.GetRequiredService<ILogger<MUSHCodeParser>>(),
				integrationServer.Services.GetRequiredService<LibraryService<string, FunctionDefinition>>(),
				integrationServer.Services.GetRequiredService<LibraryService<string, CommandDefinition>>(),
				integrationServer.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>(),
				integrationServer.Services,
				state: new ParserState(
					Registers: new([[]]),
					IterationRegisters: [],
					RegexRegisters: [],
					ExecutionStack: [],
					EnvironmentRegisters: [],
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
		var colorFile = Path.Combine(AppContext.BaseDirectory, "colors.json");

		// Create MySQL service with real connection
		var mysqlConnectionString = MySqlTestServer.Instance.GetConnectionString();
		var mysqlService = new MySqlService(mysqlConnectionString);

		_server = new TestWebApplicationBuilderFactory<Program>(config, configFile, colorFile, Substitute.For<INotifyService>(), mysqlService);

		var provider = _server.Services;
		var connectionService = provider.GetRequiredService<IConnectionService>();
		var databaseService = provider.GetRequiredService<ISharpDatabase>();

		// Migrate the database, which ensures we have a #1 object to bind to.
		await databaseService.Migrate();

		// Retrieve the object with DBRef #1 and bind it to a connection.
		var realOne = await databaseService.GetObjectNodeAsync(new DBRef(1));
		_one = realOne.Object()!.DBRef;
		connectionService.Register(1, "localhost", "localhost", "test", _ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		connectionService.Bind(1, _one);
	}
}
