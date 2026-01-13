using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>
/// Test that outputs telemetry summary.
/// This test should be run last to provide a summary of metrics collected during the test session.
/// </summary>
public class TelemetryOutputTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactory { get; init; }

	[Test]
	public async Task OutputTelemetrySummary()
	{
		var prometheusService = WebAppFactory.Services.GetService<IPrometheusQueryService>();
		if (prometheusService == null)
		{
			Console.Error.WriteLine("PrometheusQueryService not available");
			return;
		}

		await TelemetryOutputHelper.OutputTelemetrySummaryAsync(prometheusService, Console.Out);
	}
}
