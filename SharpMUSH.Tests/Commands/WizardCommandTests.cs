using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

using SharpMUSH.Tests;

namespace SharpMUSH.Tests.Commands;

public class WizardCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private Task<TestIsolationHelpers.TestPlayer> CreateTestPlayerAsync(string namePrefix) =>
		TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, namePrefix);
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask HaltCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("HalCom");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@halt {testPlayer.DbRef}"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}

	[Test]
	public async ValueTask AllhaltCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("AllCom");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@allhalt"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "All objects halted")));
	}

	[Test]
	public async ValueTask DrainCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("DraCom");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@drain {testPlayer.DbRef}"));

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "#-1")));
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask PsCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("PsCom");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@ps"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask PsWithTarget()
	{
		var testPlayer = await CreateTestPlayerAsync("PsWitTar");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@ps {testPlayer.DbRef}"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask TriggerCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("TriCom");
		var executor = testPlayer.DbRef;
		// Set an attribute first
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"&TRIGGER_TEST_WIZ_UNIQUE {testPlayer.DbRef}=think Triggered!"));

		// Trigger it
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@trigger {testPlayer.DbRef}/TRIGGER_TEST_WIZ_UNIQUE"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}

	[Test]
	public async ValueTask ForceCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("ForCom");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@force {testPlayer.DbRef}=think Forced!"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<OneOf.OneOf<MString, string>>());
	}

	/// <summary>
	/// Verifies that @force evaluates functions inside &amp;attr obj=value commands.
	/// <c>@force me=&amp;testattr me=[add(1,1)]</c> should set the attribute to "2" (evaluated),
	/// not the literal string "[add(1,1)]".
	/// </summary>
	[Test]
	public async ValueTask ForceCommand_EvaluatesAmpersandAttrValue()
	{
		var testPlayer = await CreateTestPlayerAsync("ForComEvaAmp");
		var attrName = $"FORCEEVAL_{Guid.NewGuid():N}"[..20];

		// Use @force to set an attribute with a function call as the value
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@force me=&{attrName} me=[add(1,1)]"));

		// Read back the attribute value using think [get()]
		var result = await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"think [get(me/{attrName})]"));

		var attrValue = result.Message?.ToPlainText()?.Trim() ?? "";
		await Assert.That(attrValue).IsEqualTo("2")
			.Because("@force should evaluate [add(1,1)] to 2 before the & command stores it");
	}

	[Test]
	public async ValueTask NotifyCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("NotCom");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@notify {testPlayer.DbRef}"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Notified")));
	}

	[Test]
	public async ValueTask WaitCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("WaiCom");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@wait 1=think Waited"));

		// Note: This test doesn't verify the wait actually happened, just that the command executed
		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "#-1")));
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
		var testPlayer = await CreateTestPlayerAsync("WaiComEvaAmp");
		// Arrange - create an isolated test object with a unique attribute name
		var testObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WaitEvalWiz");
		var uniqueId = Guid.NewGuid().ToString("N");
		var attrName = $"WIZWAIT_{uniqueId[..8].ToUpper()}";

		// Act - queue @wait with [add(1,1)] inside a & attribute-set command after 1s
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
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
		var testPlayer = await CreateTestPlayerAsync("WaiComPrePat");
		// Create a test object with a $command that uses @wait to store %0
		var testObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WaitArgObj");
		var uniqueId = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var resultAttr = $"RESULT_{uniqueId}";

		// Set up a $command pattern: when triggered, stores %0 via @wait callback
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"&CMD_TEST_{uniqueId} {testObj}=$testcmd_{uniqueId} *:@wait 1={{&{resultAttr} {testObj}=%0}}"));

		// Trigger the $command — %0 should be "hello_world"
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
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
		var testPlayer = await CreateTestPlayerAsync("UptCom");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@uptime"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<OneOf.OneOf<MString, string>>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask DbckCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("DbcCom");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@dbck"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask DumpCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("DumCom");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@dump"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask QuotaCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("QuoCom");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@quota {testPlayer.DbRef}"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask AllquotaCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("AllCom");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@allquota"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask BootCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("BooCom");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@boot {testPlayer.DbRef}"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}

	[Test]
	public async ValueTask WallCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("WalCom");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@wall Test wall message"));

		await NotifyService
			.Received()
			.Notify(executor.Number, Arg.Any<OneOf.OneOf<MString, string>>());
	}

	[Test]
	public async ValueTask WizwallCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("WizCom");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@wizwall Test wizwall message"));

		await NotifyService
			.Received()
			.Notify(executor.Number, Arg.Any<OneOf.OneOf<MString, string>>());
	}

	[Test]
	[DependsOn(nameof(ReadCacheCommand))]
	public async ValueTask PollCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("PolCom");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@poll"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "poll") || TestHelpers.MessageContains(s, "Poll")));
	}

	[Test]
	public async ValueTask Hide_NoSwitch_TogglesHidden()
	{
		var testPlayer = await CreateTestPlayerAsync("HidNoSwiTog");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		var executor = testPlayer.DbRef;
		// Test that @hide without switches toggles the DARK flag


		// First call should hide (set DARK)
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@hide"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "now hidden")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());



		// Second call should unhide (unset DARK)
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@hide"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "no longer hidden") || TestHelpers.MessageContains(s, "visible")),
				Arg.Any<AnySharpObject>(),
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	public async ValueTask Hide_YesSwitch_SetsHidden()
	{
		var testPlayer = await CreateTestPlayerAsync("HidYesSwiSet");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		var executor = testPlayer.DbRef;
		// Test that @hide/yes sets the DARK flag


		// Ensure we start unhidden (call @hide/off first)
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@hide/off"));


		// Now test @hide/yes
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@hide/yes"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "hidden")));
	}

	[Test]
	public async ValueTask Hide_OnSwitch_SetsHidden()
	{
		var testPlayer = await CreateTestPlayerAsync("HidOnSwiSet");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		var executor = testPlayer.DbRef;
		// Test that @hide/on sets the DARK flag


		// Ensure we start unhidden
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@hide/off"));


		// Now test @hide/on
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@hide/on"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s
				=> s.Value.ToString()!.Contains("hidden")));
	}

	[Test]
	public async ValueTask Hide_NoSwitch_UnsetsHidden()
	{
		var testPlayer = await CreateTestPlayerAsync("HidNoSwiUns");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		var executor = testPlayer.DbRef;
		// Test that @hide/no unsets the DARK flag


		// Ensure we start hidden
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@hide/on"));


		// Now test @hide/no
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@hide/no"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s
				=> TestHelpers.MessageContains(s, "no longer hidden") || TestHelpers.MessageContains(s, "visible")));
	}

	[Test]
	public async ValueTask Hide_OffSwitch_UnsetsHidden()
	{
		var testPlayer = await CreateTestPlayerAsync("HidOffSwiUns");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		var executor = testPlayer.DbRef;
		// Test that @hide/off unsets the DARK flag


		// Ensure we start hidden
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@hide/on"));


		// Now test @hide/off
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@hide/off"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s
				=> TestHelpers.MessageContains(s, "no longer hidden") || TestHelpers.MessageContains(s, "visible")));
	}

	[Test, NotInParallel]
	public async ValueTask Hide_AlreadyHidden_ShowsAppropriateMessage()
	{
		var testPlayer = await CreateTestPlayerAsync("HidAlrHidSho");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		var executor = testPlayer.DbRef;
		// Test that @hide/on when already hidden shows appropriate message


		// Set hidden
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@hide/on"));


		// Try to set hidden again
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@hide/on"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s
				=> s.Value.ToString()!.Contains("already hidden")));
	}

	[Test, NotInParallel]
	public async ValueTask Hide_AlreadyVisible_ShowsAppropriateMessage()
	{
		var testPlayer = await CreateTestPlayerAsync("HidAlrVisSho");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		var executor = testPlayer.DbRef;
		// Test that @hide/off when already visible shows appropriate message


		// Ensure unhidden
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@hide/off"));


		// Try to set visible again
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@hide/off"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s
				=> s.Value.ToString()!.Contains("already visible")));
	}

	[Test]
	public async ValueTask PurgeCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("PurCom");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@purge"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Purge complete") || TestHelpers.MessageContains(s, "GOING_TWICE")));
	}

	[Test]
	public async ValueTask ReadCacheCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("ReaCacCom");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@readcache"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Reindexing text files") || TestHelpers.MessageContains(s, "Text file cache rebuilt")));
	}

	[Test]
	public async ValueTask ShutdownCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("ShuCom");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@shutdown"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "SHUTDOWN") || TestHelpers.MessageContains(s, "web") || TestHelpers.MessageContains(s, "orchestration")));
	}

	[Test]
	public async ValueTask ShutdownRebootCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("ShuRebCom");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@shutdown/reboot"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "REBOOT") || TestHelpers.MessageContains(s, "web") || TestHelpers.MessageContains(s, "orchestration")));
	}

	[Test]
	[DependsOn(nameof(AllhaltCommand))]
	public async ValueTask ChownallCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("ChoCom");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		var executor = testPlayer.DbRef;
		// This test may need adjustment based on actual player setup
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@chownall {testPlayer.DbRef}"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Permission denied") || TestHelpers.MessageContains(s, "objects") || TestHelpers.MessageContains(s, "ownership")));
	}

	[Test]
	[DependsOn(nameof(PollCommand))]
	public async ValueTask SuggestListCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("SugLisCom");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@suggest/list"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Suggestion categories") || TestHelpers.MessageContains(s, "No suggestion categories")));
	}

	[Test]
	[DependsOn(nameof(SuggestListCommand))]
	public async ValueTask SuggestAddCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("SugAddCom");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@suggest/add testcat547=testword923"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Added 'testword923' to category 'testcat547'")));
	}

	[Test]
	[DependsOn(nameof(SuggestAddCommand))]
	public async ValueTask PollSetCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("PolSetCom");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@poll TestPollMessage897"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Poll message set") || TestHelpers.MessageContains(s, "Permission")));
	}

	[Test]
	[DependsOn(nameof(PollSetCommand))]
	public async ValueTask PollClearCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("PolCleCom");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@poll/clear"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessageContains(s, "Poll message cleared") || TestHelpers.MessageContains(s, "Permission")));
	}


}
