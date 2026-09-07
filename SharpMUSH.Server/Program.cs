using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using SharpMUSH.Database;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Messaging.NATS.Strategy;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Mcp;
using SharpMUSH.Server.Middleware;

namespace SharpMUSH.Server;

public class Program
{
	/// <summary>
	/// Installs the markup layers this process can render and serialise. <see cref="MarkupText"/>
	/// resolves emitters and codecs through <see cref="MarkupRegistry.Default"/>, which throws until
	/// something sets it, so this has to run before the first render or deserialise.
	/// </summary>
	private static void ConfigureMarkup()
	{
		if (!MarkupRegistry.IsConfigured)
		{
			MarkupRegistry.Default = MarkupRegistry.Empty.WithAnsi().WithHtml();
		}
	}

	public static async Task Main(params string[] args)
	{
		ConfigureMarkup();

		var dbProviderStr = Environment.GetEnvironmentVariable("SHARPMUSH_DATABASE_PROVIDER");
		var databaseProvider = string.Equals(dbProviderStr, "surrealdb", StringComparison.OrdinalIgnoreCase)
			? DatabaseProvider.SurrealDB
			: DatabaseProvider.Lightning;

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
		var startup = new Startup(colorFile, natsUrl, databaseProvider);
		startup.ConfigureServices(builder.Services, builder.Configuration, builder.Environment);

		var app = builder.Build();

		// Migrate before anything is resolved that reads the database: the options factory reads
		// server data on first use, and the hosted services are constructed before their lifecycle
		// hooks run, so this is the one place that is both async and provably first. The provider's
		// factory only constructs.
		await app.Services.GetRequiredService<IDatabaseLifecycle>().Migrate();

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

		// MUST be first: rewrites HttpContext.Connection.RemoteIpAddress/Request.Scheme from
		// X-Forwarded-For/-Proto before anything downstream (routing, rate limiting, canonical-URL
		// redirects, session origin capture) reads them. Only trusts proxies listed in
		// "ForwardedHeaders:KnownProxies"/"KnownNetworks" (see Startup.ConfigureServices) — empty by
		// default, so no header is trusted until an operator explicitly configures the proxy hop.
		app.UseForwardedHeaders();

		// Before the static-file and Blazor-framework middleware, so it can compress what they serve.
		// The pre-brotlied _framework files already carry a Content-Encoding and are skipped.
		app.UseResponseCompression();

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

		// Serve the bundled Blazor WASM portal's framework files (_framework/*, blazor.boot.json,
		// compressed variants) with the correct content types, then its static assets. Paired with
		// MapFallbackToFile("index.html") below so the SPA is served from this server.
		app.UseBlazorFrameworkFiles();
		app.UseStaticFiles();

		app.UseMiddleware<BotDetectionMiddleware>();

		app.UseMiddleware<BotPrerenderMiddleware>();

		app.UseAuthentication();
		app.UseAuthorization();
		app.UseRateLimiter();
		app.MapControllers();
		app.MapRazorPages();
		app.MapHub<GameHub>("/hubs/game");

		// In-server MCP (Model Context Protocol) endpoint. Only mapped when Mcp:Enabled is
		// true; otherwise requests to the path fall through to the SPA fallback / 404. The
		// endpoint requires an authenticated game character via the MushBasic scheme
		// (Authorization: Basic base64(character:password)).
		var mcpOptions = app.Services.GetRequiredService<IOptions<McpOptions>>().Value;
		if (mcpOptions.Enabled)
		{
			var mcpPolicy = new AuthorizationPolicyBuilder(MushBasicAuthenticationHandler.SchemeName)
				.RequireAuthenticatedUser()
				.Build();
			// Rate-limit the endpoint: it does character+password Basic auth on every request, so
			// the per-IP "mcp" limiter blunts brute-force credential guessing without throttling a
			// legitimate agent's tool-call throughput.
			app.MapMcp(mcpOptions.Path)
				.RequireRateLimiting("mcp")
				.RequireAuthorization(mcpPolicy);
		}

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

		// Polled by the client's ServerStartupGate before it lets the app render: hosted services
		// (incl. BootstrapService/migrations) all complete before Kestrel accepts traffic, so mere
		// reachability of this endpoint is sufficient readiness. Dependency-free and unauthenticated
		// on purpose — it must answer even before the DB/bootstrap has finished, and it is polled
		// every few seconds so it carries no rate limit.
		app.MapGet("/api/health", () => Results.Ok(new { status = "ready" }));

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
