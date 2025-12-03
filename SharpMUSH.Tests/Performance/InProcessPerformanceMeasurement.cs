using System.Diagnostics;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Performance;

/// <summary>
/// Measures actual performance of @dolist vs iter() to identify the bottleneck.
/// This runs in-process with real services to get accurate measurements.
/// </summary>
[NotInParallel]
public class InProcessPerformanceMeasurement
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async Task MeasureDoListVsIterPerformance()
	{
		Console.WriteLine("=== Performance Measurement: @dolist vs iter() ===\n");
		
		// Warm up
		await Parser.CommandParse(1, ConnectionService, MModule.single("think test"));
		
		// Test 1: @dolist with small iteration (100)
		Console.WriteLine("Test 1: @dolist lnum(100)=think %i0");
		var sw1 = Stopwatch.StartNew();
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dolist lnum(100)=think %i0"));
		sw1.Stop();
		Console.WriteLine($"  Time: {sw1.ElapsedMilliseconds}ms");
		Console.WriteLine($"  Notify calls: Check if buffering was used");
		
		// Test 2: iter with small iteration (100)
		Console.WriteLine("\nTest 2: think iter(lnum(100),%i0,,%r)");
		var sw2 = Stopwatch.StartNew();
		await Parser.CommandParse(1, ConnectionService, MModule.single("think iter(lnum(100),%i0,,%r)"));
		sw2.Stop();
		Console.WriteLine($"  Time: {sw2.ElapsedMilliseconds}ms");
		
		// Test 3: @dolist with large iteration (1000)
		Console.WriteLine("\nTest 3: @dolist lnum(1000)=think %i0");
		var sw3 = Stopwatch.StartNew();
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dolist lnum(1000)=think %i0"));
		sw3.Stop();
		Console.WriteLine($"  Time: {sw3.ElapsedMilliseconds}ms");
		
		// Test 4: iter with large iteration (1000)
		Console.WriteLine("\nTest 4: think iter(lnum(1000),%i0,,%r)");
		var sw4 = Stopwatch.StartNew();
		await Parser.CommandParse(1, ConnectionService, MModule.single("think iter(lnum(1000),%i0,,%r)"));
		sw4.Stop();
		Console.WriteLine($"  Time: {sw4.ElapsedMilliseconds}ms");
		
		// Test 5: @dolist with @pemit (100)
		Console.WriteLine("\nTest 5: @dolist lnum(100)=@pemit %#=%i0");
		var sw5 = Stopwatch.StartNew();
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dolist lnum(100)=@pemit %#=%i0"));
		sw5.Stop();
		Console.WriteLine($"  Time: {sw5.ElapsedMilliseconds}ms");
		
		// Test 6: @dolist with @pemit (1000)
		Console.WriteLine("\nTest 6: @dolist lnum(1000)=@pemit %#=%i0");
		var sw6 = Stopwatch.StartNew();
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dolist lnum(1000)=@pemit %#=%i0"));
		sw6.Stop();
		Console.WriteLine($"  Time: {sw6.ElapsedMilliseconds}ms");
		
		// Test 7: Nested @dolist to test buffering scope behavior
		Console.WriteLine("\nTest 7: Nested @dolist (outer 10, inner 10)");
		var sw7 = Stopwatch.StartNew();
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dolist lnum(10)={@dolist lnum(10)=think %i0}"));
		sw7.Stop();
		Console.WriteLine($"  Time: {sw7.ElapsedMilliseconds}ms");
		
		// Summary
		Console.WriteLine("\n=== SUMMARY ===");
		Console.WriteLine($"@dolist 100 think:     {sw1.ElapsedMilliseconds,5}ms");
		Console.WriteLine($"iter 100:              {sw2.ElapsedMilliseconds,5}ms");
		Console.WriteLine($"@dolist 1000 think:    {sw3.ElapsedMilliseconds,5}ms");
		Console.WriteLine($"iter 1000:             {sw4.ElapsedMilliseconds,5}ms");
		Console.WriteLine($"@dolist 100 @pemit:    {sw5.ElapsedMilliseconds,5}ms");
		Console.WriteLine($"@dolist 1000 @pemit:   {sw6.ElapsedMilliseconds,5}ms");
		Console.WriteLine($"Nested @dolist (10x10): {sw7.ElapsedMilliseconds,5}ms");
		
		Console.WriteLine("\n=== ANALYSIS ===");
		if (sw1.ElapsedMilliseconds > 0 && sw2.ElapsedMilliseconds > 0)
		{
			var ratio1 = (double)sw1.ElapsedMilliseconds / sw2.ElapsedMilliseconds;
			Console.WriteLine($"@dolist vs iter (100):  {ratio1:F2}x");
		}
		if (sw3.ElapsedMilliseconds > 0 && sw4.ElapsedMilliseconds > 0)
		{
			var ratio2 = (double)sw3.ElapsedMilliseconds / sw4.ElapsedMilliseconds;
			Console.WriteLine($"@dolist vs iter (1000): {ratio2:F2}x");
		}
		
		// Check if buffering is working
		Console.WriteLine("\n=== CURRENT STATE ===");
		Console.WriteLine("In the current implementation:");
		Console.WriteLine("- @dolist calls Notify() for each iteration separately");
		Console.WriteLine("- iter() accumulates results and calls Notify() once");
		Console.WriteLine("- This difference likely explains the performance gap");
		Console.WriteLine("\nIf @dolist is significantly slower, the bottleneck is likely:");
		Console.WriteLine("1. RabbitMQ message publishing overhead (1000 vs 1 publish)");
		Console.WriteLine("2. Message serialization overhead");
		Console.WriteLine("3. NOT the parsing or execution time");
	}
	
	[Test]
	public async Task MeasureNotifyServiceOverhead()
	{
		Console.WriteLine("=== Measuring NotifyService Call Overhead ===\n");
		
		var handle = 1L;
		var testMessage = "Test message";
		
		// Test: Direct Notify calls to measure overhead
		Console.WriteLine("Test: 1000 direct Notify calls");
		var sw1 = Stopwatch.StartNew();
		for (int i = 0; i < 1000; i++)
		{
			await NotifyService.Notify(handle, testMessage, null);
		}
		sw1.Stop();
		Console.WriteLine($"  Time: {sw1.ElapsedMilliseconds}ms ({sw1.ElapsedMilliseconds / 1000.0:F3}ms per call)");
		
		Console.WriteLine("\nNOTE: This measures the overhead of 1000 individual Notify calls.");
		Console.WriteLine("In the current implementation, each call publishes to RabbitMQ immediately.");
		Console.WriteLine("This is likely the bottleneck causing @dolist to be slower than iter().");
	}
}
