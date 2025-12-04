using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace SharpMUSH.Tests.Performance;

/// <summary>
/// Benchmarks batching service metrics to understand why batching isn't effective.
/// </summary>
[NotInParallel]
public class TcpWriteBenchmark
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	[Test]
	public async Task MeasureBatchingServiceMetrics()
	{
		Console.WriteLine("=== Batching Service Metrics ===\n");
		
		// Try to get the batching service via reflection since we don't have direct reference
		var batchingServiceType = Type.GetType("SharpMUSH.ConnectionServer.Services.TelnetOutputBatchingService, SharpMUSH.ConnectionServer");
		
		if (batchingServiceType == null)
		{
			Console.WriteLine("Batching service type not found");
			return;
		}
		
		var batchingService = WebAppFactoryArg.Services.GetService(batchingServiceType);
		
		if (batchingService == null)
		{
			Console.WriteLine("Batching service not available in DI container");
			return;
		}

		// Get metrics via reflection
		var getMetricsMethod = batchingServiceType.GetMethod("GetMetrics");
		if (getMetricsMethod == null)
		{
			Console.WriteLine("GetMetrics method not found");
			return;
		}
		
		var metricsResult = getMetricsMethod.Invoke(batchingService, null);
		if (metricsResult == null)
		{
			Console.WriteLine("Failed to get metrics");
			return;
		}
		
		// Extract tuple values via reflection
		var metricsType = metricsResult.GetType();
		var messagesReceived = (long)metricsType.GetField("Item1")!.GetValue(metricsResult)!;
		var batchesFlushed = (long)metricsType.GetField("Item2")!.GetValue(metricsResult)!;
		var avgBatchSize = (double)metricsType.GetField("Item3")!.GetValue(metricsResult)!;
		var flushesFromSize = (long)metricsType.GetField("Item4")!.GetValue(metricsResult)!;
		var flushesFromTimeout = (long)metricsType.GetField("Item5")!.GetValue(metricsResult)!;
		var totalTcpWriteTimeMs = (long)metricsType.GetField("Item6")!.GetValue(metricsResult)!;
		
		Console.WriteLine($"Total messages received:   {messagesReceived}");
		Console.WriteLine($"Total batches flushed:     {batchesFlushed}");
		Console.WriteLine($"Average batch size:        {avgBatchSize:F2} messages");
		Console.WriteLine($"Flushes from size limit:   {flushesFromSize}");
		Console.WriteLine($"Flushes from timeout:      {flushesFromTimeout}");
		Console.WriteLine($"Total TCP write time:      {totalTcpWriteTimeMs}ms");
		Console.WriteLine();

		if (avgBatchSize < 2.0 && messagesReceived > 100)
		{
			Console.WriteLine("WARNING: Average batch size < 2 - batching is NOT working!");
			Console.WriteLine("Messages are arriving too slowly to batch effectively.");
			Console.WriteLine("This confirms the architectural limitation: messages are published");
			Console.WriteLine("sequentially with awaits between them, so they arrive too slowly to batch.");
		}
		else if (avgBatchSize >= 50)
		{
			Console.WriteLine("SUCCESS: Good batch sizes - batching is working well!");
		}
		else if (avgBatchSize >= 10)
		{
			Console.WriteLine("Moderate batching - some benefit but could be better");
		}
		
		// Calculate how much time was spent in TCP writes vs total time
		Console.WriteLine($"\nIf @dolist took ~17000ms and TCP writes took {totalTcpWriteTimeMs}ms,");
		Console.WriteLine($"then TCP overhead is {(double)totalTcpWriteTimeMs / 17000.0 * 100:F1}% of total time.");
		
		await Task.CompletedTask;
	}
}
