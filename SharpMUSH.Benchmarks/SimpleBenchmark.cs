using System.Collections.Concurrent;
using System.Diagnostics;

namespace SharpMUSH.Benchmarks;

/// <summary>
/// Simple, direct performance measurement without BenchmarkDotNet overhead.
/// This gives us quick estimates of actual performance.
/// </summary>
public class SimpleBenchmark
{
	public static void Run()
	{
		Console.WriteLine("=== Simple Performance Measurement ===\n");
		
		// Test 1: ConcurrentDictionary Get
		var dict = new ConcurrentDictionary<long, string>();
		dict.TryAdd(1, "test data");
		
		const int iterations = 10_000_000;
		
		// Warmup
		for (int i = 0; i < 1000; i++)
		{
			_ = dict.GetValueOrDefault(1L);
		}
		
		// Measure Get
		var sw1 = Stopwatch.StartNew();
		for (int i = 0; i < iterations; i++)
		{
			_ = dict.GetValueOrDefault(1L);
		}
		sw1.Stop();
		
		var nsPerGet = (sw1.Elapsed.TotalNanoseconds / iterations);
		Console.WriteLine($"ConcurrentDictionary.GetValueOrDefault:");
		Console.WriteLine($"  {iterations:N0} iterations in {sw1.ElapsedMilliseconds}ms");
		Console.WriteLine($"  {nsPerGet:F2} ns per operation");
		Console.WriteLine();
		
		// Test 2: ConcurrentDictionary Update
		var sw2 = Stopwatch.StartNew();
		for (int i = 0; i < iterations; i++)
		{
			dict.AddOrUpdate(1L, "new", (_, _) => "new");
		}
		sw2.Stop();
		
		var nsPerUpdate = (sw2.Elapsed.TotalNanoseconds / iterations);
		Console.WriteLine($"ConcurrentDictionary.AddOrUpdate:");
		Console.WriteLine($"  {iterations:N0} iterations in {sw2.ElapsedMilliseconds}ms");
		Console.WriteLine($"  {nsPerUpdate:F2} ns per operation");
		Console.WriteLine();
		
		// Test 3: Simulated Redis-like operation (Task overhead + artificial delay)
		const int redisIterations = 10_000;
		var sw3 = Stopwatch.StartNew();
		for (int i = 0; i < redisIterations; i++)
		{
			// Simulate minimal async overhead (no actual Redis, just Task creation)
			_ = SimulateAsyncGet().GetAwaiter().GetResult();
		}
		sw3.Stop();
		
		var usPerAsyncGet = (sw3.Elapsed.TotalMicroseconds / redisIterations);
		Console.WriteLine($"Simulated async Get (Task overhead only, no I/O):");
		Console.WriteLine($"  {redisIterations:N0} iterations in {sw3.ElapsedMilliseconds}ms");
		Console.WriteLine($"  {usPerAsyncGet:F2} μs per operation");
		Console.WriteLine();
		
		// Calculate ratios
		Console.WriteLine("=== Performance Ratios ===");
		Console.WriteLine($"In-memory Get:     {nsPerGet,10:F2} ns");
		Console.WriteLine($"In-memory Update:  {nsPerUpdate,10:F2} ns");
		Console.WriteLine($"Async overhead:    {usPerAsyncGet * 1000,10:F2} ns ({usPerAsyncGet:F2} μs)");
		Console.WriteLine();
		
		var asyncOverheadRatio = (usPerAsyncGet * 1000) / nsPerGet;
		Console.WriteLine($"Async overhead is {asyncOverheadRatio:F0}x slower than in-memory Get");
		Console.WriteLine("(This is JUST async overhead - real Redis adds 200-1000μs network latency)");
		Console.WriteLine();
		
		// Realistic Redis estimate
		const double typicalRedisLatencyUs = 500; // 0.5ms for localhost Redis
		var redisRatio = (typicalRedisLatencyUs * 1000) / nsPerGet;
		Console.WriteLine($"Estimated Redis Get (500μs):  {redisRatio:F0}x slower than in-memory");
		Console.WriteLine();
		
		// Real-world impact
		Console.WriteLine("=== Real-World Impact ===");
		Console.WriteLine("Scenario: 100 commands with 2 Get operations each");
		Console.WriteLine();
		
		var inMemoryTime = (200 * nsPerGet) / 1_000_000; // Convert to ms
		var redisTime = 200 * typicalRedisLatencyUs / 1000; // Convert to ms
		
		Console.WriteLine($"In-memory approach:  {inMemoryTime:F4} ms");
		Console.WriteLine($"Redis-only approach: {redisTime:F2} ms");
		Console.WriteLine($"Performance loss:    {redisTime / inMemoryTime:F0}x slower");
		Console.WriteLine();
		
		Console.WriteLine("=== Conclusion ===");
		Console.WriteLine($"Based on these measurements:");
		Console.WriteLine($"- In-memory Get: ~{nsPerGet:F0}ns");
		Console.WriteLine($"- Redis Get (estimated): ~500μs = 500,000ns");
		Console.WriteLine($"- Actual ratio: ~{redisRatio:F0}x (not the 500,000x I incorrectly claimed)");
		Console.WriteLine();
		Console.WriteLine("The original analysis was still directionally correct:");
		Console.WriteLine("- Redis-only would be dramatically slower");
		Console.WriteLine("- Hybrid in-memory + Redis is the right pattern");
		Console.WriteLine("But the specific numbers were not empirically measured.");
	}
	
	private static async Task<string> SimulateAsyncGet()
	{
		// Just return a completed task to measure async overhead
		await Task.CompletedTask;
		return "test";
	}
}
