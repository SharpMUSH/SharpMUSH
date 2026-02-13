using Core.Arango;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server;
using SharpMUSH.Server.Strategy.ArangoDB;
using SharpMUSH.Server.Strategy.Prometheus;
using SharpMUSH.Server.Strategy.Redis;
using System.Linq;
using TUnit.AspNetCore;

namespace SharpMUSH.Tests;

public class ServerTestWebApplicationBuilderFactory<TProgram>(
	string sqlConnectionString,
	string configFile,
	INotifyService notifier,
	string prometheusUrl,
	string? databaseName = null,
	string sqlPlatform = "mysql") :
	TestWebApplicationFactory<TProgram> where TProgram : class
{
	/// <summary>
	/// Lifecycle Step 4: Runs BEFORE Program.cs startup.
	/// Use this for configuration that Program.cs needs during its initialization.
	/// </summary>
	protected override void ConfigureStartupConfiguration(IConfigurationBuilder configurationBuilder)
	{
		// Set Prometheus URL as environment variable so the PrometheusStrategyProvider will use ExternalStrategy
		// This runs before Program.cs, so if Program.cs reads environment variables, they'll be available
		Environment.SetEnvironmentVariable("PROMETHEUS_URL", prometheusUrl);
	}

	/// <summary>
	/// Lifecycle Step 3: Shared configuration for all tests (runs once per test session).
	/// Use ConfigureServices (NOT ConfigureTestServices) here for shared service registration.
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

		var colorFile = Path.Combine(AppContext.BaseDirectory, "colors.json");
		if (!File.Exists(colorFile))
		{
			var tempColorFile = Path.Combine(Path.GetTempPath(), "colors.json");
			File.WriteAllText(tempColorFile, "{}");
			try
			{
				Directory.CreateDirectory(AppContext.BaseDirectory);
				File.Copy(tempColorFile, colorFile, true);
			}
			catch
			{
				// If we can't create it in the base directory, that's OK
				// The startup will handle the missing file
			}
		}

		// IMPORTANT: Use ConfigureServices (not ConfigureTestServices) in ConfigureWebHost
		// This is shared configuration that runs once per test session (step 3)
		// ConfigureTestServices should be reserved for per-test overrides (step 7)
		builder.ConfigureServices(sc =>
			{
				var substitute = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
				var config = ReadPennMushConfig.Create(configFile);
				
				// Create IOptionsMonitor for SqlService with test connection string
				var sqlOptionsMonitor = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();
				
				// Override SQL configuration with test values by parsing connection string
				var sqlConfigOverride = config with
				{
					Net = config.Net with
					{
						SqlHost = ExtractSqlHost(sqlConnectionString),
						SqlDatabase = ExtractSqlDatabase(sqlConnectionString),
						SqlUsername = ExtractSqlUsername(sqlConnectionString),
						SqlPassword = ExtractSqlPassword(sqlConnectionString),
						SqlPlatform = sqlPlatform
					}
				};
				
				substitute.CurrentValue.Returns(config);
				sqlOptionsMonitor.CurrentValue.Returns(sqlConfigOverride);

				sc.RemoveAll<IOptionsWrapper<SharpMUSHOptions>>();
				sc.AddSingleton(substitute);

				sc.RemoveAll<INotifyService>();
				sc.AddSingleton(notifier);
				
				sc.RemoveAll<ISqlService>();
				sc.AddSingleton<ISqlService>(new SqlService(sqlOptionsMonitor));
				
				if (!string.IsNullOrEmpty(databaseName))
				{
					sc.RemoveAll<ArangoHandle>();
					sc.AddSingleton(new ArangoHandle(databaseName));
				}
			}
		);
	}
	
	private static string ExtractSqlHost(string connectionString)
	{
		var parts = connectionString.Split(';');
		string? host = null;
		string? port = null;
		
		foreach (var trimmedPart in parts.Select(part => part.Trim()))
		{
			if (trimmedPart.StartsWith("Server=", StringComparison.OrdinalIgnoreCase))
				host = trimmedPart.Substring(7);
			else if (trimmedPart.StartsWith("Host=", StringComparison.OrdinalIgnoreCase))
				host = trimmedPart.Substring(5);
			else if (trimmedPart.StartsWith("Port=", StringComparison.OrdinalIgnoreCase))
				port = trimmedPart.Substring(5);
		}
		
		// Combine host and port if both present
		if (!string.IsNullOrEmpty(host) && !string.IsNullOrEmpty(port))
			return $"{host}:{port}";
		
		return host ?? "localhost";
	}
	
	private static string ExtractSqlDatabase(string connectionString)
	{
		var parts = connectionString.Split(';');
		foreach (var trimmedPart in parts.Select(part => part.Trim()))
		{
			if (trimmedPart.StartsWith("Database=", StringComparison.OrdinalIgnoreCase))
				return trimmedPart.Substring(9);
			if (trimmedPart.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
				return trimmedPart.Substring(12);
		}
		return "";
	}
	
	private static string ExtractSqlUsername(string connectionString)
	{
		var parts = connectionString.Split(';');
		foreach (var trimmedPart in parts.Select(part => part.Trim()))
		{
			if (trimmedPart.StartsWith("Uid=", StringComparison.OrdinalIgnoreCase))
				return trimmedPart.Substring(4);
			if (trimmedPart.StartsWith("User Id=", StringComparison.OrdinalIgnoreCase))
				return trimmedPart.Substring(8);
			if (trimmedPart.StartsWith("Username=", StringComparison.OrdinalIgnoreCase))
				return trimmedPart.Substring(9);
			if (trimmedPart.StartsWith("User=", StringComparison.OrdinalIgnoreCase))
				return trimmedPart.Substring(5);
		}
		return "";
	}
	
	private static string ExtractSqlPassword(string connectionString)
	{
		var parts = connectionString.Split(';');
		foreach (var trimmedPart in parts.Select(part => part.Trim()))
		{
			if (trimmedPart.StartsWith("Pwd=", StringComparison.OrdinalIgnoreCase))
				return trimmedPart.Substring(4);
			if (trimmedPart.StartsWith("Password=", StringComparison.OrdinalIgnoreCase))
				return trimmedPart.Substring(9);
		}
		return "";
	}
}