using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
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

	/// <summary>
	/// Everything <paramref name="who"/> was notified of while <paramref name="action"/> ran, in
	/// order. The <see cref="INotifyService"/> substitute is shared across the whole test session,
	/// so a bare <c>DidNotReceive()</c> asserts that the recipient was never sent that message by
	/// ANY test - which makes the assertion pass or fail on what else happened to run. Windowing
	/// through the recipient-keyed recorder scopes it to this command.
	/// </summary>
	private async Task<List<string>> MessagesWhile(DBRef who, Func<Task> action)
	{
		var recorder = WebAppFactoryArg.Notifications;
		var before = recorder.CountFor(who);
		await action();
		return [.. recorder.For(who).Skip(before)];
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask HaltCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Create a unique thing to halt, instead of halting shared God (#1).
		var thingDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "HaltTarget");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@halt {thingDbRef}"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("Halted God and all their objects."), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask AllhaltCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@allhalt"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.AllObjectsHaltedWithCountFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask DrainCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;

		var messages = await MessagesWhile(executor, () =>
			Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@drain #1")).AsTask());

		await Assert.That(messages.Any(m => m.StartsWith("#-1"))).IsFalse()
			.Because("@drain must not report an error return code");
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask PsCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@ps"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "@ps")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask PsWithTarget()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@ps #1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "@ps")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask TriggerCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("&TRIGGER_TEST_WIZ_UNIQUE #1=think Triggered!"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@trigger #1/TRIGGER_TEST_WIZ_UNIQUE"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("Triggered!"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask ForceCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Pattern A: embed unique token so the think output is globally unique in the session.
		var token = TestIsolationHelpers.GenerateUniqueName("Forced");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@force #1=think {token}"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, token)),
				TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
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

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@force me=&{attrName} me=[add(1,1)]"));

		var result = await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"think [get(me/{attrName})]"));

		var attrValue = result.Message?.ToPlainText()?.Trim() ?? "";
		await Assert.That(attrValue).IsEqualTo("2")
			.Because("@force should evaluate [add(1,1)] to 2 before the & command stores it");
	}

	/// <summary>
	/// Regression test for the command-argument subtree-reuse optimization (ArgumentSplit /
	/// SharpMUSHParserVisitor.EvaluateArgumentSubtree): @FORCE is EqSplit + RSBrace WITHOUT
	/// NoParse/RSNoParse, so its RHS is both (a) brace-preserved by the NoParse boundary-finding
	/// pass (CommandBehavior.RSBrace -> ParserStateFlags.PreserveBraces) and (b) eagerly
	/// function-evaluated afterwards — exercising the brace-preservation and subtree-reuse paths
	/// together in the same argument.
	/// </summary>
	[Test]
	public async ValueTask ForceCommand_PreservesBraces_WhileEvaluatingFunctionInside()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain("@force me={@pemit me=[add(1,2)]}"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("3"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask NotifyCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@notify #1"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.Notified), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask WaitCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;

		// Note: This test doesn't verify the wait actually happened, just that the command executed
		var messages = await MessagesWhile(executor, () =>
			Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@wait 1=think Waited")).AsTask());

		await Assert.That(messages.Any(m => m.StartsWith("#-1"))).IsFalse()
			.Because("@wait must not report an error return code");
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
		var testObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WaitEvalWiz");
		var uniqueId = Guid.NewGuid().ToString("N");
		var attrName = $"WIZWAIT_{uniqueId[..8].ToUpper()}";

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@wait 1={{&{attrName} {testObj}=[add(1,1)]}}"));

		// Poll until the @wait callback sets the attribute (or 10s timeout).
		// Polling replaces a fixed Task.Delay so the test isn't fragile against
		var obj = await Mediator.Send(new GetObjectNodeQuery(testObj));
		await TestHelpers.WaitForAttribute(AttributeService, obj.Known, attrName, 10000);

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
		var testObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WaitArgObj");
		var uniqueId = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var resultAttr = $"RESULT_{uniqueId}";

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&CMD_TEST_{uniqueId} {testObj}=$testcmd_{uniqueId} *:@wait 1={{&{resultAttr} {testObj}=%0}}"));

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"testcmd_{uniqueId} hello_world"));

		// Poll until the @wait callback sets the attribute (or 10s timeout).
		// Polling replaces a fixed Task.Delay so the test isn't fragile against
		var obj = await Mediator.Send(new GetObjectNodeQuery(testObj));
		await TestHelpers.WaitForAttribute(AttributeService, obj.Known, resultAttr, 10000);

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
		// Pattern C: unique receiver (non-wizard) isolates the notification — content is dynamic timestamps.
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "UptimeTest");

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@uptime"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf.OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextContains(msg, "SharpMUSH Uptime:")),
				TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask DbckCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@dbck"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("Not Supported for SharpMUSH."), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask DumpCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@dump"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("Dump command does nothing for SharpMUSH. Consider using @backup."), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask QuotaCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@quota #1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("The quota system is disabled on this server."), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask AllquotaCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@allquota"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("Usage: @allquota <amount>"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask BootCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@boot #1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("That player is not connected."), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask WallCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Pattern A: embed unique token so the wall output is session-unique.
		var token = TestIsolationHelpers.GenerateUniqueName("Wall");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@wall {token}"));

		await NotifyService
			.Received(1)
			.Notify(executor.Number, Arg.Is<OneOf.OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, $"Announcement: {token}")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask WizwallCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Pattern A: embed unique token so the wizwall output is session-unique.
		var token = TestIsolationHelpers.GenerateUniqueName("Wizwall");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@wizwall {token}"));

		await NotifyService
			.Received(1)
			.Notify(executor.Number, Arg.Is<OneOf.OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, $"Broadcast: {token}")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[DependsOn(nameof(ReadCacheCommand))]
	public async ValueTask PollCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@poll WizardPollDisplay999"));
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.PollMessageSet), executor, executor)).IsTrue();

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@poll"));
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.PollCurrentMessageFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Hide_NoSwitch_TogglesHidden()
	{
		// Use isolated player to avoid modifying shared God (#1).
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HideToggle");
		// @hide is permission-gated (CanHide: wizard/royalty or the Hide power) - grant WIZARD.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {testPlayer.DbRef}=WIZARD"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.NoLongerAppearOnWho), testPlayer.DbRef)).IsTrue();



		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.NowAppearOnWho), testPlayer.DbRef)).IsTrue();
	}

	[Test]
	public async ValueTask Hide_YesSwitch_SetsHidden()
	{
		// Use isolated player to avoid modifying shared God (#1).
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HideYes");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {testPlayer.DbRef}=WIZARD"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide/off"));


		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide/yes"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.NoLongerAppearOnWho), testPlayer.DbRef)).IsTrue();
	}

	[Test]
	public async ValueTask Hide_OnSwitch_SetsHidden()
	{
		// Use isolated player to avoid modifying shared God (#1).
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HideOn");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {testPlayer.DbRef}=WIZARD"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide/off"));


		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide/on"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.NoLongerAppearOnWho), testPlayer.DbRef)).IsTrue();
	}

	[Test]
	public async ValueTask Hide_NoSwitch_UnsetsHidden()
	{
		// Use isolated player to avoid modifying shared God (#1).
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HideNo");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {testPlayer.DbRef}=WIZARD"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide/on"));


		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide/no"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.NowAppearOnWho), testPlayer.DbRef)).IsTrue();
	}

	[Test]
	public async ValueTask Hide_OffSwitch_UnsetsHidden()
	{
		// Use isolated player to avoid modifying shared God (#1).
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HideOff");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {testPlayer.DbRef}=WIZARD"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide/on"));


		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide/off"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.NowAppearOnWho), testPlayer.DbRef)).IsTrue();
	}

	// PennMUSH's hide_player has no "already hidden/visible" branch for the no-target case: it
	// unconditionally re-applies the requested state to every connection and re-sends the same
	// notify (bsd.c:7234-7250). Repeating /on (or /off) just repeats the same message.
	[Test, NotInParallel]
	public async ValueTask Hide_OnSwitch_Repeated_StillNotifiesHidden()
	{
		// Use isolated player to avoid modifying shared God (#1).
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HideAlready");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {testPlayer.DbRef}=WIZARD"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide/on"));


		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide/on"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.NoLongerAppearOnWho), testPlayer.DbRef)).IsTrue();
		await Assert.That(ConnectionService.Get(testPlayer.Handle)?.IsHidden).IsTrue();
	}

	[Test, NotInParallel]
	public async ValueTask Hide_OffSwitch_Repeated_StillNotifiesVisible()
	{
		// Use isolated player to avoid modifying shared God (#1).
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HideVisible");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {testPlayer.DbRef}=WIZARD"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide/off"));


		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide/off"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.NowAppearOnWho), testPlayer.DbRef)).IsTrue();
		await Assert.That(ConnectionService.Get(testPlayer.Handle)?.IsHidden).IsFalse();
	}

	[Test]
	public async ValueTask PurgeCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@purge"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.PurgeComplete), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask ReadCacheCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@readcache"));

		// ReadCacheReindexing is always sent before the try/catch, so it is deterministic.
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ReadCacheReindexing), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask ShutdownCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@shutdown"));

		// No switch → else branch sends ShutdownInitiated, then ShutdownNoteWebApp is always sent.
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ShutdownInitiated), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask ShutdownRebootCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@shutdown/reboot"));

		// /reboot switch → ShutdownRebootInitiated is sent in the REBOOT branch.
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ShutdownRebootInitiated), executor, executor)).IsTrue();
	}

	[Test]
	[DependsOn(nameof(AllhaltCommand))]
	public async ValueTask ChownallCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;

		// Chown a throwaway player's objects instead of God's (#1). @chownall on a player
		// without /preserve strips WIZARD/ROYALTY and sets HALT on every swept object; God
		// owns the shared system handlers (#8 HTTP Handler, #9 Event Handler), so
		// "@chownall #1" would de-wizard and HALT them and — because the test DB is shared
		// PerTestSession — poison those handlers for every later test in the run.
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ChownallOwner");
		var thingName = TestIsolationHelpers.GenerateUniqueName("ChownallThing");
		await Parser.CommandParse(owner.Handle, ConnectionService, MarkupText.Plain($"@create {thingName}"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chownall #{owner.DbRef.Number}"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ChownAllCompleteFormat), executor, executor)).IsTrue();
	}

	[Test]
	[DependsOn(nameof(PollCommand))]
	public async ValueTask SuggestListCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@suggest/list"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.NoSuggestionCategoriesDefined), executor, executor)).IsTrue();
	}

	[Test]
	[DependsOn(nameof(SuggestListCommand))]
	public async ValueTask SuggestAddCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@suggest/add testcat547=testword923"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.SuggestAddedWordToCategoryFormat), executor, executor)).IsTrue();
	}

	[Test]
	[DependsOn(nameof(SuggestAddCommand))]
	public async ValueTask PollSetCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@poll TestPollMessage897"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.PollMessageSet), executor, executor)).IsTrue();
	}

	[Test]
	[DependsOn(nameof(PollSetCommand))]
	public async ValueTask PollClearCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@poll/clear"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.PollMessageCleared), executor, executor)).IsTrue();
	}


}
