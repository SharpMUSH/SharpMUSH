using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using OneOf;

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
		// Create a unique thing to halt, instead of halting shared God (#1).
		var thingDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "HaltTarget");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@halt {thingDbRef}"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), "Halted God and all their objects.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask AllhaltCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@allhalt"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.AllObjectsHaltedWithCountFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask DrainCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@drain #1"));

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "#-1")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
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
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "@ps")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
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
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "@ps")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
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
			.Notify(TestHelpers.MatchingObject(executor), "Triggered!", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ForceCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@force #1=think Forced!"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<OneOf.OneOf<MString, string>>(), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
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

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.Notified), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask WaitCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@wait 1=think Waited"));

		// Note: This test doesn't verify the wait actually happened, just that the command executed
		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "#-1")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
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
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<OneOf.OneOf<MString, string>>(), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
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
			.Notify(TestHelpers.MatchingObject(executor), "Not Supported for SharpMUSH.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
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
			.Notify(TestHelpers.MatchingObject(executor), "Dump command does nothing for SharpMUSH. Consider using @backup.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
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
			.Notify(TestHelpers.MatchingObject(executor), "The quota system is disabled on this server.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
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
			.Notify(TestHelpers.MatchingObject(executor), "Usage: @allquota <amount>", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
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
			.Notify(TestHelpers.MatchingObject(executor), "That player is not connected.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask WallCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@wall Test wall message"));

		await NotifyService
			.Received()
			.Notify(executor.Number, Arg.Is<OneOf.OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "Test wall message")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask WizwallCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@wizwall Test wizwall message"));

		await NotifyService
			.Received()
			.Notify(executor.Number, Arg.Is<OneOf.OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextContains(msg, "Test wizwall message")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[DependsOn(nameof(ReadCacheCommand))]
	public async ValueTask PollCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@poll"));

		// Fresh DB has no poll message set, so PollNoPollMessage is always sent.
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.PollNoPollMessage), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Hide_NoSwitch_TogglesHidden()
	{
		// Use isolated player to avoid modifying shared God (#1).
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HideToggle");

		// First call should hide (set DARK)
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@hide"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.NowHiddenFromWho), testPlayer.DbRef)).IsTrue();



		// Second call should unhide (unset DARK)
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@hide"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.NoLongerHiddenFromWho), testPlayer.DbRef)).IsTrue();
	}

	[Test]
	public async ValueTask Hide_YesSwitch_SetsHidden()
	{
		// Use isolated player to avoid modifying shared God (#1).
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HideYes");

		// Ensure we start unhidden (call @hide/off first)
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@hide/off"));


		// Now test @hide/yes
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@hide/yes"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.NowHiddenFromWho), testPlayer.DbRef)).IsTrue();
	}

	[Test]
	public async ValueTask Hide_OnSwitch_SetsHidden()
	{
		// Use isolated player to avoid modifying shared God (#1).
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HideOn");

		// Ensure we start unhidden
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@hide/off"));


		// Now test @hide/on
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@hide/on"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.NowHiddenFromWho), testPlayer.DbRef)).IsTrue();
	}

	[Test]
	public async ValueTask Hide_NoSwitch_UnsetsHidden()
	{
		// Use isolated player to avoid modifying shared God (#1).
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HideNo");

		// Ensure we start hidden
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@hide/on"));


		// Now test @hide/no
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@hide/no"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.NoLongerHiddenFromWho), testPlayer.DbRef)).IsTrue();
	}

	[Test]
	public async ValueTask Hide_OffSwitch_UnsetsHidden()
	{
		// Use isolated player to avoid modifying shared God (#1).
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HideOff");

		// Ensure we start hidden
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@hide/on"));


		// Now test @hide/off
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@hide/off"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.NoLongerHiddenFromWho), testPlayer.DbRef)).IsTrue();
	}

	[Test, NotInParallel]
	public async ValueTask Hide_AlreadyHidden_ShowsAppropriateMessage()
	{
		// Use isolated player to avoid modifying shared God (#1).
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HideAlready");

		// Set hidden
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@hide/on"));


		// Try to set hidden again
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@hide/on"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.AlreadyHiddenFromWho), testPlayer.DbRef)).IsTrue();
	}

	[Test, NotInParallel]
	public async ValueTask Hide_AlreadyVisible_ShowsAppropriateMessage()
	{
		// Use isolated player to avoid modifying shared God (#1).
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HideVisible");

		// Ensure unhidden
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@hide/off"));


		// Try to set visible again
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@hide/off"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.AlreadyVisibleOnWho), testPlayer.DbRef)).IsTrue();
	}

	[Test]
	public async ValueTask PurgeCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@purge"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.PurgeCompleteFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask ReadCacheCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@readcache"));

		// ReadCacheReindexing is always sent before the try/catch, so it is deterministic.
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ReadCacheReindexing), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask ShutdownCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@shutdown"));

		// No switch → else branch sends ShutdownInitiated, then ShutdownNoteWebApp is always sent.
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ShutdownInitiated), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask ShutdownRebootCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@shutdown/reboot"));

		// /reboot switch → ShutdownRebootInitiated is sent in the REBOOT branch.
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ShutdownRebootInitiated), executor, executor)).IsTrue();
	}

	[Test]
	[DependsOn(nameof(AllhaltCommand))]
	public async ValueTask ChownallCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// This test may need adjustment based on actual player setup
		await Parser.CommandParse(1, ConnectionService, MModule.single("@chownall #1"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ChownAllCompleteFormat), executor, executor)).IsTrue();
	}

	[Test]
	[DependsOn(nameof(PollCommand))]
	public async ValueTask SuggestListCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@suggest/list"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.NoSuggestionCategoriesDefined), executor, executor)).IsTrue();
	}

	[Test]
	[DependsOn(nameof(SuggestListCommand))]
	public async ValueTask SuggestAddCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@suggest/add testcat547=testword923"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.SuggestAddedWordToCategoryFormat), executor, executor)).IsTrue();
	}

	[Test]
	[DependsOn(nameof(SuggestAddCommand))]
	public async ValueTask PollSetCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@poll TestPollMessage897"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.PollMessageSet), executor, executor)).IsTrue();
	}

	[Test]
	[DependsOn(nameof(PollSetCommand))]
	public async ValueTask PollClearCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@poll/clear"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.PollMessageCleared), executor, executor)).IsTrue();
	}


}
