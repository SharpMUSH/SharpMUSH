using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Provides centralized logging configuration for SharpMUSH applications.
/// Supports both Kubernetes (JSON) and local/test (plain text) environments.
/// </summary>
public static class LoggingConfiguration
{
	/// <summary>
	/// Detects if the application is running in a Kubernetes environment.
	/// Checks for common Kubernetes environment indicators.
	/// </summary>
	public static bool IsRunningInKubernetes()
	{
		// Check for Kubernetes-specific environment variables
		var kubernetesServiceHost = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST");
		var dotnetRunningInContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
		
		return !string.IsNullOrEmpty(kubernetesServiceHost) || 
		       dotnetRunningInContainer?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
	}

	/// <summary>
	/// Creates a standard Serilog logger configuration for console output.
	/// Uses JSON formatting in Kubernetes environments and plain text elsewhere.
	/// </summary>
	/// <param name="minimumLevel">The minimum log level. Defaults to Information.</param>
	/// <param name="overrides">Optional log level overrides for specific namespaces.</param>
	/// <returns>A configured LoggerConfiguration ready for additional sinks or to be created.</returns>
	public static LoggerConfiguration CreateStandardConsoleConfiguration(
		LogEventLevel minimumLevel = LogEventLevel.Information,
		Dictionary<string, LogEventLevel>? overrides = null)
	{
		var config = new LoggerConfiguration()
			.MinimumLevel.Is(minimumLevel)
			.Enrich.FromLogContext();

		// Apply any namespace-specific overrides
		if (overrides != null)
		{
			foreach (var (ns, level) in overrides)
			{
				config.MinimumLevel.Override(ns, level);
			}
		}

		// Use JSON formatting in Kubernetes, plain text elsewhere
		if (IsRunningInKubernetes())
		{
			// CompactJsonFormatter is the standard for Kubernetes log aggregation
			config.WriteTo.Console(new CompactJsonFormatter());
		}
		else
		{
			// Plain text for local development and testing
			config.WriteTo.Console();
		}

		return config;
	}

	/// <summary>
	/// Creates a standard set of log level overrides commonly used in SharpMUSH.
	/// </summary>
	/// <returns>A dictionary of namespace to log level overrides.</returns>
	public static Dictionary<string, LogEventLevel> CreateStandardOverrides()
	{
		return new Dictionary<string, LogEventLevel>
		{
			// Reduce noise from caching library
			["ZiggyCreatures.Caching.Fusion"] = LogEventLevel.Error,
			// Reduce noise from ASP.NET Core
			["Microsoft.AspNetCore"] = LogEventLevel.Warning,
			["Microsoft.Hosting.Lifetime"] = LogEventLevel.Information
		};
	}
}
