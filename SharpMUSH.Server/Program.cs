using Core.Arango;
using Core.Arango.Serilog;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Sinks.PeriodicBatching;
using SharpMUSH.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Messaging.NATS.Strategy;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Middleware;
using SharpMUSH.Server.Strategy.ArangoDB;

namespace SharpMUSH.Server;

public class Program
{
	public static async Task Main(params string[] args)
	{
		var dbProviderStr = Environment.GetEnvironmentVariable("SHARPMUSH_DATABASE_PROVIDER");
		var databaseProvider = string.Equals(dbProviderStr, "memgraph", StringComparison.OrdinalIgnoreCase)
			? DatabaseProvider.Memgraph
			: string.Equals(dbProviderStr, "surrealdb", StringComparison.OrdinalIgnoreCase)
				? DatabaseProvider.SurrealDB
				: DatabaseProvider.ArangoDB;

		ArangoConfiguration? arangoConfig = null;
		string? memgraphUri = null;

		if (databaseProvider == DatabaseProvider.Memgraph)
		{
			memgraphUri = Environment.GetEnvironmentVariable("MEMGRAPH_URI") ?? "bolt://localhost:7687";
		}
		else if (databaseProvider == DatabaseProvider.ArangoDB)
		{
			arangoConfig = await ArangoStartupStrategyProvider.GetStrategy().ConfigureArango();
		}
		// SurrealDB uses embedded in-memory mode, no external configuration needed

		// Resolve the NATS URL.  Ownership of the testcontainer (when NATS_URL is not set)
		// belongs to ConnectionServer; Server only needs the URL to connect.
		var natsStrategy = NatsStrategyProvider.GetStrategy();
		var natsUrl = await natsStrategy.GetUrlAsync();

		var colorFile = Path.Combine(AppContext.BaseDirectory, "colors.json");

		if (!File.Exists(colorFile))
		{
			throw new FileNotFoundException($"Configuration file not found: {colorFile}");
		}

		var builder = WebApplication.CreateBuilder(args);
		var startup = new Startup(arangoConfig, colorFile, natsUrl, databaseProvider, memgraphUri);
		startup.ConfigureServices(builder.Services, builder.Configuration, builder.Environment);

		var app = builder.Build();

		if (databaseProvider == DatabaseProvider.ArangoDB)
		{
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
		}

		var logger = app.Services.GetRequiredService<ILogger<Program>>();
		logger.LogInformation("[NATS] Connected to NATS at {NatsUrl}", natsUrl);

		try
		{
			await ConfigureApp(app).RunAsync();
		}
		finally
		{
			await Log.CloseAndFlushAsync();
		}
	}

	private static WebApplication ConfigureApp(WebApplication app)
	{
		var env = app.Environment;
		app.UseRouting();
		app.UseCors();

		// RFC 7807 exception handler covers all environments (dev too);
		// DeveloperExceptionPage is left in as an additional dev aid for HTML views.
		app.UseExceptionHandler();

		if (env.EnvironmentName == "Development")
		{
			app.UseDeveloperExceptionPage();
		}

		app.UseHttpsRedirection();

		// ── URL canonicalisation: must run before static files so redirects fire first
		app.UseMiddleware<CanonicalUrlMiddleware>();

		app.UseStaticFiles();

		app.UseMiddleware<BotDetectionMiddleware>();

		app.UseMiddleware<BotPrerenderMiddleware>();

		app.UseAuthentication();
		app.UseAuthorization();
		app.UseRateLimiter();
		app.MapControllers();
		app.MapRazorPages();
		app.MapHub<GameHub>("/hubs/game");

		// Phase 9 — plugin web-contribution seam: after the host maps its own controllers/hubs, let each
		// plugin implementing IEndpointContributor map its endpoints (hubs/routes) into the pipeline. The
		// Scene plugin maps its SceneHub at /hubs/scene here. Each is isolated so a single failing plugin
		// cannot abort endpoint mapping for the rest.
		var pluginCatalog = app.Services.GetRequiredService<SharpMUSH.Implementation.Services.PluginCatalog>();
		var endpointLogger = app.Services.GetRequiredService<ILogger<Program>>();
		foreach (var contributor in pluginCatalog.EndpointContributors)
		{
			try
			{
				contributor.MapEndpoints(app);
			}
			catch (Exception ex)
			{
				endpointLogger.LogError(ex,
					"[Plugins] Endpoint contributor '{Contributor}' threw while mapping endpoints; skipping it.",
					contributor.GetType().FullName);
			}
		}

		app.MapGet("/health", () => "healthy");
		app.MapGet("/ready", () => "ready");

		// Inbound HTTP to the MUSH: /http/<path> runs the http_handler's <METHOD> attribute as
		// commands, PennMUSH-style (see help sharphttp). Prefixed (rather than a catch-all) so it
		// cannot shadow the portal's routes.
		app.Map("/http/{**path}", HandleMushHttpRequest);

		app.MapPrometheusScrapingEndpoint();

		// SPA fallback: all non-API, non-static routes serve index.html so that
		// Blazor WASM handles client-side routing (deep links, browser refresh).
		app.MapFallbackToFile("index.html");

		return app;
	}

	/// <summary>
	/// Bridges an inbound ASP.NET request to the in-game http_handler: the request's method, path
	/// (including query string), body, and headers go down to
	/// <see cref="SharpMUSH.Library.Services.Interfaces.IHttpHandlerCommandDispatcher"/>, and the
	/// handler-produced status line, content type, headers, and emitted output come back up as the
	/// HTTP response. Content-Length is computed here, never by softcode.
	/// </summary>
	private static async Task HandleMushHttpRequest(
		HttpContext context,
		SharpMUSH.Library.Services.Interfaces.IHttpHandlerCommandDispatcher dispatcher)
	{
		var request = context.Request;

		// %0 is the path as the MUSH sees it — strip the /http prefix, keep the query string.
		var path = $"/{context.GetRouteValue("path") as string}{request.QueryString.Value}";

		using var reader = new StreamReader(request.Body);
		var body = await reader.ReadToEndAsync(context.RequestAborted);

		var headers = request.Headers
			.SelectMany(header => header.Value.Where(value => value is not null)
				.Select(value => (header.Key, Value: value!)));

		var result = await dispatcher.DispatchAsync(request.Method, path, body, headers, context.RequestAborted);

		await result.Match(
			async handled =>
			{
				context.Response.StatusCode = handled.Status;
				var feature = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>();
				if (feature is not null)
				{
					feature.ReasonPhrase = handled.ReasonPhrase;
				}

				context.Response.ContentType = handled.ContentType;
				foreach (var (name, value) in handled.Headers)
				{
					// @respond already forbids Content-Length; defend anyway since the server computes it.
					if (!name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
					{
						context.Response.Headers.Append(name, value);
					}
				}

				await context.Response.WriteAsync(handled.Body, context.RequestAborted);
			},
			async _ =>
			{
				// No http_handler configured, or no <METHOD> attribute on it (see help sharphttp).
				context.Response.StatusCode = StatusCodes.Status404NotFound;
				await context.Response.WriteAsync("Not Found", context.RequestAborted);
			});
	}
}
