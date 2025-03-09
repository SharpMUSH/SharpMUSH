using Core.Arango;
using Core.Arango.Serialization.Newtonsoft;
using Core.Arango.Serilog;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Serilog;
using Serilog.Sinks.PeriodicBatching;
using Serilog.Sinks.SystemConsole.Themes;
using SharpMUSH.Server.ProtocolHandlers;
using Testcontainers.ArangoDb;

namespace SharpMUSH.Server;

public class Program
{
	public static async Task Main()
	{
		Log.Logger = new LoggerConfiguration()
			.Enrich.FromLogContext()
			.WriteTo.Console(theme: AnsiConsoleTheme.Code)
			.MinimumLevel.Debug()
			.CreateLogger();

		var container = new ArangoDbBuilder()
			// .WithReuse(true)
			.WithLabel("reuse-id", "SharpMUSH")
			.WithImage("arangodb:latest")
			.WithPassword("password")
			.Build();

		await container.StartAsync()
			.ConfigureAwait(false);

		var config = new ArangoConfiguration
		{
			ConnectionString = $"Server={container.GetTransportAddress()};User=root;Realm=;Password=password;",
			Serializer = new ArangoNewtonsoftSerializer(new ArangoNewtonsoftDefaultContractResolver())
		};

		Log.Logger = new LoggerConfiguration()
			.Enrich.FromLogContext()
			.WriteTo.Console(theme: AnsiConsoleTheme.Code)
			.WriteTo.Sink(new PeriodicBatchingSink(
				new ArangoSerilogSink(new ArangoContext(config), "logs", "logs"),
				new()
				{
					BatchSizeLimit = 1000,
					QueueLimit = 100000,
					Period = TimeSpan.FromSeconds(2),
					EagerlyEmitFirstEvent = true,
				}
			))
			.MinimumLevel.Debug()
			.CreateLogger();

		var configFile = Path.Combine(AppContext.BaseDirectory, "mushcnf.dst");

		if (!File.Exists(configFile))
		{
			throw new FileNotFoundException($"Configuration file not found: {configFile}");
		}

		await CreateWebHostBuilder(config, configFile).Build().RunAsync();
	}

	public static IWebHostBuilder CreateWebHostBuilder(ArangoConfiguration arangoConfig, string configFile) =>
		WebHost
			.CreateDefaultBuilder()
			.UseStartup(_ => new Startup(arangoConfig, configFile))
			.UseKestrel(options =>
				options.ListenLocalhost(
					4202,
					builder => builder.UseConnectionHandler<TelnetServer>()
				)
			);
}