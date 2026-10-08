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

	/// <summary>Measurements taken in this test's own flow, so parallel tests' calls stay out.</summary>
	private static MeterListener ListenHere(AsyncLocal<bool> here, ConcurrentQueue<(string Name, double Ms)> measured)
	{
		var listener = new MeterListener
		{
			InstrumentPublished = (instrument, owner) =>
			{
				if (instrument.Meter.Name == "SharpMUSH" && instrument.Name.EndsWith(".invocation.duration", StringComparison.Ordinal))
					owner.EnableMeasurementEvents(instrument);
			}
		};
		listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
		{
			if (!here.Value) return;
			foreach (var tag in tags)
			{
				if (tag.Key is "function.name" or "command.name" && tag.Value is string name)
					measured.Enqueue((name, value));
			}
		});
		listener.Start();
		return listener;
	}

	[Test]
	public async Task AFunctionsTimeLeavesOutItsArguments()
	{
		var here = new AsyncLocal<bool>();
		var measured = new ConcurrentQueue<(string Name, double Ms)>();
		using var listener = ListenHere(here, measured);

		here.Value = true;
		await WebAppFactoryArg.CommandParser.FunctionParse(MarkupText.Plain("[null(iter(lnum(3000),add(##,1)))]"));
		here.Value = false;

		var nullMs = measured.Single(m => m.Name == "NULL").Ms;
		var nestedMs = measured.Where(m => m.Name != "NULL").Sum(m => m.Ms);
		await Assert.That(measured.Count(m => m.Name == "ADD")).IsEqualTo(3000);
		await Assert.That(nullMs).IsLessThan(nestedMs / 4);
	}

	[Test]
	public async Task ACommandsTimeLeavesOutWhatItRuns()
	{
		var here = new AsyncLocal<bool>();
		var measured = new ConcurrentQueue<(string Name, double Ms)>();
		using var listener = ListenHere(here, measured);

		here.Value = true;
		await WebAppFactoryArg.CommandParser.CommandParse(1,
			WebAppFactoryArg.Services.GetRequiredService<IConnectionService>(),
			MarkupText.Plain($"@ifelse 1={{think {TestIsolationHelpers.GenerateUniqueName("metrics")}[null(iter(lnum(3000),add(##,1)))]}}"));
		here.Value = false;

		// @IFELSE runs THINK in place, and THINK evaluates the functions.
		var ifElseMs = measured.Single(m => m.Name == "@IFELSE").Ms;
		var nestedMs = measured.Where(m => m.Name != "@IFELSE").Sum(m => m.Ms);
		await Assert.That(measured.Count(m => m.Name == "THINK")).IsEqualTo(1);
		await Assert.That(measured.Count(m => m.Name == "ADD")).IsEqualTo(3000);
		await Assert.That(ifElseMs).IsLessThan(nestedMs / 4);
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
