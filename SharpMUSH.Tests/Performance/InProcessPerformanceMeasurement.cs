using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Diagnostics;

namespace SharpMUSH.Tests.Performance;

/// <summary>
/// Measures actual performance of @dolist vs iter() to identify the bottleneck.
/// This runs in-process with real services to get accurate measurements.
/// </summary>
public class InProcessPerformanceMeasurement
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test, Explicit]
	public async Task MeasureDoListVsIterPerformance()
	{
		TestDiagnostics.WriteLine("=== Performance Measurement: @dolist vs iter() ===\n");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("think test"));

		TestDiagnostics.WriteLine("Test 1: @dolist lnum(100)=think %i0");
		var sw1 = Stopwatch.StartNew();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@dolist lnum(100)=think %i0"));
		sw1.Stop();
		TestDiagnostics.WriteLine($"  Time: {sw1.ElapsedMilliseconds}ms");
		TestDiagnostics.WriteLine($"  Notify calls: Check if buffering was used");

		TestDiagnostics.WriteLine("\nTest 2: think iter(lnum(100),%i0,,%r)");
		var sw2 = Stopwatch.StartNew();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("think iter(lnum(100),%i0,,%r)"));
		sw2.Stop();
		TestDiagnostics.WriteLine($"  Time: {sw2.ElapsedMilliseconds}ms");

		TestDiagnostics.WriteLine("\nTest 3: @dolist lnum(1000)=think %i0");
		var sw3 = Stopwatch.StartNew();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@dolist lnum(1000)=think %i0"));
		sw3.Stop();
		TestDiagnostics.WriteLine($"  Time: {sw3.ElapsedMilliseconds}ms");

		TestDiagnostics.WriteLine("\nTest 4: think iter(lnum(1000),%i0,,%r)");
		var sw4 = Stopwatch.StartNew();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("think iter(lnum(1000),%i0,,%r)"));
		sw4.Stop();
		TestDiagnostics.WriteLine($"  Time: {sw4.ElapsedMilliseconds}ms");

		TestDiagnostics.WriteLine("\nTest 5: @dolist lnum(100)=@pemit %#=%i0");
		var sw5 = Stopwatch.StartNew();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@dolist lnum(100)=@pemit %#=%i0"));
		sw5.Stop();
		TestDiagnostics.WriteLine($"  Time: {sw5.ElapsedMilliseconds}ms");

		TestDiagnostics.WriteLine("\nTest 6: @dolist lnum(1000)=@pemit %#=%i0");
		var sw6 = Stopwatch.StartNew();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@dolist lnum(1000)=@pemit %#=%i0"));
		sw6.Stop();
		TestDiagnostics.WriteLine($"  Time: {sw6.ElapsedMilliseconds}ms");

		TestDiagnostics.WriteLine("\nTest 7: Nested @dolist (outer 10, inner 10)");
		var sw7 = Stopwatch.StartNew();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@dolist lnum(10)={@dolist lnum(10)=think %i0}"));
		sw7.Stop();
		TestDiagnostics.WriteLine($"  Time: {sw7.ElapsedMilliseconds}ms");

		TestDiagnostics.WriteLine("\n=== SUMMARY ===");
		TestDiagnostics.WriteLine($"@dolist 100 think:     {sw1.ElapsedMilliseconds,5}ms");
		TestDiagnostics.WriteLine($"iter 100:              {sw2.ElapsedMilliseconds,5}ms");
		TestDiagnostics.WriteLine($"@dolist 1000 think:    {sw3.ElapsedMilliseconds,5}ms");
		TestDiagnostics.WriteLine($"iter 1000:             {sw4.ElapsedMilliseconds,5}ms");
		TestDiagnostics.WriteLine($"@dolist 100 @pemit:    {sw5.ElapsedMilliseconds,5}ms");
		TestDiagnostics.WriteLine($"@dolist 1000 @pemit:   {sw6.ElapsedMilliseconds,5}ms");
		TestDiagnostics.WriteLine($"Nested @dolist (10x10): {sw7.ElapsedMilliseconds,5}ms");

		TestDiagnostics.WriteLine("\n=== ANALYSIS ===");
		if (sw1.ElapsedMilliseconds > 0 && sw2.ElapsedMilliseconds > 0)
		{
			var ratio1 = (double)sw1.ElapsedMilliseconds / sw2.ElapsedMilliseconds;
			TestDiagnostics.WriteLine($"@dolist vs iter (100):  {ratio1:F2}x");
		}
		if (sw3.ElapsedMilliseconds > 0 && sw4.ElapsedMilliseconds > 0)
		{
			var ratio2 = (double)sw3.ElapsedMilliseconds / sw4.ElapsedMilliseconds;
			TestDiagnostics.WriteLine($"@dolist vs iter (1000): {ratio2:F2}x");
		}

		TestDiagnostics.WriteLine("\n=== CURRENT STATE ===");
		TestDiagnostics.WriteLine("In the current implementation:");
		TestDiagnostics.WriteLine("- @dolist calls Notify() for each iteration separately");
		TestDiagnostics.WriteLine("- iter() accumulates results and calls Notify() once");
		TestDiagnostics.WriteLine("- This difference likely explains the performance gap");
		TestDiagnostics.WriteLine("\nIf @dolist is significantly slower, the bottleneck is likely:");
		TestDiagnostics.WriteLine("1. Kafka message publishing overhead (1000 vs 1 publish)");
		TestDiagnostics.WriteLine("2. Message serialization overhead");
		TestDiagnostics.WriteLine("3. NOT the parsing or execution time");

		// via reflection to avoid assembly reference
		var batchingServiceType = Type.GetType("SharpMUSH.ConnectionServer.Services.TelnetOutputBatchingService, SharpMUSH.ConnectionServer");
		if (batchingServiceType != null)
		{
			var batchingService = WebAppFactoryArg.Services.GetService(batchingServiceType);
			if (batchingService != null)
			{
				var getMetricsMethod = batchingServiceType.GetMethod("GetMetrics");
				if (getMetricsMethod != null)
				{
					var metricsResult = getMetricsMethod.Invoke(batchingService, null);
					if (metricsResult != null)
					{
						var metricsType = metricsResult.GetType();
						var messagesReceived = (long)metricsType.GetField("Item1")!.GetValue(metricsResult)!;
						var batchesFlushed = (long)metricsType.GetField("Item2")!.GetValue(metricsResult)!;
						var avgBatchSize = (double)metricsType.GetField("Item3")!.GetValue(metricsResult)!;
						var flushesFromSize = (long)metricsType.GetField("Item4")!.GetValue(metricsResult)!;
						var flushesFromTimeout = (long)metricsType.GetField("Item5")!.GetValue(metricsResult)!;
						var totalTcpWriteTimeMs = (long)metricsType.GetField("Item6")!.GetValue(metricsResult)!;

						TestDiagnostics.WriteLine("\n=== BATCHING SERVICE METRICS ===");
						TestDiagnostics.WriteLine($"Messages received:   {messagesReceived}");
						TestDiagnostics.WriteLine($"Batches flushed:     {batchesFlushed}");
						TestDiagnostics.WriteLine($"Avg batch size:      {avgBatchSize:F2}");
						TestDiagnostics.WriteLine($"Flush from size:     {flushesFromSize}");
						TestDiagnostics.WriteLine($"Flush from timeout:  {flushesFromTimeout}");
						TestDiagnostics.WriteLine($"TCP write time:      {totalTcpWriteTimeMs}ms");

						if (avgBatchSize < 2.0 && messagesReceived > 100)
						{
							TestDiagnostics.WriteLine("\nWARNING: Batching is NOT working effectively!");
							TestDiagnostics.WriteLine("Average batch size < 2 means messages arrive too slowly to batch.");
						}
					}
				}
			}
		}
	}

	[Test, Explicit]
	public async Task MeasureNotifyServiceOverhead()
	{
		TestDiagnostics.WriteLine("=== Measuring NotifyService Call Overhead ===\n");

		var handle = 1L;
		var testMessage = "Test message";

		TestDiagnostics.WriteLine("Test: 1000 direct Notify calls");
		var sw1 = Stopwatch.StartNew();
		for (int i = 0; i < 1000; i++)
		{
			await NotifyService.Notify(handle, testMessage, null);
		}
		sw1.Stop();
		TestDiagnostics.WriteLine($"  Time: {sw1.ElapsedMilliseconds}ms ({sw1.ElapsedMilliseconds / 1000.0:F3}ms per call)");

		TestDiagnostics.WriteLine("\nNOTE: This measures the overhead of 1000 individual Notify calls.");
		TestDiagnostics.WriteLine("In the current implementation, each call publishes to Kafka immediately.");
		TestDiagnostics.WriteLine("This is likely the bottleneck causing @dolist to be slower than iter().");
	}
}
