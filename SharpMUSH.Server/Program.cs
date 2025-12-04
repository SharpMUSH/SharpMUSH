using Microsoft.AspNetCore.Builder;
using SharpMUSH.Server.Strategy.ArangoDB;

namespace SharpMUSH.Server;

public class Program
{
	public static async Task Main(params string[] args)
	{
		var builder = WebApplication.CreateBuilder(args);

		var arangoConfig = await ArangoStartupStrategyProvider.GetStrategy().ConfigureArango();

		var colorFile = Path.Combine(AppContext.BaseDirectory, "colors.json");

		if (!File.Exists(colorFile))
		{
			throw new FileNotFoundException($"Configuration file not found: {colorFile}");
		}

		var startup = new Startup(arangoConfig, colorFile);
		
		startup.ConfigureServices(builder.Services);
		
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

		// Health and readiness endpoints for deployment checks
		app.MapGet("/health", () => "healthy");
		app.MapGet("/ready", () => "ready");

		// Prometheus metrics endpoint
		app.MapPrometheusScrapingEndpoint();

		return app;
	}
}