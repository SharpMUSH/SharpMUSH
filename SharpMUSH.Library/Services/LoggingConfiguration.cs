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

		// KUBERNETES_SERVICE_HOST is set by Kubernetes
		// DOTNET_RUNNING_IN_CONTAINER is set to "1" by the .NET runtime when in a container
		return !string.IsNullOrEmpty(kubernetesServiceHost) ||
					 !string.IsNullOrEmpty(dotnetRunningInContainer);
	}

	/// <summary>
	/// Detects if the application is running in Google Kubernetes Engine (GKE).
	/// Checks for GCP-specific environment variables combined with Kubernetes indicators.
	/// </summary>
	public static bool IsRunningInGKE()
	{
		// First check environment variables for GCP project
		var projectId = GetGoogleCloudProjectId();
		if (!string.IsNullOrEmpty(projectId) && IsRunningInKubernetes())
		{
			return true;
		}

		// GKE also exposes project ID via GCE metadata server
		// Check for metadata server availability (indicates GCP environment)
		if (IsRunningInKubernetes())
		{
			try
			{
				var metadataFlavor = Environment.GetEnvironmentVariable("GCE_METADATA_HOST");
				// In GKE, the metadata server is always available at 169.254.169.254
				// We don't make an HTTP call here to avoid startup latency, 
				// but we can check if we're in a GCE-like environment
				if (!string.IsNullOrEmpty(metadataFlavor))
				{
					return true;
				}
			}
			catch
			{
				// Ignore - not in GKE
			}
		}

		return false;
	}

	/// <summary>
	/// Gets the Google Cloud Project ID if available from environment variables.
	/// </summary>
	public static string? GetGoogleCloudProjectId()
	{
		return Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT") ??
					 Environment.GetEnvironmentVariable("GCP_PROJECT") ??
					 Environment.GetEnvironmentVariable("GCLOUD_PROJECT");
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
			["ZiggyCreatures.Caching.Fusion"] = LogEventLevel.Error,
			["Microsoft.AspNetCore"] = LogEventLevel.Warning,
			["Microsoft.Hosting.Lifetime"] = LogEventLevel.Information,
			["TelnetNegotiationCore"] = LogEventLevel.Information
		};
	}
}
