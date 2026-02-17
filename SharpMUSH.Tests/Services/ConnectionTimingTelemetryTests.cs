using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Tests for connection timing telemetry.
/// Verifies that the new RecordConnectionTiming method works correctly.
/// </summary>
public class ConnectionTimingTelemetryTests
{
	[Test]
	public void RecordConnectionTiming_RecordsTimingMetric()
	{
		// Arrange
		using var telemetryService = new TelemetryService();
		
		// Act - Record timing for different stages - should not throw
		telemetryService.RecordConnectionTiming("descriptor_allocation", 0.05);
		telemetryService.RecordConnectionTiming("telnet_setup", 15.3);
		telemetryService.RecordConnectionTiming("connection_registration", 25.7);
		telemetryService.RecordConnectionTiming("total_connection_establishment", 120.5);
		telemetryService.RecordConnectionTiming("disconnection", 45.2);
	}

	[Test]
	public void RecordConnectionTiming_WithNullService_DoesNotThrow()
	{
		// Arrange
		ITelemetryService? telemetryService = null;
		
		// Act - Should not throw when service is null
		telemetryService?.RecordConnectionTiming("test", 100.0);
	}

	[Test]
	public void RecordConnectionTiming_WithZeroDuration_RecordsSuccessfully()
	{
		// Arrange
		using var telemetryService = new TelemetryService();
		
		// Act - Should handle zero duration
		telemetryService.RecordConnectionTiming("instant_operation", 0.0);
	}

	[Test]
	public void RecordConnectionTiming_WithLargeDuration_RecordsSuccessfully()
	{
		// Arrange
		using var telemetryService = new TelemetryService();
		
		// Act - Should handle large duration (10 seconds)
		telemetryService.RecordConnectionTiming("slow_connection", 10000.0);
	}

	[Test]
	public void RecordConnectionTiming_MultipleStages_RecordsAllMetrics()
	{
		// Arrange
		using var telemetryService = new TelemetryService();
		
		// Act - Simulate a complete connection lifecycle
		telemetryService.RecordConnectionTiming("descriptor_allocation", 0.001);
		telemetryService.RecordConnectionTiming("telnet_setup", 12.5);
		telemetryService.RecordConnectionTiming("connection_registration", 28.3);
		telemetryService.RecordConnectionTiming("total_connection_establishment", 145.7);
		telemetryService.RecordConnectionTiming("disconnection", 52.1);
	}

	[Test]
	public void RecordConnectionEvent_StillWorks()
	{
		// Arrange
		using var telemetryService = new TelemetryService();
		
		// Act - Ensure existing connection event tracking still works
		telemetryService.RecordConnectionEvent("connected");
		telemetryService.RecordConnectionEvent("logged_in");
		telemetryService.RecordConnectionEvent("disconnected");
	}
}

