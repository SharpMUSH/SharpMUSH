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
	[Explicit("Requires Quartz scheduler to process queued tasks - tests queued iteration context")]
	public async ValueTask DolistDefault_ShouldQueueWithIterationContext()
	{
		// Arrange
		var uniqueId = Guid.NewGuid().ToString("N");
		
		// Act - @dolist (without /inline) should queue with iteration context
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@dolist a b c=@pemit #1=Queued{uniqueId}"));

		await Task.Delay(500);

		// Assert - should have executed from queue
		await NotifyService.Received().Notify(
			Arg.Any<AnySharpObject>(), 
			$"Queued{uniqueId}", 
			Arg.Any<AnySharpObject>(), 
			INotifyService.NotificationType.Announce);
	}

	[Test]
	[Explicit("Requires Quartz scheduler to process queued tasks and semaphore operations")]
	public async ValueTask NotifySetQ_ShouldModifyQRegisters()
	{
		// Arrange
		var uniqueId = Guid.NewGuid().ToString("N");
		var uniqueAttr = $"SEM_{uniqueId}";
		
		// Queue a wait command that uses %q0
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@wait #1/{uniqueAttr}=@pemit #1=[r(0)]-{uniqueId}"));

		await Task.Delay(200);

		// Act - use @notify/setq to set q0
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@notify/setq #1/{uniqueAttr}=0,TestValue"));

		await Task.Delay(100);

		// Now notify to run the command
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@notify #1/{uniqueAttr}"));

		await Task.Delay(300);

		// Assert - the command should have used the modified Q-register
		await NotifyService.Received().Notify(
			Arg.Any<AnySharpObject>(), 
			$"TestValue-{uniqueId}", 
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
	[Explicit("Requires Quartz scheduler to process queued tasks - tests timed waits")]
	public async ValueTask WaitCommand_WithTime_CanExecute()
	{
		// Arrange & Act - just verify @wait command can execute
		var uniqueId = Guid.NewGuid().ToString("N");
		
		await Parser.CommandParse(1, ConnectionService, 
			MModule.single($"@wait 1=@pemit #1=Wait{uniqueId}"));

		await Task.Delay(100);

		// No assertion - just verify no exceptions and command parses
	}
}
