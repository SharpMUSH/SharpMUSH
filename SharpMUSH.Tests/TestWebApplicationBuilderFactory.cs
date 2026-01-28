using Core.Arango;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
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

namespace SharpMUSH.Tests;

public class TestWebApplicationBuilderFactory<TProgram>(
	string sqlConnectionString,
	string configFile,
	INotifyService notifier,
	string prometheusUrl,
	string? databaseName = null,
	string sqlPlatform = "mysql") :
	WebApplicationFactory<TProgram> where TProgram : class
{
	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		var log = new LoggerConfiguration()
			.Enrich.FromLogContext()
			.WriteTo.Console(theme: AnsiConsoleTheme.Code)
			.MinimumLevel.Debug()
			.CreateLogger();

		Log.Logger = log;

		// Set Prometheus URL as environment variable so the PrometheusStrategyProvider will use ExternalStrategy
		Environment.SetEnvironmentVariable("PROMETHEUS_URL", prometheusUrl);

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

		builder.ConfigureTestServices(sc =>
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
		foreach (var part in parts)
		{
			var trimmedPart = part.Trim();
			if (trimmedPart.StartsWith("Server=", StringComparison.OrdinalIgnoreCase))
				return trimmedPart.Substring(7);
			if (trimmedPart.StartsWith("Host=", StringComparison.OrdinalIgnoreCase))
				return trimmedPart.Substring(5);
		}
		return "localhost";
	}
	
	private static string ExtractSqlDatabase(string connectionString)
	{
		var parts = connectionString.Split(';');
		foreach (var part in parts)
		{
			var trimmedPart = part.Trim();
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
		foreach (var part in parts)
		{
			var trimmedPart = part.Trim();
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
		foreach (var part in parts)
		{
			var trimmedPart = part.Trim();
			if (trimmedPart.StartsWith("Pwd=", StringComparison.OrdinalIgnoreCase))
				return trimmedPart.Substring(4);
			if (trimmedPart.StartsWith("Password=", StringComparison.OrdinalIgnoreCase))
				return trimmedPart.Substring(9);
		}
		return "";
	}
}