using Core.Arango;
using Core.Arango.Serialization.Newtonsoft;
using Microsoft.AspNetCore.Builder;
using SharpMUSH.Server.Connectors;
using SharpMUSH.Server.Strategy.ArangoDB;
using Testcontainers.ArangoDb;

namespace SharpMUSH.Server;

public class Program
{
	public static async Task Main(params string[] args)
	{
		var builder = WebApplication.CreateBuilder(args);

		var arangoStrategyProvider = new ArangoStartupStrategyProvider().GetStrategy();
		var arangoConfig = await arangoStrategyProvider.ConfigureArango();

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

		return app;
	}
}