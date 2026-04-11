using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class WizardCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask HaltCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@halt #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), "Huh?  (Type \"help\" for help.)", null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask AllhaltCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@allhalt"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "All objects halted")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DrainCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@drain #1"));

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "#-1")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask PsCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@ps"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), "Huh?  (Type \"help\" for help.)", null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask PsWithTarget()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@ps #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), "Huh?  (Type \"help\" for help.)", null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask TriggerCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Set an attribute first
		await Parser.CommandParse(1, ConnectionService, MModule.single("&TRIGGER_TEST_WIZ_UNIQUE #1=think Triggered!"));

		// Trigger it
		await Parser.CommandParse(1, ConnectionService, MModule.single("@trigger #1/TRIGGER_TEST_WIZ_UNIQUE"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), "Huh?  (Type \"help\" for help.)", null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ForceCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@force #1=think Forced!"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<OneOf.OneOf<MString, string>>(), null, INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// Verifies that @force evaluates functions inside &amp;attr obj=value commands.
	/// <c>@force me=&amp;testattr me=[add(1,1)]</c> should set the attribute to "2" (evaluated),
	/// not the literal string "[add(1,1)]".
	/// </summary>
	[Test]
	public async ValueTask ForceCommand_EvaluatesAmpersandAttrValue()
	{
		var attrName = $"FORCEEVAL_{Guid.NewGuid():N}"[..20];

		// Use @force to set an attribute with a function call as the value
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@force me=&{attrName} me=[add(1,1)]"));

		// Read back the attribute value using think [get()]
		var result = await Parser.CommandParse(1, ConnectionService,
			MModule.single($"think [get(me/{attrName})]"));

		var attrValue = result.Message?.ToPlainText()?.Trim() ?? "";
		await Assert.That(attrValue).IsEqualTo("2")
			.Because("@force should evaluate [add(1,1)] to 2 before the & command stores it");
	}

	[Test]
	public async ValueTask NotifyCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@notify #1"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Notified")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask WaitCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@wait 1=think Waited"));

		// Note: This test doesn't verify the wait actually happened, just that the command executed
		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "#-1")), null, INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// Verifies that @wait evaluates functions inside &amp;attr obj=value commands.
	/// <c>@wait 1=&amp;testattr obj=[add(1,1)]</c> should, after the delay fires, set the
	/// attribute to "2" (evaluated), not the literal string "[add(1,1)]".
	/// 
	/// This works because the DirectInput flag (ParserStateFlags) is cleared for queue/callback
	/// contexts, so the &amp; command evaluates the RHS via ParsedMessage().
	/// </summary>
	[Test]
	[NotInParallel]
	public async ValueTask WaitCommand_EvaluatesAmpersandAttrValue()
	{
		// Arrange - create an isolated test object with a unique attribute name
		var testObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WaitEvalWiz");
		var uniqueId = Guid.NewGuid().ToString("N");
		var attrName = $"WIZWAIT_{uniqueId[..8].ToUpper()}";

		// Act - queue @wait with [add(1,1)] inside a & attribute-set command after 1s
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@wait 1={{&{attrName} {testObj}=[add(1,1)]}}"));

		// Allow the scheduler to fire and the command-list consumer to execute.
		// [NotInParallel] ensures the queue consumer isn't saturated by other tests.
		await Task.Delay(3000);

		// Assert - the & command should evaluate [add(1,1)] → "2" before storing
		var obj = await Mediator.Send(new GetObjectNodeQuery(testObj));
		var attr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, attrName,
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(attr.IsAttribute).IsTrue()
			.Because("@wait callback should have set the attribute");
		await Assert.That(attr.AsAttribute.Last().Value.ToPlainText()).IsEqualTo("2")
			.Because("@wait should evaluate [add(1,1)] to 2 when the callback fires");
	}

	/// <summary>
	/// Verifies that @wait preserves pattern-match %0-%9 from the enclosing $command scope.
	/// When a $command pattern sets %0 to a matched value, @wait callbacks should still see
	/// that %0, not @wait's own args. This matches PennMUSH wenv preservation behavior.
	/// </summary>
	[Test]
	[NotInParallel]
	public async ValueTask WaitCommand_PreservesPatternMatchArgs()
	{
		// Create a test object with a $command that uses @wait to store %0
		var testObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WaitArgObj");
		var uniqueId = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var resultAttr = $"RESULT_{uniqueId}";

		// Set up a $command pattern: when triggered, stores %0 via @wait callback
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&CMD_TEST_{uniqueId} {testObj}=$testcmd_{uniqueId} *:@wait 1={{&{resultAttr} {testObj}=%0}}"));

		// Trigger the $command — %0 should be "hello_world"
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"testcmd_{uniqueId} hello_world"));

		// Allow the scheduler to fire and the command-list consumer to execute.
		// [NotInParallel] ensures the queue consumer isn't saturated by other tests.
		await Task.Delay(3000);

		// Assert - the attribute should contain the pattern-matched value, not @wait's arg
		var obj = await Mediator.Send(new GetObjectNodeQuery(testObj));
		var attr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, resultAttr,
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(attr.IsAttribute).IsTrue()
			.Because("@wait callback should have set the attribute");
		await Assert.That(attr.AsAttribute.Last().Value.ToPlainText()).IsEqualTo("hello_world")
			.Because("@wait callback should see %0 from the enclosing $command pattern, not @wait's delay arg");
	}

	[Test]
	public async ValueTask UptimeCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@uptime"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<OneOf.OneOf<MString, string>>(), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask DbckCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dbck"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), "Huh?  (Type \"help\" for help.)", null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask DumpCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@dump"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), "Huh?  (Type \"help\" for help.)", null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask QuotaCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@quota #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), "Huh?  (Type \"help\" for help.)", null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask AllquotaCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@allquota"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), "Huh?  (Type \"help\" for help.)", null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask BootCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@boot #1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), "Huh?  (Type \"help\" for help.)", null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask WallCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@wall Test wall message"));

		await NotifyService
			.Received()
			.Notify(executor.Number, Arg.Any<OneOf.OneOf<MString, string>>(), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask WizwallCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@wizwall Test wizwall message"));

		await NotifyService
			.Received()
			.Notify(executor.Number, Arg.Any<OneOf.OneOf<MString, string>>(), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[DependsOn(nameof(ReadCacheCommand))]
	public async ValueTask PollCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@poll"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "poll") || TestHelpers.MessageContains(s, "Poll")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Hide_NoSwitch_TogglesHidden()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test that @hide without switches toggles the DARK flag


		// First call should hide (set DARK)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "now hidden")), null, INotifyService.NotificationType.Announce);



		// Second call should unhide (unset DARK)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "no longer hidden") || TestHelpers.MessageContains(s, "visible")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Hide_YesSwitch_SetsHidden()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test that @hide/yes sets the DARK flag


		// Ensure we start unhidden (call @hide/off first)
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/off"));


		// Now test @hide/yes
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/yes"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "hidden")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Hide_OnSwitch_SetsHidden()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test that @hide/on sets the DARK flag


		// Ensure we start unhidden
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/off"));


		// Now test @hide/on
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/on"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s
				=> s.Value.ToString()!.Contains("hidden")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Hide_NoSwitch_UnsetsHidden()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test that @hide/no unsets the DARK flag


		// Ensure we start hidden
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/on"));


		// Now test @hide/no
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/no"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s
				=> TestHelpers.MessageContains(s, "no longer hidden") || TestHelpers.MessageContains(s, "visible")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Hide_OffSwitch_UnsetsHidden()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test that @hide/off unsets the DARK flag


		// Ensure we start hidden
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/on"));


		// Now test @hide/off
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/off"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s
				=> TestHelpers.MessageContains(s, "no longer hidden") || TestHelpers.MessageContains(s, "visible")), null, INotifyService.NotificationType.Announce);
	}

	[Test, NotInParallel]
	public async ValueTask Hide_AlreadyHidden_ShowsAppropriateMessage()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test that @hide/on when already hidden shows appropriate message


		// Set hidden
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/on"));


		// Try to set hidden again
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/on"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s
				=> s.Value.ToString()!.Contains("already hidden")), null, INotifyService.NotificationType.Announce);
	}

	[Test, NotInParallel]
	public async ValueTask Hide_AlreadyVisible_ShowsAppropriateMessage()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test that @hide/off when already visible shows appropriate message


		// Ensure unhidden
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/off"));


		// Try to set visible again
		await Parser.CommandParse(1, ConnectionService, MModule.single("@hide/off"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s
				=> s.Value.ToString()!.Contains("already visible")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask PurgeCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@purge"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Purge complete") || TestHelpers.MessageContains(s, "GOING_TWICE")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ReadCacheCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@readcache"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Reindexing text files") || TestHelpers.MessageContains(s, "Text file cache rebuilt")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ShutdownCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@shutdown"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "SHUTDOWN") || TestHelpers.MessageContains(s, "web") || TestHelpers.MessageContains(s, "orchestration")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ShutdownRebootCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@shutdown/reboot"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "REBOOT") || TestHelpers.MessageContains(s, "web") || TestHelpers.MessageContains(s, "orchestration")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[DependsOn(nameof(AllhaltCommand))]
	public async ValueTask ChownallCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// This test may need adjustment based on actual player setup
		await Parser.CommandParse(1, ConnectionService, MModule.single("@chownall #1"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Permission denied") || TestHelpers.MessageContains(s, "objects") || TestHelpers.MessageContains(s, "ownership")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[DependsOn(nameof(PollCommand))]
	public async ValueTask SuggestListCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@suggest/list"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Suggestion categories") || TestHelpers.MessageContains(s, "No suggestion categories")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[DependsOn(nameof(SuggestListCommand))]
	public async ValueTask SuggestAddCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@suggest/add testcat547=testword923"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Added 'testword923' to category 'testcat547'")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[DependsOn(nameof(SuggestAddCommand))]
	public async ValueTask PollSetCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@poll TestPollMessage897"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Poll message set") || TestHelpers.MessageContains(s, "Permission")), null, INotifyService.NotificationType.Announce);
	}

	[Test]
	[DependsOn(nameof(PollSetCommand))]
	public async ValueTask PollClearCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@poll/clear"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Poll message cleared") || TestHelpers.MessageContains(s, "Permission")), null, INotifyService.NotificationType.Announce);
	}


}
