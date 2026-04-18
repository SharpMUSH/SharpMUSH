using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Diagnostics;

namespace SharpMUSH.Tests.Performance;

/// <summary>
/// Observational throughput diagnostics for the MUSH parser hot paths.
/// Reports ops/sec and P50/P95/P99 latency percentiles to the console.
/// These tests are always-run (not Explicit) to provide warm-path diagnostics in CI output,
/// but contain no assertions — they are informational only.
/// </summary>
public class ParserThroughputTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactory { get; init; }

	private IMUSHCodeParser Parser => WebAppFactory.FunctionParser;
	private IMUSHCodeParser CommandParser => WebAppFactory.CommandParser;

	private static readonly MString SimpleAdd = MModule.single("[add(1,1)]");
	private static readonly MString NestedAdd10 = MModule.single(BuildNestedAdd(10));
	private static readonly MString NestedAdd50 = MModule.single(BuildNestedAdd(50));
	private static readonly MString Lnum100 = MModule.single("lnum(100)");
	private static readonly MString Iter100 = MModule.single("iter(lnum(100),%i0)");
	private static readonly MString ThinkCmd = MModule.single("think hello");
	private static readonly MString ThinkSubst = MModule.single("think %#");

	private static string BuildNestedAdd(int depth)
	{
		var inner = "1";
		for (var i = 0; i < depth; i++)
			inner = $"add(1,{inner})";
		return $"[{inner}]";
	}

	[Test]
	public async Task ReportFunctionParseBaseline()
	{
		const int iterations = 200;
		const int warmup = 20;

		// Warm up
		for (var i = 0; i < warmup; i++)
			await Parser.FunctionParse(SimpleAdd);

		var timings = new long[iterations];
		for (var i = 0; i < iterations; i++)
		{
			var sw = Stopwatch.StartNew();
			await Parser.FunctionParse(SimpleAdd);
			timings[i] = sw.ElapsedTicks;
		}

		PrintReport("FunctionParse: [add(1,1)]", timings, iterations);
	}

	[Test]
	public async Task ReportNestedFunctionParse()
	{
		const int iterations = 200;
		const int warmup = 20;

		for (var i = 0; i < warmup; i++)
			await Parser.FunctionParse(NestedAdd10);

		var timings10 = new long[iterations];
		for (var i = 0; i < iterations; i++)
		{
			var sw = Stopwatch.StartNew();
			await Parser.FunctionParse(NestedAdd10);
			timings10[i] = sw.ElapsedTicks;
		}

		for (var i = 0; i < warmup; i++)
			await Parser.FunctionParse(NestedAdd50);

		var timings50 = new long[iterations];
		for (var i = 0; i < iterations; i++)
		{
			var sw = Stopwatch.StartNew();
			await Parser.FunctionParse(NestedAdd50);
			timings50[i] = sw.ElapsedTicks;
		}

		PrintReport("FunctionParse: nested add depth=10", timings10, iterations);
		PrintReport("FunctionParse: nested add depth=50", timings50, iterations);
	}

	[Test]
	public async Task ReportListFunctionThroughput()
	{
		const int iterations = 100;
		const int warmup = 10;

		for (var i = 0; i < warmup; i++)
			await Parser.FunctionParse(Lnum100);

		var lnumTimings = new long[iterations];
		for (var i = 0; i < iterations; i++)
		{
			var sw = Stopwatch.StartNew();
			await Parser.FunctionParse(Lnum100);
			lnumTimings[i] = sw.ElapsedTicks;
		}

		for (var i = 0; i < warmup; i++)
			await Parser.FunctionParse(Iter100);

		var iterTimings = new long[iterations];
		for (var i = 0; i < iterations; i++)
		{
			var sw = Stopwatch.StartNew();
			await Parser.FunctionParse(Iter100);
			iterTimings[i] = sw.ElapsedTicks;
		}

		PrintReport("FunctionParse: lnum(100)", lnumTimings, iterations);
		PrintReport("FunctionParse: iter(lnum(100),%i0)", iterTimings, iterations);
	}

	[Test]
	public async Task ReportCommandParseThroughput()
	{
		const int iterations = 200;
		const int warmup = 20;

		var connectionService = WebAppFactory.Services.GetRequiredService<IConnectionService>();

		for (var i = 0; i < warmup; i++)
			await CommandParser.CommandParse(1, connectionService, ThinkCmd);

		var thinkTimings = new long[iterations];
		for (var i = 0; i < iterations; i++)
		{
			var sw = Stopwatch.StartNew();
			await CommandParser.CommandParse(1, connectionService, ThinkCmd);
			thinkTimings[i] = sw.ElapsedTicks;
		}

		for (var i = 0; i < warmup; i++)
			await CommandParser.CommandParse(1, connectionService, ThinkSubst);

		var substTimings = new long[iterations];
		for (var i = 0; i < iterations; i++)
		{
			var sw = Stopwatch.StartNew();
			await CommandParser.CommandParse(1, connectionService, ThinkSubst);
			substTimings[i] = sw.ElapsedTicks;
		}

		PrintReport("CommandParse: think hello", thinkTimings, iterations);
		PrintReport("CommandParse: think %#", substTimings, iterations);
	}

	private static void PrintReport(string label, long[] timings, int iterations)
	{
		Array.Sort(timings);
		var ticksPerMs = (double)Stopwatch.Frequency / 1000.0;

		var meanMs = timings.Average() / ticksPerMs;
		var p50Ms = timings[iterations / 2] / ticksPerMs;
		var p95Ms = timings[(int)(iterations * 0.95)] / ticksPerMs;
		var p99Ms = timings[(int)(iterations * 0.99)] / ticksPerMs;
		var opsPerSec = 1000.0 / meanMs;

		Console.WriteLine($"\n[{label}]");
		Console.WriteLine($"  Iterations : {iterations}");
		Console.WriteLine($"  Mean       : {meanMs,8:F3} ms  ({opsPerSec:F0} ops/sec)");
		Console.WriteLine($"  P50        : {p50Ms,8:F3} ms");
		Console.WriteLine($"  P95        : {p95Ms,8:F3} ms");
		Console.WriteLine($"  P99        : {p99Ms,8:F3} ms");
	}
}
