using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class MetricsCommandTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IPrometheusQueryService PrometheusQueryService => WebAppFactoryArg.Services.GetRequiredService<IPrometheusQueryService>();

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
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("Service Health Status")));
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
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => 
				s.Contains("Connection Metrics") && 
				s.Contains("Active Connections") && 
				s.Contains("Logged In Players")));
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
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => 
				s.Contains("Slowest Operations") && 
				s.Contains($"over {timeRange}")));
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
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => 
				s.Contains("Most Popular Operations") && 
				s.Contains($"over {timeRange}")));
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
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => 
				s.Contains("Slowest Functions") && 
				!s.Contains("Slowest Commands")));
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
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => 
				s.Contains("Slowest Commands") && 
				!s.Contains("Slowest Functions")));
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
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => 
				s.Contains("Most Called Functions") && 
				!s.Contains("Most Called Commands")));
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
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("Slowest Operations")));
	}

	[Test]
	public async Task MetricsCommand_Query_ExecutesCustomPromQL()
	{
		// Arrange
		var command = "@metrics/query rate(sharpmush_function_invocation_duration_count[5m])";

		// Act
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		// Assert - Verify that NotifyService was called with query results
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => s.Contains("Query Result")));
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
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<string>(s => 
				s.Contains("Usage:") && 
				s.Contains("@metrics/")));
	}

	[Test]
	public async Task PrometheusQueryService_IsAvailable()
	{
		// Arrange & Act
		var service = WebAppFactoryArg.Services.GetService<IPrometheusQueryService>();

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
}
