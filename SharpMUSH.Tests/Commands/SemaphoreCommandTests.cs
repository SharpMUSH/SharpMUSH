using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class SemaphoreCommandTests
{
	[ClassDataSource<TestClassFactory>(Shared = SharedType.PerClass)]
	public required TestClassFactory Factory { get; init; }

	private IMUSHCodeParser Parser => Factory.CommandParser;
	private INotifyService NotifyService => Factory.NotifyService;
	private IConnectionService ConnectionService => Factory.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();
	private ITaskScheduler Scheduler => Factory.Services.GetRequiredService<ITaskScheduler>();
	private IAttributeService AttributeService => Factory.Services.GetRequiredService<IAttributeService>();

	[Test]
	public async ValueTask NotifyCommand_ShouldWakeWaitingTask()
	{
		// Clear any previous calls to the mock

		// Arrange - create a unique semaphore and test message with underscore separator
		var uniqueId = Guid.NewGuid().ToString("N");
		var uniqueAttr = $"SEM_{uniqueId}";
		var testMessage = $"TaskExecuted_{uniqueId}";
		
		// Queue a task that waits on the semaphore - use think to ensure output
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@wait #1/{uniqueAttr}=think {testMessage}"));
		
		// Give time for the task to be registered with the scheduler
		await Task.Delay(200);
		
		// Act - notify the semaphore to wake the waiting task
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@notify #1/{uniqueAttr}"));
		
		// Give the scheduler time to execute the queued task
		await Task.Delay(2000);

		// Assert - verify the waiting task was executed
		await NotifyService.Received().Notify(
			Arg.Any<AnySharpObject>(), 
			testMessage,
			Arg.Any<AnySharpObject>(),
			INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DolistInline_ShouldExecuteImmediately()
	{
		// Clear any previous calls to the mock

		// Arrange
		var uniqueId = Guid.NewGuid().ToString("N");
		
		// Act - @dolist/inline should execute immediately
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@dolist/inline a b c=@pemit #1=Inline{uniqueId}"));

		// Assert - all iterations should have executed (checking for at least one to ensure it ran)
		await NotifyService.Received().Notify(
			Arg.Any<AnySharpObject>(), 
			$"Inline{uniqueId}", 
			Arg.Any<AnySharpObject>(), 
			INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DolistDefault_ShouldQueueCommands()
	{
		// Clear any previous calls to the mock

		// Arrange
		var uniqueId = Guid.NewGuid().ToString("N");
		
		// Act - @dolist (without /inline) should queue commands
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@dolist a b c=@pemit #1=Queued{uniqueId}"));

		// Assert - verify queuing happened by checking that jobs were scheduled
		// We verify this indirectly by checking that the command didn't execute inline
		// (if it executed inline, we would see 3 Notify calls immediately)
		// Since we're using NSubstitute mock, we can verify the inline calls didn't happen
		var receivedCalls = NotifyService.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == "Notify")
			.Where(call => 
			{
				var args = call.GetArguments();
				if (args.Length >= 2 && args[1] != null)
				{
					var message = args[1]!.ToString();
					return message != null && message.Contains($"Queued{uniqueId}");
				}
				return false;
			})
			.ToList();
		
		// Should have 0 inline calls (commands are queued, not executed inline)
		if (receivedCalls.Count != 0)
		{
			throw new Exception($"Commands should be queued, not executed inline. Found {receivedCalls.Count} inline calls.");
		}
	}

	[Test]
	public async ValueTask NotifySetQ_CommandShouldAcceptParameters()
	{
		// Clear any previous calls to the mock

		// This test verifies that @notify/setq accepts qreg parameters
		// Fixed bug where CB.RSArgs was interfering with comma parsing
		var uniqueId = Guid.NewGuid().ToString("N");
		var uniqueAttr = $"SEM_{uniqueId}";
		
		// Try calling @notify/setq without waiting task first to test parameter parsing
		var result = await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@notify/setq #1/{uniqueAttr}=0,TestValue"));
		
		// The command should not generate a parsing error about pairs
		// It might say "no queue entry" but shouldn't say "must be in pairs"
		await NotifyService.DidNotReceive().Notify(
			Arg.Any<AnySharpObject>(),
			Arg.Is<OneOf<MString, string>>(msg => 
				msg.Value.ToString()!.Contains("must be in pairs")),
			Arg.Any<AnySharpObject?>(),
			Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask NotifySetQ_ShouldSetQRegisterForWaitingTask()
	{
		// Clear any previous calls to the mock

		// Arrange - create a unique semaphore with a unique test value
		var uniqueId = Guid.NewGuid().ToString("N");
		var uniqueAttr = $"SEM_{uniqueId}";
		var testValue = $"TestValue_{uniqueId.Substring(0, 8)}"; // Use unique value with GUID prefix
		
		// Queue a task that waits on the semaphore and will output the Q-register value
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@wait #1/{uniqueAttr}=think QRegValue:%q0"));
		
		// Give time for the task to be registered with the scheduler
		await Task.Delay(200);
		
		// Act - notify the semaphore with /setq to set Q-register 0
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@notify/setq #1/{uniqueAttr}=0,{testValue}"));
		
		// Give the scheduler time to execute the queued task
		await Task.Delay(2000);
		
		// Assert - verify the task executed with the correct Q-register value
		// The waiting task should have been executed with %q0 set to our test value
		await NotifyService.Received().Notify(
			Arg.Any<AnySharpObject>(), 
			$"QRegValue:{testValue}",
			Arg.Any<AnySharpObject>(),
			INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DrainCommand_Basic()
	{
		// Arrange - just verify @drain command can execute
		var uniqueId = Guid.NewGuid().ToString("N");
		var uniqueAttr = $"SEM_{uniqueId}";
		
		// Act - drain (with nothing queued) - should not throw exception
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@drain #1/{uniqueAttr}"));

		// No assertion - just verify no exceptions
	}

	[Test]
	public async ValueTask WaitCommand_WithTime_CanExecute()
	{
		// Arrange & Act - just verify @wait command can execute without errors
		var uniqueId = Guid.NewGuid().ToString("N");
		
		await Parser.CommandParse(1, ConnectionService, 
			MModule.single($"@wait 1=@pemit #1=Wait{uniqueId}"));

		// No assertion - just verify no exceptions and command parses
		// Note: We don't wait for execution as this tests command parsing, not scheduler execution
	}
}
