using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// A call refused for its argument count is a failed invocation, the same as one refused for an unknown
/// name or a permission: telemetry must record it with <c>success</c> false.
/// </summary>
public class FunctionArityTelemetryTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	[Test]
	public async Task ArityErrorIsRecordedAsUnsuccessful()
	{
		var outcomes = new ConcurrentQueue<bool>();
		using var listener = new MeterListener();
		listener.InstrumentPublished = (instrument, owner) =>
		{
			if (instrument.Meter.Name == "SharpMUSH" && instrument.Name == "sharpmush.function.invocation.duration")
				owner.EnableMeasurementEvents(instrument);
		};
		listener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
		{
			var isAbs = false;
			bool? success = null;
			foreach (var tag in tags)
			{
				if (tag.Key == "function.name" && string.Equals(tag.Value as string, "abs", StringComparison.OrdinalIgnoreCase))
					isAbs = true;
				if (tag.Key == "success" && tag.Value is bool value)
					success = value;
			}

			if (isAbs && success is { } outcome)
				outcomes.Enqueue(outcome);
		});
		listener.Start();

		var result = await WebAppFactoryArg.CommandParser.FunctionParse(MarkupText.Plain("abs(1,2)"));

		await Assert.That(result!.Message.ToPlainText()).StartsWith("#-1");
		await Assert.That(outcomes).Contains(false);
	}
}
