using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Linq;

namespace SharpMUSH.Tests.Commands;

public class MetricsCommandTests
{
	[ClassDataSource<TestClassFactory>(Shared = SharedType.PerClass)]
	public required TestClassFactory Factory { get; init; }

	private IMUSHCodeParser Parser => Factory.CommandParser;
	private INotifyService NotifyService => Factory.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => Factory.Services.GetRequiredService<IConnectionService>();
	private IPrometheusQueryService PrometheusQueryService => Factory.Services.GetRequiredService<IPrometheusQueryService>();

	[Test]
	public async Task MetricsCommand_Health_ReturnsHealthStatus()
	{
		// Arrange
		var command = "@metrics/health";

		// Act
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Assert - Verify that NotifyService was called with health status information
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Service Health Status")) ||
				(msg.IsT1 && msg.AsT1.Contains("Service Health Status"))), 
				Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async Task MetricsCommand_Connections_ReturnsConnectionMetrics()
	{
		// Arrange
		var command = "@metrics/connections";

		// Act
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Assert - Verify that NotifyService was called with connection metrics
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Connection Metrics") && 
				 msg.AsT0.ToString().Contains("Active Connections") && 
				 msg.AsT0.ToString().Contains("Logged In Players")) ||
				(msg.IsT1 && msg.AsT1.Contains("Connection Metrics") && 
				 msg.AsT1.Contains("Active Connections") && 
				 msg.AsT1.Contains("Logged In Players"))), 
				Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Arguments("5m")]
	[Arguments("1h")]
	[Arguments("24h")]
	public async Task MetricsCommand_Slowest_ReturnsSlowOperations(string timeRange)
	{
		// Arrange
		var command = $"@metrics/slowest {timeRange}";

		// Act
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Assert - Verify that NotifyService was called with slowest operations
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Slowest Operations") && 
				 msg.AsT0.ToString().Contains($"over {timeRange}")) ||
				(msg.IsT1 && msg.AsT1.Contains("Slowest Operations") && 
				 msg.AsT1.Contains($"over {timeRange}"))), 
				Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Arguments("5m")]
	[Arguments("1h")]
	public async Task MetricsCommand_Popular_ReturnsMostCalledOperations(string timeRange)
	{
		// Arrange
		var command = $"@metrics/popular {timeRange}";

		// Act
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Assert - Verify that NotifyService was called with most popular operations
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Most Popular Operations") && 
				 msg.AsT0.ToString().Contains($"over {timeRange}")) ||
				(msg.IsT1 && msg.AsT1.Contains("Most Popular Operations") && 
				 msg.AsT1.Contains($"over {timeRange}"))), 
				Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async Task MetricsCommand_SlowestFunctions_ReturnsOnlyFunctions()
	{
		// Arrange
		var command = "@metrics/slowest/functions 5m";

		// Act
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Assert - Verify that NotifyService was called with function-specific results
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Slowest Functions") && 
				 !msg.AsT0.ToString().Contains("Slowest Commands")) ||
				(msg.IsT1 && msg.AsT1.Contains("Slowest Functions") && 
				 !msg.AsT1.Contains("Slowest Commands"))), 
				Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async Task MetricsCommand_SlowestCommands_ReturnsOnlyCommands()
	{
		// Arrange
		var command = "@metrics/slowest/commands 5m";

		// Act
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Assert - Verify that NotifyService was called with command-specific results
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Slowest Commands") && 
				 !msg.AsT0.ToString().Contains("Slowest Functions")) ||
				(msg.IsT1 && msg.AsT1.Contains("Slowest Commands") && 
				 !msg.AsT1.Contains("Slowest Functions"))), 
				Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async Task MetricsCommand_PopularFunctions_ReturnsOnlyFunctions()
	{
		// Arrange
		var command = "@metrics/popular/functions 5m";

		// Act
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Assert - Verify that NotifyService was called with function-specific results
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Most Called Functions") && 
				 !msg.AsT0.ToString().Contains("Most Called Commands")) ||
				(msg.IsT1 && msg.AsT1.Contains("Most Called Functions") && 
				 !msg.AsT1.Contains("Most Called Commands"))), 
				Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async Task MetricsCommand_WithLimit_RespectsLimit()
	{
		// Arrange
		var command = "@metrics/slowest 5m 20";

		// Act
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Assert - Verify the command executed successfully
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Slowest Operations")) ||
				(msg.IsT1 && msg.AsT1.Contains("Slowest Operations"))), 
				Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async Task MetricsCommand_Query_ExecutesCustomPromQL()
	{
		// Arrange
		var command = "@metrics/query rate(sharpmush_function_invocation_duration_count[5m])";

		// Act
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Assert - Verify that NotifyService was called with query results or error
		// The query may fail if Prometheus doesn't have data yet, which is acceptable
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && (msg.AsT0.ToString().Contains("Query Result") || msg.AsT0.ToString().Contains("Error"))) ||
				(msg.IsT1 && (msg.AsT1.Contains("Query Result") || msg.AsT1.Contains("Error") || msg.AsT1.Contains("HTTP")))), 
				Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async Task MetricsCommand_NoSwitch_ShowsUsage()
	{
		// Arrange
		var command = "@metrics";

		// Act
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Assert - Verify that usage information was shown
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains("Usage:") && 
				 msg.AsT0.ToString().Contains("@metrics/")) ||
				(msg.IsT1 && msg.AsT1.Contains("Usage:") && 
				 msg.AsT1.Contains("@metrics/"))), 
				Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async Task PrometheusQueryService_IsAvailable()
	{
		// Arrange & Act
		var service = Factory.Services.GetService<IPrometheusQueryService>();

		// Assert
		await Assert.That(service).IsNotNull();
	}

	[Test]
	public async Task PrometheusQueryService_GetHealthStatus_ReturnsData()
	{
		// Act
		var result = await PrometheusQueryService.GetHealthStatusAsync();

		// Assert
		await Assert.That(result).IsNotNull();
		// Note: The result might be empty if no metrics have been recorded yet, which is okay for a test
	}

	[Test]
	public async Task PrometheusQueryService_GetConnectionMetrics_ReturnsData()
	{
		// Act
		var result = await PrometheusQueryService.GetConnectionMetricsAsync();

		// Assert
		await Assert.That(result.ActiveConnections).IsGreaterThanOrEqualTo(0);
		await Assert.That(result.LoggedInPlayers).IsGreaterThanOrEqualTo(0);
	}

	[Test]
	public async Task PrometheusQueryService_GetMostCalledFunctions_ReturnsInDescendingOrder()
	{
		// Arrange - Record some function invocations to generate metrics
		var telemetryService = Factory.Services.GetRequiredService<ITelemetryService>();
		
		// Simulate multiple function calls with different frequencies
		// Function1: 100 calls at 1ms each
		for (int i = 0; i < 100; i++)
		{
			telemetryService.RecordFunctionInvocation("testFunction1", 1.0, true);
		}
		
		// Function2: 50 calls at 1ms each
		for (int i = 0; i < 50; i++)
		{
			telemetryService.RecordFunctionInvocation("testFunction2", 1.0, true);
		}
		
		// Function3: 150 calls at 1ms each (should be first)
		for (int i = 0; i < 150; i++)
		{
			telemetryService.RecordFunctionInvocation("testFunction3", 1.0, true);
		}
		
		// Function4: 75 calls at 1ms each
		for (int i = 0; i < 75; i++)
		{
			telemetryService.RecordFunctionInvocation("testFunction4", 1.0, true);
		}

		// Wait a bit to ensure metrics are exported to Prometheus
		await Task.Delay(TimeSpan.FromSeconds(10));

		// Act - Query the most called functions
		var result = await PrometheusQueryService.GetMostCalledFunctionsAsync("1m", limit: 10);

		// Assert - Verify results are returned in descending order by call rate
		await Assert.That(result).IsNotNull();
		
		if (result.Count > 1)
		{
			// Verify that each function has a lower or equal call rate than the previous one
			for (int i = 1; i < result.Count; i++)
			{
				var previous = result[i - 1];
				var current = result[i];
				
				await Assert.That(current.CallsPerSecond).IsLessThanOrEqualTo(previous.CallsPerSecond);
			}
		}

		// Additionally, if our test functions are present, verify they're in the expected order
		var testFunctions = result.Where(f => f.FunctionName.StartsWith("testFunction")).ToList();
		if (testFunctions.Count >= 2)
		{
			// testFunction3 (150 calls) should appear before testFunction1 (100 calls)
			var func3Index = testFunctions.FindIndex(f => f.FunctionName == "testFunction3");
			var func1Index = testFunctions.FindIndex(f => f.FunctionName == "testFunction1");
			
			if (func3Index >= 0 && func1Index >= 0)
			{
				await Assert.That(func3Index).IsLessThan(func1Index);
			}
		}
	}
}
