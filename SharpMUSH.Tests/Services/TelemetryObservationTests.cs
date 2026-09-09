using System.Diagnostics.Metrics;
using NSubstitute;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

[NotInParallel]
public class TelemetryObservationTests
{
	[Test]
	public async Task InvocationObserversPreserveExistingMetricExport()
	{
		var observer = Substitute.For<ITelemetryInvocationObserver>();
		var measurements = new List<(string Name, double Value)>();
		using var listener = new MeterListener();
		listener.InstrumentPublished = (instrument, owner) =>
		{
			if (instrument.Meter.Name == "SharpMUSH") owner.EnableMeasurementEvents(instrument);
		};
		listener.SetMeasurementEventCallback<double>((instrument, value, _, _) => measurements.Add((instrument.Name, value)));
		listener.Start();
		using var telemetry = new TelemetryService([observer]);
		telemetry.RecordFunctionInvocation("add", 2.5, true);
		telemetry.RecordCommandInvocation("@wait", 4, false);
		observer.Received(1).RecordInvocation(new(TelemetryInvocationKind.Function, "add", 2.5, true));
		observer.Received(1).RecordInvocation(new(TelemetryInvocationKind.Command, "@wait", 4, false));
		await Assert.That(measurements.Contains(("sharpmush.function.invocation.duration", 2.5))).IsTrue();
		await Assert.That(measurements.Contains(("sharpmush.command.invocation.duration", 4))).IsTrue();
	}

	[Test]
	public void BrokenObserverCannotInterruptInvocationOrOtherObservers()
	{
		var broken = Substitute.For<ITelemetryInvocationObserver>();
		broken.When(x => x.RecordInvocation(Arg.Any<TelemetryInvocation>())).Do(_ => throw new InvalidOperationException("private error"));
		var healthy = Substitute.For<ITelemetryInvocationObserver>();
		using var telemetry = new TelemetryService([broken, healthy]);
		telemetry.RecordFunctionInvocation("add", 1, true);
		healthy.Received(1).RecordInvocation(new(TelemetryInvocationKind.Function, "add", 1, true));
	}
}
