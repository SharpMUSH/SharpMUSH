using Core.Arango;
using Core.Arango.Serialization.Newtonsoft;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
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

		Log.Logger = log;

		var config = new ArangoConfiguration()
		{
			ConnectionString =
						"Server=http://127.0.0.1:8529;User=root;Realm=;Password=KJt7fVjUGFSl9Xqn;",
			Serializer = new ArangoNewtonsoftSerializer(
						new ArangoNewtonsoftDefaultContractResolver()
				)
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
