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

	// The own-time tests below time calls by counting clock readings instead of wall time: every reading
	// is one tick, so a call is charged a tick for each nested call that starts or ends while its clock
	// runs, and nothing for the ones that run while it is paused. Fifty calls inside an argument charge
	// the call they feed about fifty ticks when that argument is not paused, and none when it is.

	/// <summary>Time that moves one millisecond each time it is read.</summary>
	private sealed class CountedTime : TimeProvider
	{
		private long _now;
		public override long TimestampFrequency => 1000;
		public override long GetTimestamp() => Interlocked.Increment(ref _now);
	}

	private const int Calls = 50;

	/// <summary>The bound a call's own time stays under when the calls in its arguments are left out.</summary>
	private const double LeftOut = Calls / 5;

	private static string Repeated(string call) => string.Concat(Enumerable.Repeat(call, Calls));

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

	/// <summary>Runs <paramref name="run"/> on counted time and returns each measured call's own time.</summary>
	private static async Task<List<(string Name, double Ms)>> Measure(Func<Task> run)
	{
		var here = new AsyncLocal<bool>();
		var measured = new ConcurrentQueue<(string Name, double Ms)>();
		using var listener = ListenHere(here, measured);
		using var time = InvocationClock.UseTime(new CountedTime());

		here.Value = true;
		await run();
		here.Value = false;
		return measured.ToList();
	}

	private Task<List<(string Name, double Ms)>> MeasureFunction(string text) =>
		Measure(() => WebAppFactoryArg.CommandParser.FunctionParse(MarkupText.Plain(text)).AsTask());

	private Task<List<(string Name, double Ms)>> MeasureCommand(string command) =>
		Measure(() => WebAppFactoryArg.CommandParser.CommandParse(1,
			WebAppFactoryArg.Services.GetRequiredService<IConnectionService>(), MarkupText.Plain(command)).AsTask());

	[Test]
	public async Task AFunctionsTimeLeavesOutItsArguments()
	{
		var measured = await MeasureFunction($"[null({Repeated("[add(1,1)]")})]");

		await Assert.That(measured.Count(m => m.Name == "ADD")).IsEqualTo(Calls);
		await Assert.That(measured.Single(m => m.Name == "NULL").Ms).IsLessThan(LeftOut);
	}

	[Test]
	public async Task AFunctionsTimeLeavesOutTheArgumentsItEvaluatesItself()
	{
		// ITER evaluates its second argument once per item, pausing for each: a tick an item is its own.
		var measured = await MeasureFunction($"[iter(1 2,{Repeated("[add(1,1)]")})]");

		await Assert.That(measured.Count(m => m.Name == "ADD")).IsEqualTo(Calls * 2);
		await Assert.That(measured.Single(m => m.Name == "ITER").Ms).IsLessThan(LeftOut);
	}

	[Test]
	public async Task ACommandsTimeLeavesOutWhatItRuns()
	{
		var measured = await MeasureCommand(
			$"@ifelse 1={{think {TestIsolationHelpers.GenerateUniqueName("metrics")}{Repeated("[add(1,1)]")}}}");

		await Assert.That(measured.Count(m => m.Name == "THINK")).IsEqualTo(1);
		await Assert.That(measured.Count(m => m.Name == "ADD")).IsEqualTo(Calls);
		await Assert.That(measured.Single(m => m.Name == "@IFELSE").Ms).IsLessThan(LeftOut);
		await Assert.That(measured.Single(m => m.Name == "THINK").Ms).IsLessThan(LeftOut);
	}

	[Test]
	public async Task ACommandsTimeLeavesOutTheArgumentsItEvaluatesItself()
	{
		// @SWITCH's patterns are no-parse: the command evaluates each one as it compares it, and that
		// work is the pattern's.
		var measured = await MeasureCommand($"@switch 1={Repeated("[null(1)]")}1,think");

		await Assert.That(measured.Count(m => m.Name == "NULL")).IsEqualTo(Calls);
		await Assert.That(measured.Single(m => m.Name == "@SWITCH").Ms).IsLessThan(LeftOut);
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
