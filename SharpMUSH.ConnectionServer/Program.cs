using MassTransit;
using Microsoft.AspNetCore.Connections;
using SharpMUSH.ConnectionServer.Consumers;
using SharpMUSH.ConnectionServer.ProtocolHandlers;
using SharpMUSH.ConnectionServer.Services;
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


// Add ConnectionService
builder.Services.AddSingleton<IConnectionServerService, ConnectionServerService>();

// Configure MassTransit with Kafka/RedPanda
builder.Services.AddConnectionServerMessaging(
	options =>
	{
		options.Host = kafkaHost!;
		options.Port = 9092;
		options.MaxMessageBytes = 6 * 1024 * 1024; // 6MB
	},
	x =>
	{
		// Register consumers for output messages from MainProcess
		x.AddConsumer<TelnetOutputConsumer>();
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

var app = builder.Build();

// Map API endpoints
app.MapControllers();
app.MapGet("/", () => "SharpMUSH Connection Server");
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }));
app.MapGet("/ready", () => Results.Ok(new { status = "ready", timestamp = DateTimeOffset.UtcNow }));

app.Run();
