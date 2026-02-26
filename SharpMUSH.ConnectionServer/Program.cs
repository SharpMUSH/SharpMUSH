using Microsoft.AspNetCore.Connections;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.ResourceDetectors.Container;
using Serilog;
using SharpMUSH.ConnectionServer.Configuration;
using SharpMUSH.ConnectionServer.Consumers;
using SharpMUSH.ConnectionServer.ProtocolHandlers;
using SharpMUSH.ConnectionServer.Services;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.NATS;

namespace SharpMUSH.ConnectionServer;

public class Program
{
	public static async Task Main(string[] args)
	{
		var app = await CreateHostBuilderAsync(args);

		try
		{
			// Enable WebSocket support
			app.UseWebSockets();
			var webSocketHandler = app.Services.GetRequiredService<WebSocketServer>();
			app.Map("/ws", webSocketHandler.HandleWebSocketAsync);

			// Map API endpoints
			app.MapControllers();
			app.MapGet("/", () => "SharpMUSH Connection Server");
			app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }));
			app.MapGet("/ready", () => Results.Ok(new { status = "ready", timestamp = DateTimeOffset.UtcNow }));

			// Prometheus metrics endpoint (for scraping, not for logging to console)
			app.MapPrometheusScrapingEndpoint();

			await app.RunAsync();
		}
		finally
		{
			// NATS consumer service stops automatically via IHostedService lifecycle
		}
	}

	/// <summary>
	/// Creates and configures the WebApplication host.
	/// This method is used by WebApplicationFactory for testing.
	/// </summary>
	public static async Task<WebApplication> CreateHostBuilderAsync(string[] args)
	{
		var builder = WebApplication.CreateBuilder(args);

		// Configure ConnectionServer options
		var connectionServerOptions = new ConnectionServerOptions();
		builder.Configuration.GetSection("ConnectionServer").Bind(connectionServerOptions);
		builder.Services.AddSingleton(connectionServerOptions);

		builder.Services.AddLogging(logging =>
		{
			logging.ClearProviders();

			// Read Serilog configuration from appsettings.json (MinimumLevel, Overrides, WriteTo, Enrich)
			var loggerConfig = new LoggerConfiguration()
				.ReadFrom.Configuration(builder.Configuration);

			logging.AddSerilog(loggerConfig.CreateLogger());
		});

		// Get NATS URL from environment or default
		var natsUrl = Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://localhost:4222";

		// Add NATS-backed connection state store
		builder.Services.AddSingleton<IConnectionStateStore>(sp =>
		{
			var logger = sp.GetRequiredService<ILogger<NatsConnectionStateStore>>();
			return NatsConnectionStateStore.CreateAsync(natsUrl, logger).GetAwaiter().GetResult();
		});

		// Add ConnectionService
		builder.Services.AddSingleton<IConnectionServerService, ConnectionServerService>();

		// Add Output Transformation Service
		builder.Services.AddSingleton<IOutputTransformService, OutputTransformService>();

		// Add DescriptorGeneratorService
		builder.Services.AddSingleton<IDescriptorGeneratorService, DescriptorGeneratorService>();

		// Add TelemetryService
		builder.Services.AddSingleton<ITelemetryService, TelemetryService>();

		// Add WebSocketServer
		builder.Services.AddSingleton<WebSocketServer>();

		// Add health monitoring service
		builder.Services.AddHostedService<SharpMUSH.ConnectionServer.Services.HealthMonitoringService>();

		// Configure NATS messaging
		builder.Services.AddNatsConnectionServerMessaging(
			options =>
			{
				options.Url = natsUrl;
			},
			x =>
			{
				x.AddConsumer<TelnetOutputConsumer>();
				x.AddConsumer<TelnetPromptConsumer>();
				x.AddConsumer<BroadcastConsumer>();
				x.AddConsumer<DisconnectConnectionConsumer>();
				x.AddConsumer<GMCPOutputConsumer>();
				x.AddConsumer<UpdatePlayerPreferencesConsumer>();
				x.AddConsumer<WebSocketOutputConsumer>();
				x.AddConsumer<WebSocketPromptConsumer>();
			});

		// Configure Kestrel to listen for Telnet and WebSocket connections
		builder.WebHost.ConfigureKestrel((context, options) =>
		{
			options.AddServerHeader = true;

			// Listen for Telnet connections on configured port
			options.ListenAnyIP(connectionServerOptions.TelnetPort, listenOptions =>
			{
				listenOptions.UseConnectionHandler<TelnetServer>();
			});

			// HTTP API port (for WebSocket and HTTP endpoints)
			options.ListenAnyIP(connectionServerOptions.HttpPort);
		});

		// Add API controllers
		builder.Services.AddControllers();

		// Configure OpenTelemetry Metrics with GKE/Kubernetes-aware resource detection
		// Prometheus exporter is compatible with both GKE Managed Prometheus and standard Prometheus
		var isGKE = LoggingConfiguration.IsRunningInGKE();
		var isK8s = LoggingConfiguration.IsRunningInKubernetes();

		builder.Services.AddOpenTelemetry()
			.ConfigureResource(resource =>
			{
				resource.AddService(
					serviceName: "sharpmush-connectionserver",
					serviceVersion: "1.0.0",
					serviceInstanceId: Environment.MachineName);

				// Add container resource detection for Kubernetes environments
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

		return builder.Build();
	}

	/// <summary>
	/// Synchronous wrapper for CreateHostBuilderAsync for WebApplicationFactory compatibility.
	/// WebApplicationFactory traditionally expects a synchronous CreateHostBuilder method.
	/// </summary>
	public static WebApplication CreateHostBuilder(string[] args)
		=> CreateHostBuilderAsync(args).GetAwaiter().GetResult();
}
