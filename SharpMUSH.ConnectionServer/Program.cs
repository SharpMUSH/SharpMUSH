using MassTransit;
using Microsoft.AspNetCore.Connections;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using SharpMUSH.ConnectionServer.Consumers;
using SharpMUSH.ConnectionServer.ProtocolHandlers;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messaging.Extensions;
using Testcontainers.Redpanda;
using Serilog;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

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


// Add ConnectionService
builder.Services.AddSingleton<IConnectionServerService, ConnectionServerService>();

// Add TelemetryService
builder.Services.AddSingleton<ITelemetryService, TelemetryService>();

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

// Map API endpoints
app.MapControllers();
app.MapGet("/", () => "SharpMUSH Connection Server");
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }));
app.MapGet("/ready", () => Results.Ok(new { status = "ready", timestamp = DateTimeOffset.UtcNow }));

// Prometheus metrics endpoint
app.MapPrometheusScrapingEndpoint();

app.Run();
