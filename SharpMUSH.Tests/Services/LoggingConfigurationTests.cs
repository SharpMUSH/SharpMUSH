using Serilog.Events;
using SharpMUSH.Library.Services;
using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace SharpMUSH.Tests.Services;

[NotInParallel]
public class LoggingConfigurationTests
{
	[Test]
	public async Task IsRunningInKubernetes_WithKubernetesServiceHost_ReturnsTrue()
	{
		// Arrange
		var originalK8sValue = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST");
		var originalDotnetValue = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
		try
		{
			// Set only KUBERNETES_SERVICE_HOST, clear DOTNET_RUNNING_IN_CONTAINER
			Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", null);
			Environment.SetEnvironmentVariable("KUBERNETES_SERVICE_HOST", "10.0.0.1");
			
			// Act
			var result = LoggingConfiguration.IsRunningInKubernetes();
			
			// Assert
			await Assert.That(result).IsTrue();
		}
		finally
		{
			Environment.SetEnvironmentVariable("KUBERNETES_SERVICE_HOST", originalK8sValue);
			Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", originalDotnetValue);
		}
	}
	
	[Test]
	public async Task IsRunningInKubernetes_WithDotnetRunningInContainer_ReturnsTrue()
	{
		// Arrange
		var originalDotnetValue = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
		var originalK8sValue = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST");
		try
		{
			// Clear K8s variable and set only DOTNET_RUNNING_IN_CONTAINER
			Environment.SetEnvironmentVariable("KUBERNETES_SERVICE_HOST", null);
			Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", "1");
			
			// Act
			var result = LoggingConfiguration.IsRunningInKubernetes();
			
			// Assert
			await Assert.That(result).IsTrue();
		}
		finally
		{
			Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", originalDotnetValue);
			Environment.SetEnvironmentVariable("KUBERNETES_SERVICE_HOST", originalK8sValue);
		}
	}
	
	[Test]
	public async Task IsRunningInKubernetes_WithoutK8sVariables_ReturnsFalse()
	{
		// Arrange
		var originalKubernetesValue = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST");
		var originalDotnetValue = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
		try
		{
			Environment.SetEnvironmentVariable("KUBERNETES_SERVICE_HOST", null);
			Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", null);
			
			// Act
			var result = LoggingConfiguration.IsRunningInKubernetes();
			
			// Assert
			await Assert.That(result).IsFalse();
		}
		finally
		{
			Environment.SetEnvironmentVariable("KUBERNETES_SERVICE_HOST", originalKubernetesValue);
			Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", originalDotnetValue);
		}
	}
	
	[Test]
	public async Task CreateStandardConsoleConfiguration_ReturnsValidConfiguration()
	{
		// Act
		var config = LoggingConfiguration.CreateStandardConsoleConfiguration();
		
		// Assert
		await Assert.That(config).IsNotNull();
		
		// Create the logger to verify it doesn't throw
		var logger = config.CreateLogger();
		await Assert.That(logger).IsNotNull();
	}
	
	[Test]
	public async Task CreateStandardConsoleConfiguration_WithCustomMinimumLevel_SetsLevel()
	{
		// Act
		var config = LoggingConfiguration.CreateStandardConsoleConfiguration(
			minimumLevel: LogEventLevel.Warning
		);
		
		// Assert
		await Assert.That(config).IsNotNull();
		var logger = config.CreateLogger();
		await Assert.That(logger).IsNotNull();
		await Assert.That(logger.IsEnabled(LogEventLevel.Information)).IsFalse();
		await Assert.That(logger.IsEnabled(LogEventLevel.Warning)).IsTrue();
	}
	
	[Test]
	public async Task CreateStandardOverrides_ReturnsExpectedOverrides()
	{
		// Act
		var overrides = LoggingConfiguration.CreateStandardOverrides();
		
		// Assert
		await Assert.That(overrides).IsNotNull();
		await Assert.That(overrides).ContainsKey("ZiggyCreatures.Caching.Fusion");
		await Assert.That(overrides["ZiggyCreatures.Caching.Fusion"]).IsEqualTo(LogEventLevel.Error);
	}
}
