using Microsoft.AspNetCore.Connections;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.ResourceDetectors.Container;
using Serilog;
using SharpMUSH.SocketServer.Configuration;
using SharpMUSH.SocketServer.Consumers;
using SharpMUSH.SocketServer.ProtocolHandlers;
using SharpMUSH.SocketServer.Services;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.NATS;
using SharpMUSH.Messaging.NATS.Strategy;

namespace SharpMUSH.SocketServer;

public class Program
{
	public static async Task Main(string[] args)
	{
		var natsStrategy = NatsStrategyProvider.GetStrategy();
		var natsUrl = await natsStrategy.GetUrlAsync();

		var app = await CreateHostBuilderAsync(args, natsUrl);

		try
		{
			var metricsPort = app.Services.GetRequiredService<ConnectionServerOptions>().MetricsPort;
			app.Use((context, next) =>
			{
				if (metricsPort > 0 && context.Connection.LocalPort == metricsPort && context.Request.Path != "/metrics")
				{
					context.Response.StatusCode = StatusCodes.Status404NotFound;
					return Task.CompletedTask;
				}

				return next(context);
			});

			var webSocketOptions = new WebSocketOptions
			{
				KeepAliveInterval = TimeSpan.FromSeconds(30)
			};
			app.UseWebSockets(webSocketOptions);
			var webSocketHandler = app.Services.GetRequiredService<WebSocketServer>();
			app.Map("/ws", webSocketHandler.HandleWebSocketAsync);

			app.MapControllers();
			app.MapGet("/", () => "SharpMUSH Connection Server");
			app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }));
			app.MapGet("/ready", () => Results.Ok(new { status = "ready", timestamp = DateTimeOffset.UtcNow }));

			app.MapPrometheusScrapingEndpoint();

			var logger = app.Services.GetRequiredService<ILogger<Program>>();
			logger.LogTrace("[NATS] Connected to NATS host {NatsHost}", new Uri(natsUrl).Host);

			await app.RunAsync();
		}
		finally
		{
			await natsStrategy.DisposeAsync();
		}
	}

	/// <summary>
	/// Creates and configures the WebApplication host.
	/// This method is used by WebApplicationFactory for testing.
	/// </summary>
	/// <param name="args">Application arguments.</param>
	/// <param name="natsUrl">
	/// NATS URL to use.  When called from <see cref="Main"/> this is resolved via
	/// <see cref="NatsStrategyProvider"/> before this method is invoked.  When called
	/// from a test <c>WebApplicationFactory</c> (with no explicit URL) the value is read
	/// lazily from <c>NATS_URL</c> inside each DI registration lambda, which executes after
	/// the factory's <c>ConfigureWebHost</c> callback has set the environment variable.
	/// </param>
	public static async Task<WebApplication> CreateHostBuilderAsync(string[] args, string? natsUrl = null, Action<IServiceCollection>? configureServices = null)
	{
		var builder = WebApplication.CreateBuilder(args);

		var connectionServerOptions = new ConnectionServerOptions();
		builder.Configuration.GetSection("ConnectionServer").Bind(connectionServerOptions);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(connectionServerOptions.MaxConcurrentUpgradedConnections);
		builder.Services.AddSingleton(connectionServerOptions);

		builder.Services.AddLogging(logging =>
		{
			logging.ClearProviders();

			var loggerConfig = new LoggerConfiguration()
				.ReadFrom.Configuration(builder.Configuration);

			logging.AddSerilog(loggerConfig.CreateLogger());
		});

		// Add NATS-backed connection state store.
		// Resolve the URL lazily so that WebApplicationFactory's ConfigureWebHost (which sets
		// NATS_URL via Environment.SetEnvironmentVariable) takes effect before the service is built.
		builder.Services.AddSingleton<IConnectionStateStore>(sp =>
		{
			var url = natsUrl ?? Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://localhost:4222";
			var logger = sp.GetRequiredService<ILogger<NatsConnectionStateStore>>();
			return NatsConnectionStateStore.CreateAsync(url, logger).GetAwaiter().GetResult();
		});

		builder.Services.AddSingleton<IConnectionServerService, ConnectionServerService>();
		builder.Services.AddSingleton<EngineLifecycleNoticeService>();

		builder.Services.AddSingleton<RemoteOutputRenderer>();
		builder.Services.AddSingleton<IOutputTransformService>(sp => sp.GetRequiredService<RemoteOutputRenderer>());

		builder.Services.AddSingleton<IMarkupOutputRenderer>(sp => sp.GetRequiredService<RemoteOutputRenderer>());

		builder.Services.AddSingleton<IDescriptorGeneratorService>(sp =>
		{
			var url = natsUrl ?? Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://localhost:4222";
			return DurableDescriptorGeneratorService.CreateAsync(url).GetAwaiter().GetResult();
		});

		builder.Services.AddSingleton<ITelemetryService, TelemetryService>();

		// Terminal output sequencing + durable NATS-backed replay on reconnect is always on. Buffered
		// output + resume tokens survive a ConnectionServer restart / instance change; the retention
		// window is configurable (Replay:RetentionHours, default 24h). URL resolved lazily for the same
		// reason as the connection state store above.
		// Its byte budget (Replay:MaxBytes) and per-reconnect frame budget (Replay:MaxFrames) are set
		// apart from the bus's transport retention (SHARPMUSH_NATS_MAX_AGE / SHARPMUSH_NATS_MAX_BYTES).
		var replayOptions = ReplayOptions.FromConfiguration(builder.Configuration);
		var replayRetention = replayOptions.Retention;
		builder.Services.AddSingleton<ITerminalReplayStore>(sp =>
		{
			var url = natsUrl ?? Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://localhost:4222";
			return JetStreamTerminalReplayStore
				.CreateAsync(url, sp.GetRequiredService<ILogger<JetStreamTerminalReplayStore>>(), replayOptions)
				.GetAwaiter().GetResult();
		});
		builder.Services.AddSingleton<IResumeTokenStore>(sp =>
		{
			var url = natsUrl ?? Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://localhost:4222";
			return NatsKvResumeTokenStore
				.CreateAsync(url, sp.GetRequiredService<ILogger<NatsKvResumeTokenStore>>(), replayRetention)
				.GetAwaiter().GetResult();
		});
		// Detached-session pinning: hold a dropped session for a grace window and rebind on reconnect.
		var graceSeconds = builder.Configuration.GetValue("Session:GraceSeconds", 120.0);
		builder.Services.AddSingleton<SessionSinkRegistry>();
		builder.Services.AddSingleton<IGraceScheduler, TimerGraceScheduler>();
		builder.Services.AddSingleton<DetachedSessionTracker>();
		builder.Services.AddSingleton<SessionResumeAuthorizationService>();
		builder.Services.AddSingleton<ISessionResumeAuthorizationService>(sp => sp.GetRequiredService<SessionResumeAuthorizationService>());
		builder.Services.AddSingleton(sp => new ConnectionPump(
			sp.GetRequiredService<ILogger<ConnectionPump>>(),
			sp.GetRequiredService<IConnectionServerService>(),
			sp.GetRequiredService<SharpMUSH.Messaging.Abstractions.IMessageBus>(),
			sp.GetRequiredService<IDescriptorGeneratorService>(),
			sp.GetRequiredService<ITerminalReplayStore>(),
			sp.GetRequiredService<IResumeTokenStore>(),
			sp.GetRequiredService<SessionSinkRegistry>(),
			sp.GetRequiredService<DetachedSessionTracker>(),
			TimeSpan.FromSeconds(graceSeconds),
			sp.GetRequiredService<IConnectionStateStore>(),
			sp.GetRequiredService<ISessionResumeAuthorizationService>()));

		builder.Services.AddSingleton<WebSocketServer>();

		// Register the telnet interpreter factory (server mode) with the DI system.
		// This resolves the logger from DI automatically. Protocol plugins and per-connection
		// callbacks are configured in TelnetServer.OnConnectedAsync via CreateBuilder().
		builder.Services.AddTelnetServer();
		builder.Services.AddSingleton<MsspReportHolder>();
		builder.Services.AddHostedService<MsspReportRequestService>();

		builder.Services.AddHostedService<SharpMUSH.SocketServer.Services.HealthMonitoringService>();

		builder.Services.AddHostedService<SharpMUSH.SocketServer.Services.ConnectionCleanupService>();
		builder.Services.AddHostedService<ConnectionStateRefreshService>();

		// Configure NATS messaging (URL resolved lazily for the same reason as above)
		builder.Services.AddNatsConnectionServerMessaging(
			options =>
			{
				options.Url = natsUrl ?? Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://localhost:4222";
				options.MonitoredStreams.Add(JetStreamTerminalReplayStore.ReplayStreamName);
			},
			x =>
			{
				x.AddConsumer<TelnetOutputConsumer, TelnetOutputMessage>();
				x.AddConsumer<TelnetPromptConsumer, TelnetPromptMessage>();
				x.AddConsumer<MarkupOutputConsumer, MarkupOutputMessage>();
				x.AddConsumer<MarkupPromptConsumer, MarkupPromptMessage>();
				x.AddConsumer<BroadcastConsumer, BroadcastMessage>();
				x.AddConsumer<DisconnectConnectionConsumer, DisconnectConnectionMessage>();
				x.AddConsumer<GMCPOutputConsumer, GMCPOutputMessage>();
				x.AddConsumer<UpdatePlayerPreferencesConsumer, UpdatePlayerPreferencesMessage>();
				x.AddConsumer<UpdatePlayerPreferencesConsumer, ClearPlayerOutputPreferencesMessage>();
				x.AddConsumer<UpdatePlayerPreferencesConsumer, UpdateColorStyleMessage>();
				x.AddConsumer<WebSocketOutputConsumer, WebSocketOutputMessage>();
				x.AddConsumer<WebSocketPromptConsumer, WebSocketPromptMessage>();
				x.AddConsumer<MainProcessReadyConsumer, MainProcessReadyMessage>();
				x.AddConsumer<MSSPReportConsumer, MSSPReportMessage>();
				x.AddConsumer<SessionResumeResponseConsumer, SessionResumeResponseMessage>();
				x.AddConsumer<MainProcessShutdownConsumer, MainProcessShutdownMessage>();
			});

		var keepAlive = KeepAliveOptions.FromConfiguration(builder.Configuration);
		builder.Services.AddSingleton(keepAlive);

		builder.WebHost.ConfigureKestrel((context, options) =>
		{
			options.AddServerHeader = true;
			options.Limits.MaxConcurrentUpgradedConnections = connectionServerOptions.MaxConcurrentUpgradedConnections;

			options.ListenAnyIP(connectionServerOptions.TelnetPort, listenOptions =>
			{
				listenOptions.UseTcpKeepAlive(keepAlive.TcpUserTimeout);
				listenOptions.UseConnectionHandler<TelnetServer>();
			});

			// TLS telnet, when a port is configured. Kestrel terminates the handshake and attaches
			// ITlsHandshakeFeature, which is what TelnetServer reads to report ssl() and terminfo()'s
			// "ssl" token — so a connection is secure because a handshake happened on this endpoint,
			// not because of which port number it came in on. UseHttps() with no argument takes the
			// certificate from Kestrel:Certificates:Default and throws at startup if there is none,
			// which is why the port is opt-in: a misconfigured cert fails loudly rather than quietly
			// serving plaintext on a port players believe is encrypted.
			if (connectionServerOptions.TelnetSslPort > 0)
			{
				options.ListenAnyIP(connectionServerOptions.TelnetSslPort, listenOptions =>
				{
					listenOptions.UseTcpKeepAlive(keepAlive.TcpUserTimeout);
					listenOptions.UseHttps();
					listenOptions.UseConnectionHandler<TelnetServer>();
				});
			}

			options.ListenAnyIP(connectionServerOptions.HttpPort, listenOptions =>
			{
				listenOptions.UseTcpKeepAlive(keepAlive.TcpUserTimeout);
			});

			// /metrics only; see ConnectionServerOptions.MetricsPort.
			if (connectionServerOptions.MetricsPort > 0)
			{
				options.ListenAnyIP(connectionServerOptions.MetricsPort);
			}
		});

		builder.Services.AddControllers();

		var isGKE = LoggingConfiguration.IsRunningInGKE();
		var isK8s = LoggingConfiguration.IsRunningInKubernetes();

		builder.Services.AddOpenTelemetry()
			.ConfigureResource(resource =>
			{
				resource.AddService(
					serviceName: "sharpmush-socketserver",
					serviceVersion: "1.0.0",
					serviceInstanceId: Environment.MachineName);

				if (isK8s)
				{
					resource.AddDetector(new ContainerResourceDetector());
				}

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

		configureServices?.Invoke(builder.Services);
		return builder.Build();
	}

	/// <summary>
	/// Synchronous wrapper for CreateHostBuilderAsync for WebApplicationFactory compatibility.
	/// WebApplicationFactory traditionally expects a synchronous CreateHostBuilder method.
	/// </summary>
	public static WebApplication CreateHostBuilder(string[] args)
		=> CreateHostBuilderAsync(args).GetAwaiter().GetResult();
}
