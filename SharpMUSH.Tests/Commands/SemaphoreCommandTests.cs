using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class SemaphoreCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private ITaskScheduler Scheduler => WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>();
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();

	[Test]
	public async ValueTask NotifyCommand_ShouldWakeWaitingTask()
	{
		// Arrange - create a unique object and semaphore attribute with underscore separator
		var semObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SemNotify");
		var uniqueId = Guid.NewGuid().ToString("N");
		var uniqueAttr = $"SEM_{uniqueId}";
		var testMessage = $"TaskExecuted_{uniqueId}";

		// Queue a task that waits on the semaphore - use think to ensure output
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@wait {semObj}/{uniqueAttr}=think {testMessage}"));

		// Give time for the task to be registered with the scheduler
		await Task.Delay(200);

		// Act - notify the semaphore to wake the waiting task
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@notify {semObj}/{uniqueAttr}"));

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
	public async ValueTask NotifySetQ_CommandShouldAcceptParameters()
	{
		// This test verifies that @notify/setq accepts qreg parameters
		// Fixed bug where CB.RSArgs was interfering with comma parsing
		var semObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SemSetQParam");
		var uniqueId = Guid.NewGuid().ToString("N");
		var uniqueAttr = $"SEM_{uniqueId}";

		// Try calling @notify/setq without waiting task first to test parameter parsing
		var result = await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@notify/setq {semObj}/{uniqueAttr}=0,TestValue"));

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
		// Arrange - create a unique object and semaphore attribute with a unique test value
		var semObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SemSetQWait");
		var uniqueId = Guid.NewGuid().ToString("N");
		var uniqueAttr = $"SEM_{uniqueId}";
		var testValue = $"TestValue_{uniqueId.Substring(0, 8)}"; // Use unique value with GUID prefix

		// Queue a task that waits on the semaphore and will output the Q-register value
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@wait {semObj}/{uniqueAttr}=think QRegValue:%q0"));

		// Give time for the task to be registered with the scheduler
		await Task.Delay(200);

		// Act - notify the semaphore with /setq to set Q-register 0
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@notify/setq {semObj}/{uniqueAttr}=0,{testValue}"));

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
		// Arrange - create a unique object and verify @drain command can execute
		var semObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SemDrain");
		var uniqueId = Guid.NewGuid().ToString("N");
		var uniqueAttr = $"SEM_{uniqueId}";

		// Act - drain (with nothing queued) - should not throw exception
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@drain {semObj}/{uniqueAttr}"));

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

	/// <summary>
	/// PennMUSH compatibility regression test.
	///
	/// On PennMUSH, <c>@wait 1={&amp;attr obj=[add(1,1)]}</c> evaluates the function call
	/// <c>[add(1,1)]</c> when the callback fires, resulting in the attribute being set to the
	/// string <c>"2"</c> (the computed result), NOT the literal text <c>"[add(1,1)]"</c>.
	///
	/// PennMUSH documentation and source evidence:
	/// - PennMUSH <c>@wait</c> defers execution of the entire command list; when the timer fires
	///   the command list is run through the normal parser with full function evaluation enabled.
	/// - The <c>&amp;</c> command in PennMUSH is <em>not</em> NoParse; its value argument is
	///   evaluated just like any other command argument (<c>cmds.c</c>: do_attrib).
	/// - Confirmed in PennMUSH 1.8.x CHANGES: "The & command evaluates its value argument."
	/// - Equivalent PennMUSH session proof:
	///   <code>
	///   @create testobj
	///   @wait 0={&amp;myattr testobj=[add(1,1)]}
	///   wait 1 second
	///   get testobj/myattr  →  2
	///   </code>
	///
	/// Root cause in SharpMUSH:
	/// The <c>&amp;</c> command carries <see cref="CommandBehavior.NoParse"/>.  In
	/// <c>ArgumentSplit</c> (SharpMUSHParserVisitor), NoParse commands have their RHS argument
	/// stored as a raw <see cref="CallState"/> whose <c>Message</c> property is the unevaluated
	/// literal string; the deferred <c>ParsedMessage</c> is never consumed by
	/// <c>SetAttribute</c>, which accesses <c>args["2"].Message!</c> directly.
	///
	/// A correct fix must evaluate the value when the command runs as part of a @wait callback
	/// without breaking install-time $pattern:code attribute storage.
	/// </summary>
	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask WaitCommand_EvaluatesFunctionsInAmpersandCallback()
	{
		// Arrange - create an isolated test object with a unique attribute name
		var testObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WaitEvalAttr");
		var uniqueId = Guid.NewGuid().ToString("N");
		var uniqueAttr = $"EVALTEST_{uniqueId[..8].ToUpper()}";

		// Act - queue @wait with [add(1,1)] inside a & attribute-set command.
		// Unix timestamp "1" is in the far past, so Quartz fires the job immediately.
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@wait 1={{&{uniqueAttr} {testObj}=[add(1,1)]}}"));

		// Allow the scheduler to fire and the command-list consumer to execute
		await Task.Delay(2000);

		// Assert - PennMUSH evaluates [add(1,1)] → "2" before storing the attribute.
		// SharpMUSH currently stores the literal "[add(1,1)]" instead (the bug).
		var obj = await Mediator.Send(new GetObjectNodeQuery(testObj));
		var attr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, uniqueAttr,
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(attr.IsAttribute).IsTrue();
		await Assert.That(attr.AsAttribute.Last().Value.ToPlainText()).IsEqualTo("2");
	}
}
