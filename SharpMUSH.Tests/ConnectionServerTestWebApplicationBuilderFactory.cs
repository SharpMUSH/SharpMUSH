using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using TUnit.AspNetCore;

namespace SharpMUSH.Tests;

/// <summary>
/// Test factory for SharpMUSH.ConnectionServer that configures the test environment.
/// </summary>
public class ConnectionServerTestWebApplicationBuilderFactory<TProgram>(
	string redisConnection,
	string kafkaHost) :
	TestWebApplicationFactory<TProgram> where TProgram : class
{
	/// <summary>
	/// Lifecycle Step 4: Runs BEFORE Program.cs startup.
	/// ConfigureWebHost is called BEFORE the application's ConfigureServices runs.
	/// This allows us to set up environment variables and override services before
	/// the Program.cs Configure method executes.
	/// </summary>
	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		var logConfig = new LoggerConfiguration()
			.Enrich.FromLogContext()
			.MinimumLevel.Debug();
		
		// Only write to console if explicitly enabled via environment variable
		var enableConsoleLogging = Environment.GetEnvironmentVariable("SHARPMUSH_ENABLE_TEST_CONSOLE_LOGGING");
		var isConsoleEnabled = !string.IsNullOrEmpty(enableConsoleLogging) && 
		                       (enableConsoleLogging.Equals("true", StringComparison.OrdinalIgnoreCase) || enableConsoleLogging == "1");
		
		if (isConsoleEnabled)
		{
			logConfig.WriteTo.Console(theme: AnsiConsoleTheme.Code);
		}
		
		var log = logConfig.CreateLogger();
		Log.Logger = log;

		// Set environment variables for ConnectionServer to use test infrastructure
		Environment.SetEnvironmentVariable("REDIS_CONNECTION", redisConnection);
		Environment.SetEnvironmentVariable("KAFKA_HOST", kafkaHost);

		builder.ConfigureTestServices(sc =>
		{
			// Additional service overrides for testing can go here
			// For example, you could substitute the IConnectionServerService
			// or other dependencies if needed for specific tests
		});
	}
}
