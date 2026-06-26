using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using TUnit.AspNetCore;

namespace SharpMUSH.Tests;

/// <summary>
/// Test factory for SharpMUSH.ConnectionServer that configures the test environment.
/// </summary>
public class ConnectionServerTestWebApplicationBuilderFactory<TProgram>(
	string natsUrl) :
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
			.MinimumLevel.Verbose()
			.MinimumLevel.Override("SurrealDb", LogEventLevel.Error)
			.MinimumLevel.Override("NATS", LogEventLevel.Error);

		var enableConsoleLogging = Environment.GetEnvironmentVariable("SHARPMUSH_ENABLE_TEST_CONSOLE_LOGGING");
		var isConsoleEnabled = !string.IsNullOrEmpty(enableConsoleLogging) &&
													 (enableConsoleLogging.Equals("true", StringComparison.OrdinalIgnoreCase) || enableConsoleLogging == "1");

		if (isConsoleEnabled)
		{
			logConfig.WriteTo.Console(theme: AnsiConsoleTheme.Code);
		}

		var log = logConfig.CreateLogger();
		Log.Logger = log;

		Environment.SetEnvironmentVariable("NATS_URL", natsUrl);

		builder.ConfigureTestServices(sc =>
		{
		});
	}
}
