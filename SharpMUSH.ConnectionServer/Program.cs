using KafkaFlow;
using Microsoft.AspNetCore.Connections;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using SharpMUSH.ConnectionServer.Configuration;
using SharpMUSH.ConnectionServer.Consumers;
using SharpMUSH.ConnectionServer.ProtocolHandlers;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.ConnectionServer.Strategy;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messages;
using SharpMUSH.Messaging.KafkaFlow;
using Testcontainers.Redpanda;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// Configure ConnectionServer options
var connectionServerOptions = new ConnectionServerOptions();
builder.Configuration.GetSection("ConnectionServer").Bind(connectionServerOptions);
builder.Services.AddSingleton(connectionServerOptions);

builder.Services.AddLogging(logging =>
{
	// Use standard logging configuration with JSON in K8s, plain text elsewhere
	var loggerConfig = LoggingConfiguration.CreateStandardConsoleConfiguration(
		minimumLevel: LogEventLevel.Information,
		overrides: LoggingConfiguration.CreateStandardOverrides()
	);
	
	logging.AddSerilog(loggerConfig.CreateLogger());
});

// Get Kafka/RedPanda configuration from environment or configuration
var kafkaHost = Environment.GetEnvironmentVariable("KAFKA_HOST");

RedpandaContainer? container = null;

if (kafkaHost == null)
{
	container = new RedpandaBuilder("docker.redpanda.com/redpandadata/redpanda:latest")
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

// Configure MassTransit with Kafka/RedPanda
builder.Services.AddConnectionServerMessaging(
	options =>
	{
		options.Host = kafkaHost;
		options.Port = 9092;
		options.MaxMessageBytes = 6 * 1024 * 1024; // 6MB
		options.ConsumerGroupId = "connectionserver-consumer-group"; // Separate group from main server
		options.BatchMaxSize = 100; 
		options.BatchTimeLimit = TimeSpan.FromMilliseconds(8); 
	},
	x =>
	{
		// Each consumer registration creates an INDEPENDENT KafkaFlow consumer with its own:
		// - Topic subscription
		// - Middleware pipeline  
		// - Worker configuration
		// - Buffer settings
		//
		// This means batch processing for TelnetOutputMessage does NOT affect other message types.
		
		// TelnetOutputMessage: Uses BATCH PROCESSING
		// - Middleware: TelnetOutputBatchMiddleware (IMessageMiddleware)
		// - Pipeline: Deserialize → AddBatching(100, 10ms) → TelnetOutputBatchMiddleware
		// - Groups messages by Handle, concatenates data IN ORDER, sends batched output
		// - Batch timeout: 10ms for good performance with low latency
		// - BytesSum distribution: Messages with same Handle go to same worker (ordering)
		// - Multiple workers (Environment.ProcessorCount): Different connections processed in parallel
		x.AddBatchConsumer<TelnetOutputBatchMiddleware, TelnetOutputMessage>(100, TimeSpan.FromMilliseconds(10));
		
		// All other messages: Use REGULAR CONSUMERS (individual message processing)
		// - Pipeline: Deserialize → TypedHandler (processes each message individually)
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

// Configure OpenTelemetry Metrics for Prometheus
builder.Services.AddOpenTelemetry()
	.ConfigureResource(resource => resource
		.AddService("SharpMUSH.ConnectionServer", serviceVersion: "1.0.0"))
	.WithMetrics(metrics => metrics
		.AddMeter("SharpMUSH")
		.AddRuntimeInstrumentation()
		.AddPrometheusExporter());

var app = builder.Build();

// Start Kafka bus
var bus = app.Services.CreateKafkaBus();
await bus.StartAsync();

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

	// Prometheus metrics endpoint
	app.MapPrometheusScrapingEndpoint();

	await app.RunAsync();
}
finally
{
	await bus.StopAsync();
	await redisStrategy.DisposeAsync();
}

// Required for WebApplicationFactory<Program> in tests
public partial class Program { }
