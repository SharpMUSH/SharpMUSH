using Core.Arango;
using Core.Arango.Serialization.Newtonsoft;
using Core.Arango.Serilog;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.PeriodicBatching;
using Serilog.Sinks.SystemConsole.Themes;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Database.ArangoDB;
using SharpMUSH.Implementation;
using SharpMUSH.Implementation.Commands;
using SharpMUSH.Implementation.Functions;
using SharpMUSH.Library;
using SharpMUSH.Library.Behaviors;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.ProtocolHandlers;
using Testcontainers.ArangoDb;
using TaskScheduler = SharpMUSH.Library.Services.TaskScheduler;

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

		var configFile = Path.Combine(AppContext.BaseDirectory, "mushcnf.dst");

		if (!File.Exists(configFile))
		{
			throw new FileNotFoundException($"Configuration file not found: {configFile}");
		}

		builder.WebHost.ConfigureKestrel((_, options) =>
		{
			options.ListenAnyIP(4203, listenOptions => { listenOptions.UseConnectionHandler<TelnetServer>(); });
			options.ListenAnyIP(5117);
			options.ListenAnyIP(7296, o => o.UseHttps());
		});

		var startup = new Startup(config, configFile, null);
		startup.ConfigureServices(builder.Services);

		var app = builder.Build();
		
		await ConfigureApp(app).RunAsync();
	}

	private static WebApplication ConfigureApp(WebApplication app)
	{
		if (app.Environment.IsDevelopment())
		{
			app.UseWebAssemblyDebugging();
		}
		else
		{
			app.UseDeveloperExceptionPage();
			// app.UseExceptionHandler("/Error");
			// The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
			app.UseHsts();
		}
		app.UseDefaultFiles();
		app.MapStaticAssets();
		app.UseHttpsRedirection();
		app.UseAuthorization();
		app.UseBlazorFrameworkFiles();
		app.UseStaticFiles();
		app.UseRouting();
		app.MapControllers();
		app.MapFallbackToFile("index.html");
		app.MapRazorPages();
		
		return app;
	}
}