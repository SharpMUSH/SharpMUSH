using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>
/// Test that outputs telemetry summary.
/// This test should be run last to provide a summary of metrics collected during the test session.
/// </summary>
public class TelemetryOutputTests
{
	[ClassDataSource<TestClassFactory>(Shared = SharedType.PerClass)]
	public required TestClassFactory Factory { get; init; }

	[Test]
	public async Task OutputTelemetrySummary()
	{
		var prometheusService = Factory.Services.GetService<IPrometheusQueryService>();
		if (prometheusService == null)
		{
			Console.Error.WriteLine("PrometheusQueryService not available");
			return;
		}

		await TelemetryOutputHelper.OutputTelemetrySummaryAsync(prometheusService, Console.Out);
	}
}
