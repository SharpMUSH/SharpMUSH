using MassTransit;
using Microsoft.AspNetCore.Connections;
using SharpMUSH.ConnectionServer.Consumers;
using SharpMUSH.ConnectionServer.ProtocolHandlers;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messaging.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Get RabbitMQ configuration from environment or configuration
var rabbitHost = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";
var rabbitUser = Environment.GetEnvironmentVariable("RABBITMQ_USER") ?? "sharpmush";
var rabbitPass = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD") ?? "sharpmush_dev_password";

// Add ConnectionService
builder.Services.AddSingleton<IConnectionService, ConnectionService>();

// Configure MassTransit with RabbitMQ
builder.Services.AddMassTransit(x =>
{
	// Register consumers for output messages from MainProcess
	x.AddConsumer<TelnetOutputConsumer>();
	x.AddConsumer<TelnetPromptConsumer>();
	x.AddConsumer<BroadcastConsumer>();
	x.AddConsumer<DisconnectConnectionConsumer>();

	x.UsingRabbitMq((context, cfg) =>
	{
		cfg.Host(rabbitHost, "/", h =>
		{
			h.Username(rabbitUser);
			h.Password(rabbitPass);
		});

		// Configure message retry
		cfg.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));

		// Configure endpoints for consumers
		cfg.ConfigureEndpoints(context);
	});
});

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

app.Run();
