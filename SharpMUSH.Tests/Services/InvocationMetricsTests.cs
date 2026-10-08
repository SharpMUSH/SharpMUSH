using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Function and command timings reach Prometheus as histograms whose buckets resolve sub-millisecond
/// calls, labelled only with registered names so a player cannot add series by typing.
/// </summary>
public class InvocationMetricsTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private static MeterListener Listen(ConcurrentQueue<(string Instrument, string Name)> names)
	{
		var listener = new MeterListener
		{
			InstrumentPublished = (instrument, owner) =>
			{
				if (instrument.Meter.Name == "SharpMUSH" && instrument.Name.EndsWith(".invocation.duration", StringComparison.Ordinal))
					owner.EnableMeasurementEvents(instrument);
			}
		};
		listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
		{
			foreach (var tag in tags)
			{
				if (tag.Key is "function.name" or "command.name" && tag.Value is string name)
					names.Enqueue((instrument.Name, name));
			}
		});
		listener.Start();
		return listener;
	}

	[Test]
	public async Task FunctionsAreLabelledWithTheirRegisteredName()
	{
		var names = new ConcurrentQueue<(string Instrument, string Name)>();
		using var listener = Listen(names);
		var unknown = "nosuchfn" + Guid.NewGuid().ToString("N");
		var prose = "prosefn" + Guid.NewGuid().ToString("N");

		await WebAppFactoryArg.CommandParser.FunctionParse(MarkupText.Plain("[exp(0)][Reverse(ab)]"));
		await WebAppFactoryArg.CommandParser.FunctionParse(MarkupText.Plain($"[{unknown}()]"));
		await WebAppFactoryArg.CommandParser.FunctionParse(MarkupText.Plain($"{prose}(x)"));

		var functions = names.Where(n => n.Instrument == "sharpmush.function.invocation.duration").Select(n => n.Name).ToList();
		await Assert.That(functions).Contains("E");
		await Assert.That(functions).Contains("FLIP");
		await Assert.That(functions).Contains(TelemetryService.UnknownFunction);
		await Assert.That(functions).DoesNotContain(n => n.Contains(unknown, StringComparison.OrdinalIgnoreCase));
		await Assert.That(functions).DoesNotContain(n => n.Contains(prose, StringComparison.OrdinalIgnoreCase));
	}

	[Test]
	public async Task CommandsAreLabelledWithTheirRegisteredName()
	{
		var names = new ConcurrentQueue<(string Instrument, string Name)>();
		using var listener = Listen(names);

		await WebAppFactoryArg.CommandParser.CommandParse(1,
			WebAppFactoryArg.Services.GetRequiredService<IConnectionService>(),
			MarkupText.Plain("think " + TestIsolationHelpers.GenerateUniqueName("metrics")));

		var commands = names.Where(n => n.Instrument == "sharpmush.command.invocation.duration").Select(n => n.Name).ToList();
		await Assert.That(commands).Contains("THINK");
		await Assert.That(commands).DoesNotContain("think");
	}

	[Test]
	public async Task DurationsAdviseSubMillisecondBuckets()
	{
		var advised = new ConcurrentDictionary<string, IReadOnlyList<double>?>();
		using var listener = new MeterListener
		{
			InstrumentPublished = (instrument, _) =>
			{
				if (instrument.Meter.Name == "SharpMUSH" && instrument is Histogram<double> histogram)
					advised[instrument.Name] = histogram.Advice?.HistogramBucketBoundaries;
			}
		};
		using var telemetry = new TelemetryService();
		listener.Start();

		await Assert.That(advised["sharpmush.function.invocation.duration"]).IsEquivalentTo(TelemetryService.DurationBucketsMs);
		await Assert.That(advised["sharpmush.command.invocation.duration"]).IsEquivalentTo(TelemetryService.DurationBucketsMs);
		await Assert.That(TelemetryService.DurationBucketsMs[0]).IsLessThan(0.1);
	}
}
