using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>
/// Test that outputs telemetry summary.
/// This test should be run last to provide a summary of metrics collected during the test session.
/// </summary>
public class TelemetryOutputTests : TestClassFactory
{
	[Test]
	public async Task OutputTelemetrySummary()
	{
		var prometheusService = Services.GetService<IPrometheusQueryService>();
		if (prometheusService == null)
		{
			Console.Error.WriteLine("PrometheusQueryService not available");
			return;
		}

		await TelemetryOutputHelper.OutputTelemetrySummaryAsync(prometheusService, Console.Out);
	}
}
