using Core.Arango;
using Core.Arango.Serialization.Newtonsoft;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using Testcontainers.ArangoDb;
using SharpMUSH.Server.ProtocolHandlers;

namespace SharpMUSH.Server;

public class Program
{
	static async Task Main()
	{
		var log = new LoggerConfiguration()
				.Enrich.FromLogContext()
				.WriteTo.Console(theme: AnsiConsoleTheme.Code)
				.MinimumLevel.Debug()
				.CreateLogger();

		var container = new ArangoDbBuilder()
			.WithReuse(true)
			.WithLabel("reuse-id", "SharpMUSH")
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

		CreateWebHostBuilder(config).Build().Run();
		await Task.CompletedTask;
	}

	public static IWebHostBuilder CreateWebHostBuilder(ArangoConfiguration acnf) =>
			WebHost
					.CreateDefaultBuilder()
					.UseStartup(x => new Startup(acnf))
					.UseKestrel(options =>
							options.ListenLocalhost(
									4202,
									builder => builder.UseConnectionHandler<TelnetServer>()
							)
					);
}
