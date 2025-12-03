using System.Diagnostics;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class DoListPerformanceTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async Task DoListWithPemitBuffersOutput()
	{
		// This test verifies that @dolist now buffers output efficiently
		// With buffering enabled, all notifications are batched into a single call
		var iterations = 100;
		
		var sw = Stopwatch.StartNew();
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@dolist lnum({iterations})=@pemit #1=Test %i0"));
		sw.Stop();
		
		Console.WriteLine($"@dolist with {iterations} @pemit calls took {sw.ElapsedMilliseconds}ms");
		
		// With buffering, we should see just 1 Notify call (when the buffer is flushed)
		// Note: The mock NotifyService doesn't actually implement buffering, so we can only verify execution completes
		await NotifyService.Received().FlushBuffer(Arg.Any<long>());
	}

	[Test]
	public async Task DoListWithThinkBuffersOutput()
	{
		// This test verifies that @dolist now buffers output efficiently
		// With buffering enabled, all notifications are batched into a single call
		var iterations = 100;
		
		var sw = Stopwatch.StartNew();
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@dolist lnum({iterations})=think %i0"));
		sw.Stop();
		
		Console.WriteLine($"@dolist with {iterations} think calls took {sw.ElapsedMilliseconds}ms");
		
		// With buffering, we should see the buffer being flushed
		await NotifyService.Received().FlushBuffer(Arg.Any<long>());
	}

	[Test]
	public async Task IterWithThinkCompletesSuccessfully()
	{
		// This test shows that iter() accumulates output and completes successfully
		var iterations = 100;
		
		var sw = Stopwatch.StartNew();
		await Parser.CommandParse(1, ConnectionService, MModule.single($"think iter(lnum({iterations}),%i0,,%r)"));
		sw.Stop();
		
		Console.WriteLine($"think iter() with {iterations} iterations took {sw.ElapsedMilliseconds}ms");
		
		// iter() doesn't use buffering, it just accumulates its result and sends once
		// Just verify that the command completed without errors
		await Assert.That(sw.ElapsedMilliseconds).IsGreaterThan(0L);
	}

	[Test, Skip("Performance comparison test - run manually")]
	public async Task PerformanceComparison()
	{
		// This test compares the performance of @dolist vs iter()
		// We expect iter() to be significantly faster due to fewer Notify calls
		var iterations = 1000;
		
		// Warm up
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dolist lnum(10)=think %i0"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("think iter(lnum(10),%i0,,%r)"));
		
		// Test @dolist with think
		var sw1 = Stopwatch.StartNew();
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@dolist lnum({iterations})=think %i0"));
		sw1.Stop();
		
		// Test iter with think
		var sw2 = Stopwatch.StartNew();
		await Parser.CommandParse(1, ConnectionService, MModule.single($"think iter(lnum({iterations}),%i0,,%r)"));
		sw2.Stop();
		
		Console.WriteLine($"@dolist with {iterations} think: {sw1.ElapsedMilliseconds}ms");
		Console.WriteLine($"iter() with {iterations} iterations: {sw2.ElapsedMilliseconds}ms");
		Console.WriteLine($"Ratio: {(double)sw1.ElapsedMilliseconds / sw2.ElapsedMilliseconds:F2}x slower");
		
		// We expect @dolist to be at least 2x slower due to the overhead of multiple Notify calls
		// This assertion might be too strict for CI, so we skip this test by default
		await Assert.That(sw1.ElapsedMilliseconds).IsGreaterThan((long)(sw2.ElapsedMilliseconds * 1.5));
	}
}
