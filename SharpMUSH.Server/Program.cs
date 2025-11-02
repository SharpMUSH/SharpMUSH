using Core.Arango;
using Core.Arango.Serialization.Newtonsoft;
using Core.Arango.Serilog;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;
using Serilog.Sinks.PeriodicBatching;
using Serilog.Sinks.SystemConsole.Themes;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Connectors;
using SharpMUSH.Server.ProtocolHandlers;
using Testcontainers.ArangoDb;

namespace SharpMUSH.Server;

public class Program
{
	public static async Task Main(params string[] args)
	{
		var builder = WebApplication.CreateBuilder(args);

		var arangoConnStr = Environment.GetEnvironmentVariable("ARANGO_CONNECTION_STRING");
		ArangoConfiguration config;

		if (string.IsNullOrWhiteSpace(arangoConnStr))
		{
			var container = new ArangoDbBuilder()
				.WithReuse(false)
				.WithLabel("reuse-id", "SharpMUSH")
				.WithImage("arangodb:latest")
				.WithPassword("password")
				.Build();

			await container.StartAsync();

			config = new ArangoConfiguration
			{
				ConnectionString = $"Server={container.GetTransportAddress()};User=root;Realm=;Password=password;",
				Serializer = new ArangoNewtonsoftSerializer(new ArangoNewtonsoftDefaultContractResolver())
			};
		}
		else
		{
			config = new ArangoConfiguration
			{
				ConnectionString = arangoConnStr,
				HttpClient = new HttpClient(UnixSocketHandler.CreateHandlerForUnixSocket("/var/run/arangodb3/arangodb.sock"))
				{
					BaseAddress = new Uri("http://localhost:8529") // Won't get used.
				},
				Serializer = new ArangoNewtonsoftSerializer(new ArangoNewtonsoftDefaultContractResolver())
			};
		}

		var colorFile = Path.Combine(AppContext.BaseDirectory, "colors.json");

		if (!File.Exists(colorFile))
		{
			throw new FileNotFoundException($"Configuration file not found: {colorFile}");
		}

		var startup = new Startup(config, colorFile);
		startup.ConfigureServices(builder.Services);

		builder.WebHost.ConfigureKestrel((_, options) =>
		{
			var optionMonitor = options.ApplicationServices.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
			var netValues = optionMonitor.CurrentValue.Net;

			options.AddServerHeader = true;

			options.ListenAnyIP(Convert.ToInt32(netValues.Port),
				listenOptions => { listenOptions.UseConnectionHandler<TelnetServer>(); });
			options.ListenAnyIP(Convert.ToInt32(netValues.PortalPort));
			options.ListenAnyIP(Convert.ToInt32(netValues.SslPortalPort)
				//, o => o.UseHttps()
			);
		});

		var app = builder.Build();

		await ConfigureApp(app).RunAsync();
	}

	private static WebApplication ConfigureApp(WebApplication app)
	{
		var env = app.Environment;
		app.UseRouting();
		app.UseCors();

		if (env.EnvironmentName == "Development")
		{
			app.UseDeveloperExceptionPage();
		}

		app.UseHttpsRedirection();
		app.UseAuthorization();
		app.MapControllers();
		app.MapRazorPages();

		return app;
	}
}