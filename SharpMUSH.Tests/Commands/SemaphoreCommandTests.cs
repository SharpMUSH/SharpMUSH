using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class SemaphoreCommandTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private ITaskScheduler Scheduler => WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>();
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();

	[Test]
	public async ValueTask NotifyCommand_WithObject_ShouldShowNotifiedMessage()
	{
		// Arrange
		var uniqueId = Guid.NewGuid().ToString("N");
		var uniqueAttr = $"SEM_{uniqueId}";
		
		// Act - notify the semaphore (no wait queued)
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@notify #1/{uniqueAttr}"));

		// Assert - should show "Notified." message
		await NotifyService.Received().Notify(Arg.Any<AnySharpObject>(), "Notified.");
	}

	[Test]
	public async ValueTask DolistInline_ShouldExecuteImmediately()
	{
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
	public async ValueTask NotifySetQ_ShouldAcceptQRegisterParameters()
	{
		// Arrange
		var uniqueId = Guid.NewGuid().ToString("N");
		var uniqueAttr = $"SEM_{uniqueId}";
		
		// Act - use @notify/setq with qreg parameters
		// This tests that the command parses and executes without errors
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@notify/setq #1/{uniqueAttr}=0,TestValue"));

		// Assert - verify the command executed without throwing
		// Since there's no waiting task, we expect "Notified." message (queue was empty)
		await NotifyService.Received().Notify(
			Arg.Any<AnySharpObject>(), 
			"Notified.");
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
