using MassTransit;
using Microsoft.AspNetCore.Connections;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using SharpMUSH.ConnectionServer.Consumers;
using SharpMUSH.ConnectionServer.ProtocolHandlers;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.ConnectionServer.Strategy;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Extensions;
using Testcontainers.Redpanda;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLogging(logging => logging.AddSerilog(
	new LoggerConfiguration()
		.Enrich.FromLogContext()
		.WriteTo.Console()
		.CreateLogger()
));

// Get Kafka/RedPanda configuration from environment or configuration
var kafkaHost = Environment.GetEnvironmentVariable("KAFKA_HOST");

RedpandaContainer? container = null;

if (kafkaHost == null)
{
	container = new RedpandaBuilder()
		.WithPortBinding(9092, 9092)
		.Build();
	await container.StartAsync();
	
	kafkaHost = "localhost";
}

// Initialize Redis strategy
var redisStrategy = RedisStrategyProvider.GetStrategy();
await redisStrategy.InitializeAsync();

// Configure Redis connection using strategy pattern
builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(sp =>
{
	var logger = sp.GetRequiredService<ILogger<StackExchange.Redis.ConnectionMultiplexer>>();
	
	try
	{
		var multiplexer = redisStrategy.GetConnectionAsync().AsTask().GetAwaiter().GetResult();
		logger.LogInformation("Connected to Redis successfully");
		return multiplexer;
	}
	catch (Exception ex)
	{
		logger.LogWarning(ex, "Failed to connect to Redis. Connection state will not be shared.");
		throw;
	}
});

// Add Redis-backed connection state store
builder.Services.AddSingleton<IConnectionStateStore, RedisConnectionStateStore>();

// Add ConnectionService
builder.Services.AddSingleton<IConnectionServerService, ConnectionServerService>();

// Add TelemetryService
builder.Services.AddSingleton<ITelemetryService, TelemetryService>();

// Add WebSocketServer
builder.Services.AddSingleton<WebSocketServer>();

// Add batching service for @dolist performance optimization
builder.Services.AddSingleton<TelnetOutputBatchingService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TelnetOutputBatchingService>());

// Add health monitoring service
builder.Services.AddHostedService<SharpMUSH.ConnectionServer.Services.HealthMonitoringService>();

// Configure MassTransit with Kafka/RedPanda
builder.Services.AddConnectionServerMessaging(
	options =>
	{
		options.Host = kafkaHost!;
		options.Port = 9092;
		options.MaxMessageBytes = 6 * 1024 * 1024; // 6MB
		
		// Configure batching for @dolist performance optimization
		options.BatchMaxSize = 100; // Process up to 100 messages in a batch
		options.BatchTimeLimit = TimeSpan.FromMilliseconds(10); // Wait max 10ms for a full batch
	},
	x =>
	{
		// Register batch consumer for telnet output messages (solves @dolist performance issue)
		x.AddConsumer<BatchTelnetOutputConsumer>(c =>
		{
			c.Options<BatchOptions>(o => o
				.SetMessageLimit(100)
				.SetTimeLimit(TimeSpan.FromMilliseconds(10)));
		});
		
		// Register individual consumers for other message types
		x.AddConsumer<TelnetPromptConsumer>();
		x.AddConsumer<BroadcastConsumer>();
		x.AddConsumer<DisconnectConnectionConsumer>();
		
		// Register WebSocket consumers
		x.AddConsumer<WebSocketOutputConsumer>();
		x.AddConsumer<WebSocketPromptConsumer>();
	});

// TODO: This should be configurable via environment variables or config files
// Configure Kestrel to listen for Telnet connections
builder.WebHost.ConfigureKestrel((context, options) =>
{
	options.AddServerHeader = true;

	// Listen for Telnet connections on port 4201
	options.ListenAnyIP(4201, listenOptions =>
	{
		listenOptions.UseConnectionHandler<TelnetServer>();
	});

	// HTTP API port (4202)
	options.ListenAnyIP(4202);
});

// Add API controllers
builder.Services.AddControllers();

// Configure OpenTelemetry Metrics for Prometheus
builder.Services.AddOpenTelemetry()
	.ConfigureResource(resource => resource
		.AddService("SharpMUSH.ConnectionServer", serviceVersion: "1.0.0"))
	.WithMetrics(metrics => metrics
		.AddMeter("SharpMUSH")
		.AddRuntimeInstrumentation()
		.AddConsoleExporter()
		.AddPrometheusExporter());

var app = builder.Build();

try
{
	// Enable WebSocket support
	app.UseWebSockets();

	// Get WebSocketServer instance for the endpoint
	var webSocketHandler = app.Services.GetRequiredService<WebSocketServer>();

	// Map WebSocket endpoint
	app.Map("/ws", async context =>
	{
		await webSocketHandler.HandleWebSocketAsync(context);
	});

	// Map API endpoints
	app.MapControllers();
	app.MapGet("/", () => "SharpMUSH Connection Server");
	app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }));
	app.MapGet("/ready", () => Results.Ok(new { status = "ready", timestamp = DateTimeOffset.UtcNow }));

	// Prometheus metrics endpoint
	app.MapPrometheusScrapingEndpoint();

	await app.RunAsync();
}
finally
{
	await redisStrategy.DisposeAsync();
}
