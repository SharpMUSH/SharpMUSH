using Core.Arango;
using Core.Arango.Serilog;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Sinks.PeriodicBatching;
using SharpMUSH.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Messaging.NATS.Strategy;
using SharpMUSH.Server.Strategy.ArangoDB;

namespace SharpMUSH.Server;

public class Program
{
public static async Task Main(params string[] args)
{
// Determine database provider from environment variable
var dbProviderStr = Environment.GetEnvironmentVariable("SHARPMUSH_DATABASE_PROVIDER");
var databaseProvider = string.Equals(dbProviderStr, "memgraph", StringComparison.OrdinalIgnoreCase)
? DatabaseProvider.Memgraph
: DatabaseProvider.ArangoDB;

ArangoConfiguration? arangoConfig = null;
string? memgraphUri = null;

if (databaseProvider == DatabaseProvider.Memgraph)
{
memgraphUri = Environment.GetEnvironmentVariable("MEMGRAPH_URI") ?? "bolt://localhost:7687";
}
else
{
arangoConfig = await ArangoStartupStrategyProvider.GetStrategy().ConfigureArango();
}

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
startup.ConfigureServices(builder.Services, builder.Configuration);

var app = builder.Build();

// Configure Arango database logging sink only when using ArangoDB
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

// Get logger for startup logging
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

if (env.EnvironmentName == "Development")
{
app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.MapRazorPages();

// Health and readiness endpoints for deployment checks
app.MapGet("/health", () => "healthy");
app.MapGet("/ready", () => "ready");

// Prometheus metrics endpoint (for scraping, not for logging to console)
app.MapPrometheusScrapingEndpoint();

return app;
}
}
