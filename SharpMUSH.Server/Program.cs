using Core.Arango;
using Core.Arango.Serilog;
using KafkaFlow;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Sinks.PeriodicBatching;
using SharpMUSH.Database;
using SharpMUSH.Server.Strategy.ArangoDB;
using SharpMUSH.Server.Strategy.Redis;

namespace SharpMUSH.Server;

public class Program
{
	public static async Task Main(params string[] args)
	{
		var builder = WebApplication.CreateBuilder(args);

		var arangoConfig = await ArangoStartupStrategyProvider.GetStrategy().ConfigureArango();

		// Initialize Redis strategy
		var redisStrategy = RedisStrategyProvider.GetStrategy();
		await redisStrategy.InitializeAsync();

		var colorFile = Path.Combine(AppContext.BaseDirectory, "colors.json");

		if (!File.Exists(colorFile))
		{
			throw new FileNotFoundException($"Configuration file not found: {colorFile}");
		}

		var startup = new Startup(arangoConfig, colorFile, redisStrategy);

		startup.ConfigureServices(builder.Services, builder.Configuration);

		var app = builder.Build();

		// Configure Arango database logging sink now that DI is built (avoids BuildServiceProvider anti-pattern)
		var arangoContext = app.Services.GetRequiredService<IArangoContext>();
		Log.Logger = new LoggerConfiguration()
			.ReadFrom.Configuration(builder.Configuration)
			.WriteTo.Sink(new PeriodicBatchingSink(
				new ArangoSerilogSink(
					arangoContext,
					"CurrentSharpMUSHWorld",
					DatabaseConstants.Logs,
					ArangoSerilogSink.LoggingRenderStrategy.StoreTemplate,
					indexLevel: true,
					indexTimestamp: true,
					indexTemplate: true),
				new PeriodicBatchingSinkOptions
				{
					BatchSizeLimit = 1000,
					QueueLimit = 100000,
					Period = TimeSpan.FromSeconds(2),
					EagerlyEmitFirstEvent = true,
				}))
			.CreateLogger();

		// Get logger for startup logging
		var logger = app.Services.GetRequiredService<ILogger<Program>>();

		// Start Kafka bus
		logger.LogTrace("[KAFKA-STARTUP] Starting Kafka bus...");
		var bus = app.Services.CreateKafkaBus();
		await bus.StartAsync();
		logger.LogInformation("[KAFKA-STARTUP] Kafka bus started successfully");

		try
		{
			await ConfigureApp(app).RunAsync();
		}
		finally
		{
			await bus.StopAsync();
			await redisStrategy.DisposeAsync();
			await Log.CloseAndFlushAsync();
		}
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

		// Health and readiness endpoints for deployment checks
		app.MapGet("/health", () => "healthy");
		app.MapGet("/ready", () => "ready");

		// Prometheus metrics endpoint (for scraping, not for logging to console)
		app.MapPrometheusScrapingEndpoint();

		return app;
	}
}