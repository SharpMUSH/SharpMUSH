using Asp.Versioning;
using Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Mcp;
using SharpMUSH.Server.Middleware;
using SharpMUSH.Server.RateLimiting;
using SharpMUSH.Server.Services;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using SurrealDb.Net;
using SurrealDb.Embedded.InMemory;
using System.Globalization;
using System.Threading.RateLimiting;
using OpenTelemetry.ResourceDetectors.Container;
using Quartz;
using Serilog;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Database;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Database.SurrealDB;
using SharpMUSH.Implementation;
using SharpMUSH.Implementation.Commands;
using SharpMUSH.Implementation.Functions;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Behaviors;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.Interfaces;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
using SharpMUSH.Messaging.NATS;
using Microsoft.Extensions.Caching.Memory;
using ZiggyCreatures.Caching.Fusion;
using TaskScheduler = SharpMUSH.Library.Services.TaskScheduler;
namespace SharpMUSH.Server.Registration;

/// <summary>
/// The web front door: what is compressed, who may call across origins, whose forwarded headers are
/// believed, how a caller authenticates, and the API surface itself.
/// </summary>
internal static class WebHostRegistration
{
	/// <summary>Response compression, CORS, and trusted client-IP resolution behind a reverse proxy.</summary>
	public static IServiceCollection AddSharpMushHttpPipeline(
		this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
	{
		// Compress what we send. Only the Blazor _framework files arrived compressed before, because
		// UseBlazorFrameworkFiles serves pre-brotlied copies of those and nothing else was covered —
		// so a cold first visit pulled 15.0 MB, with Monaco's editor.api (3.67 MB), Mermaid (2.57 MB),
		// MudBlazor's CSS and mush-defs.json all going out as raw bytes.
		//
		// Fastest, not Optimal: this compresses on the fly, and the largest asset here is several
		// megabytes — paying maximum-ratio brotli per request would trade a download stall for a
		// server stall. Fastest still takes those files down by roughly an order of magnitude.
		// Responses that already carry a Content-Encoding (the pre-brotlied _framework files) are
		// skipped by the middleware, so nothing is compressed twice.
		services.AddResponseCompression(options =>
		{
			options.EnableForHttps = true;
			options.Providers.Add<BrotliCompressionProvider>();
			options.Providers.Add<GzipCompressionProvider>();
			options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
			[
				"application/javascript",
				"text/javascript",
				"application/json",
				"image/svg+xml",
				"application/manifest+json",
				"font/ttf",
				"application/wasm"
			]);
		});
		services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
		services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

		services.AddCors(options =>
		{
			// C-4: Read allowed origins from Cors:AllowedOrigins config array.
			// Falls back to dev-only wildcard (no AllowCredentials) when no origins are configured.
			// AllowCredentials() is required for SignalR WebSocket handshake, so it is only
			// enabled when specific origins are listed (or unconditionally in development, where
			// localhost is the only practical origin).
			var allowedOrigins = configuration
				.GetSection("Cors:AllowedOrigins")
				.Get<string[]>();

			options.AddDefaultPolicy(builder =>
			{
				if (allowedOrigins is { Length: > 0 })
				{
					builder.WithOrigins(allowedOrigins)
						.AllowAnyMethod()
						.AllowAnyHeader()
						.AllowCredentials();
				}
				else if (environment.IsDevelopment())
				{
					builder.SetIsOriginAllowed(_ => true)
						.AllowAnyMethod()
						.AllowAnyHeader()
						.AllowCredentials();
				}
				else
				{
					// Production with no origins configured: deny all cross-origin requests.
					builder.WithOrigins(Array.Empty<string>());
				}
			});
		});

		// Trusted client-IP resolution behind a reverse proxy (Caddy/Cloudflare in deploy/docker-compose.prod.yml):
		// the origin IP captured on account sessions (AuthController.ClientIp) and matched by sitelock host rules
		// must be the real client IP, not the proxy hop. The read happens INSIDE this delegate (not eagerly
		// here) so config sources appended after ConfigureServices runs — e.g. the test host's UseSetting
		// overrides — are still visible when options are first resolved (see the AddRateLimiter comment
		// further down for the same caveat).
		services.Configure<ForwardedHeadersOptions>(opts =>
		{
			opts.KnownIPNetworks.Clear();
			opts.KnownProxies.Clear();
			foreach (var proxy in configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
				if (System.Net.IPAddress.TryParse(proxy, out var ip))
					opts.KnownProxies.Add(ip);
			foreach (var network in configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
				if (System.Net.IPNetwork.TryParse(network, out var ipNetwork))
					opts.KnownIPNetworks.Add(ipNetwork);

			// IMPORTANT (verified by ForwardedHeadersTests): ForwardedHeadersMiddleware treats an EMPTY
			// KnownProxies/KnownIPNetworks pair as "nothing to check against" and trusts EVERY remote —
			// the opposite of the spoof-safe default this config is supposed to give. So "no proxies
			// configured" must disable forwarded-header processing entirely rather than lean on an empty
			// allow-list to mean "trust nobody".
			opts.ForwardedHeaders = opts.KnownProxies.Count > 0 || opts.KnownIPNetworks.Count > 0
				? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
				: ForwardedHeaders.None;
		});

		return services;
	}

	/// <summary>The authentication schemes, the in-server MCP surface, and role derivation.</summary>
	public static IServiceCollection AddSharpMushAuthentication(
		this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
	{
		// stays as the opt-in MCP scheme.
		var authBuilder = environment.IsDevelopment()
			? services.AddAuthentication(DebugAuthenticationHandler.SchemeName)
				.AddScheme<AuthenticationSchemeOptions, DebugAuthenticationHandler>(
					DebugAuthenticationHandler.SchemeName, _ => { })
			: services.AddAuthentication(AccountSessionAuthenticationHandler.SchemeName);

		authBuilder.AddScheme<AuthenticationSchemeOptions, AccountSessionAuthenticationHandler>(
			AccountSessionAuthenticationHandler.SchemeName, _ => { });

		services.AddAuthentication()
			.AddScheme<AuthenticationSchemeOptions, MushBasicAuthenticationHandler>(
				MushBasicAuthenticationHandler.SchemeName, _ => { });

		// In-server MCP (Model Context Protocol): exposes the shared MUSH code intelligence as
		// tools over Streamable HTTP. Services are always registered; the endpoint is only
		// mapped when Mcp:Enabled is true (see Program.MapMcp), so a disabled MCP returns 404.
		services.Configure<McpOptions>(configuration.GetSection(McpOptions.Section));
		services.AddSingleton<McpDocumentStore>();
		services.AddMcpServer()
			.WithHttpTransport(mcpTransport => mcpTransport.Stateless = true)
			.WithTools<MushTools>();

		services.AddSingleton<IRoleDerivationService, RoleDerivationService>();

		return services;
	}

	/// <summary>SignalR, API versioning, rate limiting, problem details, the authorization policy plumbing and controllers.</summary>
	public static IServiceCollection AddSharpMushWebApi(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddSignalR();

		// URL-segment strategy: /api/v1/... and /api/v2/...
		// Default version: 1.0 so existing unversioned controllers keep working.
		// Deprecated versions are announced via the api-deprecated-versions response header.
		services.AddApiVersioning(options =>
		{
			options.DefaultApiVersion = new ApiVersion(1, 0);
			options.AssumeDefaultVersionWhenUnspecified = true;
			options.ReportApiVersions = true;
			options.ApiVersionReader = ApiVersionReader.Combine(
				new UrlSegmentApiVersionReader(),
				new HeaderApiVersionReader("x-api-version"));
		}).AddMvc();

		// Named "public-api" policy: fixed window, 30 req/min per client IP,
		// queue depth 5.  Auth endpoints opt in via [EnableRateLimiting("public-api")].
		// Limits are configuration-driven (defaults below match the historical hardcoded
		// values) so the test host can raise them without touching production behavior.
		// NOTE: the config reads live INSIDE the AddRateLimiter delegate on purpose — the
		// delegate runs lazily at options resolution (after the host is fully built), so
		// configuration sources appended late (e.g. the test host's in-memory overrides)
		// are visible. An eager read at ConfigureServices time only ever sees the defaults
		// under WebApplicationFactory-style test hosts.
		services.AddRateLimiter(opts =>
		{
			opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
			opts.AddFixedWindowLimiter("public-api", limiterOpts =>
			{
				limiterOpts.PermitLimit = configuration.GetValue("RateLimiting:PublicApi:PermitLimit", 30);
				limiterOpts.Window = TimeSpan.FromSeconds(configuration.GetValue("RateLimiting:PublicApi:WindowSeconds", 60));
				limiterOpts.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
				limiterOpts.QueueLimit = configuration.GetValue("RateLimiting:PublicApi:QueueLimit", 5);
			});

			// "mcp" policy: partitioned per client IP so one source can't brute-force the
			// character+password auth on /mcp, while a legitimate agent (single IP) still gets
			// generous tool-call throughput. Partitioning (unlike the global "public-api" limiter)
			// keeps one caller's bursts from throttling everyone else.
			opts.AddPolicy("mcp", httpContext =>
				RateLimitPartition.GetFixedWindowLimiter(
					partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
					_ => new FixedWindowRateLimiterOptions
					{
						PermitLimit = 300,
						Window = TimeSpan.FromMinutes(1),
						QueueLimit = 0
					}));

			// "softcode-http" policy: PennMUSH's http_per_second quota on /http/{**path}, the only
			// surface that runs arbitrary softcode for an anonymous caller. Penn keeps ONE global
			// counter rather than a per-IP allowance (src/bsd.c http_quota), so the partition key is
			// the configured limit and not the client — every caller draws on the same budget.
			//
			// Keying on the limit is what makes @config/set http_per_second live: a changed setting
			// lands in a fresh partition sized to it, instead of leaving a bucket built for the old
			// one in place. The option is read per request (never captured), as the auth surfaces
			// read their sitelock rules.
			//
			// Zero or less is Penn's "HTTP is off" setting and is answered as an unconfigured
			// http_handler by HttpHandlerCommandService (404), not throttled to death here.
			opts.AddPolicy(Startup.SoftcodeHttpPolicy, httpContext =>
			{
				var perSecond = httpContext.RequestServices
					.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>()
					.CurrentValue.Database.HttpRequestsPerSecond;

				return perSecond < 1
					? RateLimitPartition.GetNoLimiter(0u)
					: RateLimitPartition.Get(perSecond, limit => new HttpQuotaRateLimiter(limit));
			});

			// Penn schedules the next attempt with http_msecs_till_next; the HTTP equivalent is to
			// tell the client when to come back. Scoped to this policy by endpoint, NOT by "does the
			// lease carry RetryAfter metadata" — a rejected FixedWindowRateLimiter lease carries it
			// too, so that test would have quietly added a Retry-After to the portal's "public-api"
			// and "mcp" 429s as well.
			opts.OnRejected = (context, _) =>
			{
				if (context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()
						is { PolicyName: Startup.SoftcodeHttpPolicy }
					&& context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
				{
					context.HttpContext.Response.Headers.RetryAfter =
						((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
				}

				return ValueTask.CompletedTask;
			};
		});

		services.AddProblemDetails();
		services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

		services.AddAuthorization();
		// Permission-policy plumbing: resolves [Authorize(Policy = PortalPermission.X)] gates against
		// the per-scope "perm" claims carried in the JWT.
		services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
		services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
		services.AddRazorPages();
		services.AddControllers();

		return services;
	}

	/// <summary>OpenTelemetry metrics with GKE/Kubernetes-aware resource detection, exported for Prometheus.</summary>
	public static IServiceCollection AddSharpMushObservability(this IServiceCollection services)
	{
		// Prometheus exporter is compatible with both GKE Managed Prometheus and standard Prometheus
		var isGKE = LoggingConfiguration.IsRunningInGKE();
		var isK8s = LoggingConfiguration.IsRunningInKubernetes();

		services.AddOpenTelemetry()
			.ConfigureResource(resource =>
			{
				resource.AddService(
					serviceName: "sharpmush-server",
					serviceVersion: "1.0.0",
					serviceInstanceId: Environment.MachineName);

				if (isK8s)
				{
					resource.AddDetector(new ContainerResourceDetector());
				}

				// Add GKE-specific attributes for Google Cloud Monitoring compatibility
				if (isGKE)
				{
					var projectId = LoggingConfiguration.GetGoogleCloudProjectId();
					if (!string.IsNullOrEmpty(projectId))
					{
						resource.AddAttributes(new[]
						{
							new KeyValuePair<string, object>("cloud.provider", "gcp"),
							new KeyValuePair<string, object>("cloud.platform", "gcp_kubernetes_engine"),
							new KeyValuePair<string, object>("gcp.project.id", projectId)
						});
					}
				}
			})
			.WithMetrics(metrics => metrics
				.AddMeter("SharpMUSH")
				.AddRuntimeInstrumentation()
				.AddAspNetCoreInstrumentation()
				.AddPrometheusExporter());

		return services;
	}
}
