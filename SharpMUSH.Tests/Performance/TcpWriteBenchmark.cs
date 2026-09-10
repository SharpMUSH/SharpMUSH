namespace SharpMUSH.Tests.Performance;

/// <summary>
/// Benchmarks batching service metrics to understand why batching isn't effective.
/// </summary>
public class TcpWriteBenchmark
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	[Test]
	public async Task MeasureBatchingServiceMetrics()
	{
		TestDiagnostics.WriteLine("=== Batching Service Metrics ===\n");

		// Try to get the batching service via reflection since we don't have direct reference
		var batchingServiceType = Type.GetType("SharpMUSH.ConnectionServer.Services.TelnetOutputBatchingService, SharpMUSH.ConnectionServer");

		if (batchingServiceType == null)
		{
			TestDiagnostics.WriteLine("Batching service type not found");
			return;
		}

		var batchingService = WebAppFactoryArg.Services.GetService(batchingServiceType);

		if (batchingService == null)
		{
			TestDiagnostics.WriteLine("Batching service not available in DI container");
			return;
		}

		var getMetricsMethod = batchingServiceType.GetMethod("GetMetrics");
		if (getMetricsMethod == null)
		{
			TestDiagnostics.WriteLine("GetMetrics method not found");
			return;
		}

		var metricsResult = getMetricsMethod.Invoke(batchingService, null);
		if (metricsResult == null)
		{
			TestDiagnostics.WriteLine("Failed to get metrics");
			return;
		}

		var metricsType = metricsResult.GetType();
		var messagesReceived = (long)metricsType.GetField("Item1")!.GetValue(metricsResult)!;
		var batchesFlushed = (long)metricsType.GetField("Item2")!.GetValue(metricsResult)!;
		var avgBatchSize = (double)metricsType.GetField("Item3")!.GetValue(metricsResult)!;
		var flushesFromSize = (long)metricsType.GetField("Item4")!.GetValue(metricsResult)!;
		var flushesFromTimeout = (long)metricsType.GetField("Item5")!.GetValue(metricsResult)!;
		var totalTcpWriteTimeMs = (long)metricsType.GetField("Item6")!.GetValue(metricsResult)!;

		TestDiagnostics.WriteLine($"Total messages received:   {messagesReceived}");
		TestDiagnostics.WriteLine($"Total batches flushed:     {batchesFlushed}");
		TestDiagnostics.WriteLine($"Average batch size:        {avgBatchSize:F2} messages");
		TestDiagnostics.WriteLine($"Flushes from size limit:   {flushesFromSize}");
		TestDiagnostics.WriteLine($"Flushes from timeout:      {flushesFromTimeout}");
		TestDiagnostics.WriteLine($"Total TCP write time:      {totalTcpWriteTimeMs}ms");
		TestDiagnostics.WriteLine();

		if (avgBatchSize < 2.0 && messagesReceived > 100)
		{
			TestDiagnostics.WriteLine("WARNING: Average batch size < 2 - batching is NOT working!");
			TestDiagnostics.WriteLine("Messages are arriving too slowly to batch effectively.");
			TestDiagnostics.WriteLine("This confirms the architectural limitation: messages are published");
			TestDiagnostics.WriteLine("sequentially with awaits between them, so they arrive too slowly to batch.");
		}
		else if (avgBatchSize >= 50)
		{
			TestDiagnostics.WriteLine("SUCCESS: Good batch sizes - batching is working well!");
		}
		else if (avgBatchSize >= 10)
		{
			TestDiagnostics.WriteLine("Moderate batching - some benefit but could be better");
		}

		TestDiagnostics.WriteLine($"\nIf @dolist took ~17000ms and TCP writes took {totalTcpWriteTimeMs}ms,");
		TestDiagnostics.WriteLine($"then TCP overhead is {(double)totalTcpWriteTimeMs / 17000.0 * 100:F1}% of total time.");

		await Task.CompletedTask;
	}
}
