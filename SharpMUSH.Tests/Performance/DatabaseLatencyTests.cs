using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using System.Diagnostics;

namespace SharpMUSH.Tests.Performance;

/// <summary>
/// Observational latency diagnostics for the database layer.
/// Reports mean and P50/P95/P99 latency percentiles per operation to the console.
/// Always runs (not Explicit) to provide warm-path diagnostics; contains no assertions.
/// </summary>
public class DatabaseLatencyTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactory { get; init; }

	private ISharpDatabase Database => WebAppFactory.Services.GetRequiredService<ISharpDatabase>();

	[Test]
	public async Task ReportObjectNodeLookupLatency()
	{
		const int iterations = 100;
		const int warmup = 10;

		// Warm up
		for (var i = 0; i < warmup; i++)
			await Database.GetObjectNodeAsync(new DBRef(1));

		var timings = new long[iterations];
		for (var i = 0; i < iterations; i++)
		{
			var sw = Stopwatch.StartNew();
			await Database.GetObjectNodeAsync(new DBRef(1));
			timings[i] = sw.ElapsedTicks;
		}

		PrintReport("GetObjectNodeAsync(#1)", timings, iterations);
	}

	[Test]
	public async Task ReportAttributeReadLatency()
	{
		const int iterations = 100;
		const int warmup = 10;

		for (var i = 0; i < warmup; i++)
			await ConsumeAsync(Database.GetAttributeAsync(new DBRef(1), ["AADESC"]));

		var timings = new long[iterations];
		for (var i = 0; i < iterations; i++)
		{
			var sw = Stopwatch.StartNew();
			await ConsumeAsync(Database.GetAttributeAsync(new DBRef(1), ["AADESC"]));
			timings[i] = sw.ElapsedTicks;
		}

		PrintReport("GetAttributeAsync(#1, AADESC)", timings, iterations);
	}

	[Test]
	public async Task ReportContentsEnumerationLatency()
	{
		const int iterations = 100;
		const int warmup = 10;

		var masterRoom = (await Database.GetObjectNodeAsync(new DBRef(2))).Known.AsContainer;

		for (var i = 0; i < warmup; i++)
			await ConsumeAsync(Database.GetContentsAsync(masterRoom));

		var timings = new long[iterations];
		for (var i = 0; i < iterations; i++)
		{
			var sw = Stopwatch.StartNew();
			await ConsumeAsync(Database.GetContentsAsync(masterRoom));
			timings[i] = sw.ElapsedTicks;
		}

		PrintReport("GetContentsAsync(MasterRoom)", timings, iterations);
	}

	[Test]
	public async Task ReportLocationTraversalLatency()
	{
		const int iterations = 100;
		const int warmup = 10;

		for (var i = 0; i < warmup; i++)
			await Database.GetLocationAsync(new DBRef(1));

		var timings = new long[iterations];
		for (var i = 0; i < iterations; i++)
		{
			var sw = Stopwatch.StartNew();
			await Database.GetLocationAsync(new DBRef(1));
			timings[i] = sw.ElapsedTicks;
		}

		PrintReport("GetLocationAsync(#1)", timings, iterations);
	}

	private static async Task ConsumeAsync<T>(IAsyncEnumerable<T> source)
	{
		await foreach (var _ in source)
		{ /* enumerate */ }
	}

	private static void PrintReport(string label, long[] timings, int iterations)
	{
		if (timings.Length == 0)
			throw new ArgumentException("Timings collection must not be empty.", nameof(timings));

		Array.Sort(timings);
		var ticksPerMs = (double)Stopwatch.Frequency / 1000.0;
		var sampleCount = timings.Length;

		var meanMs = timings.Average() / ticksPerMs;
		var p50Ms = timings[GetPercentileIndex(sampleCount, 0.50)] / ticksPerMs;
		var p95Ms = timings[GetPercentileIndex(sampleCount, 0.95)] / ticksPerMs;
		var p99Ms = timings[GetPercentileIndex(sampleCount, 0.99)] / ticksPerMs;

		Console.WriteLine($"\n[{label}]");
		Console.WriteLine($"  Iterations : {iterations}");
		Console.WriteLine($"  Mean       : {meanMs,8:F3} ms");
		Console.WriteLine($"  P50        : {p50Ms,8:F3} ms");
		Console.WriteLine($"  P95        : {p95Ms,8:F3} ms");
		Console.WriteLine($"  P99        : {p99Ms,8:F3} ms");
	}

	private static int GetPercentileIndex(int count, double percentile)
	{
		var index = (int)Math.Ceiling(percentile * count) - 1;
		return Math.Clamp(index, 0, count - 1);
	}
}
