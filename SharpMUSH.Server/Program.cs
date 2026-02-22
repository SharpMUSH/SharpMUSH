using KafkaFlow;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

		// Get logger for startup logging
		var logger = app.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Program>>();

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