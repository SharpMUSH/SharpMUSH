using Core.Arango;
using Core.Arango.Serialization.Newtonsoft;
using Core.Arango.Serilog;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Sinks.PeriodicBatching;
using Serilog.Sinks.SystemConsole.Themes;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.ProtocolHandlers;
using Testcontainers.ArangoDb;

namespace SharpMUSH.Server;

public class Program
{
	public static async Task Main(params string[] args)
	{
		var builder = WebApplication.CreateBuilder(args);

		var container = new ArangoDbBuilder()
			// .WithReuse(true)
			.WithLabel("reuse-id", "SharpMUSH")
			.WithImage("arangodb:latest")
			.WithPassword("password")
			.Build();

		await container.StartAsync();

		var config = new ArangoConfiguration
		{
			ConnectionString = $"Server={container.GetTransportAddress()};User=root;Realm=;Password=password;",
			Serializer = new ArangoNewtonsoftSerializer(new ArangoNewtonsoftDefaultContractResolver())
		};

		builder.Logging.AddSerilog(new LoggerConfiguration()
			.Enrich.FromLogContext()
			.WriteTo.Console(theme: AnsiConsoleTheme.Code)
			.WriteTo.Sink(new PeriodicBatchingSink(
				new ArangoSerilogSink(new ArangoContext(config),
					"logs",
					"logs",
					ArangoSerilogSink.LoggingRenderStrategy.StoreTemplate,
					true,
					true,
					true),
				new PeriodicBatchingSinkOptions
				{
					BatchSizeLimit = 1000,
					QueueLimit = 100000,
					Period = TimeSpan.FromSeconds(2),
					EagerlyEmitFirstEvent = true,
				}
			))
			.MinimumLevel.Debug()
			.CreateLogger());

		var colorFile = Path.Combine(AppContext.BaseDirectory, "colors.json");

		if (!File.Exists(colorFile))
		{
			throw new FileNotFoundException($"Configuration file not found: {colorFile}");
		}

		var startup = new Startup(config, colorFile, null);
		startup.ConfigureServices(builder.Services);
		
		builder.WebHost.ConfigureKestrel((_, options) =>
		{
			var optionMonitor = options.ApplicationServices.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
			var netValues = optionMonitor.CurrentValue.Net;

			options.ListenAnyIP(Convert.ToInt32(netValues.Port), listenOptions => { listenOptions.UseConnectionHandler<TelnetServer>(); });
			options.ListenAnyIP(Convert.ToInt32(netValues.PortalPort));
			options.ListenAnyIP(Convert.ToInt32(netValues.SslPortalPort), o => o.UseHttps());
		});

		var app = builder.Build();

		await ConfigureApp(app).RunAsync();
	}

	private static WebApplication ConfigureApp(WebApplication app)
	{
		app.UseHttpsRedirection();
		app.UseAuthorization();
		app.MapControllers();
		
		return app;
	}
}