using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class ControlFlowCommandTests
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

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask SelectCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("SelCom");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@select 1=1,@pemit {testPlayer.DbRef}=One,@pemit {testPlayer.DbRef}=Other"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}

	[Test]
	public async ValueTask SwitchCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("SwiCom");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@switch 1=1,@pemit {testPlayer.DbRef}=One,@pemit {testPlayer.DbRef}=Other"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), "One", Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask BreakCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("BreCom");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@break"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask AssertCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("AssCom");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@assert 1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask RetryCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("RetCom");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@retry 1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}

	[Test]
	public async ValueTask SkipCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("SkiCom");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@skip 0=@pemit {testPlayer.DbRef}=SkipCommand False; @pemit {testPlayer.DbRef}=SkipCommand Rest"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), "SkipCommand False", Arg.Any<AnySharpObject>());

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), "SkipCommand Rest", Arg.Any<AnySharpObject>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask IncludeCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("IncCom");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@include {testPlayer.DbRef}/ATTRIBUTE"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<string>());
	}

	[Test]
	public async ValueTask IfElseCommand()
	{
		var testPlayer = await CreateTestPlayerAsync("IfElsCom");
		var executor = testPlayer.DbRef;
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@ifelse 1=@pemit {testPlayer.DbRef}=IfElseCommand True,@pemit {testPlayer.DbRef}=IfElseCommand False"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), "IfElseCommand True", Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Include_StripsDollarCommandPrefix()
	{
		var testPlayer = await CreateTestPlayerAsync("IncStrDolCom");
		var executor = testPlayer.DbRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "InclDollar");

		// Set an attribute with a $...: command prefix pattern
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"&INCL_DOLLAR_TEST {objDbRef}=$testcmd *:@pemit #1=IncludeDollarPrefix_Executed_71934"));

		// @include should strip the $testcmd *: prefix and execute the remainder
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@include {objDbRef}/INCL_DOLLAR_TEST"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "IncludeDollarPrefix_Executed_71934")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Include_StripsCaretListenPrefix()
	{
		var testPlayer = await CreateTestPlayerAsync("IncStrCarLis");
		var executor = testPlayer.DbRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "InclCaret");

		// Set an attribute with a ^...: listen prefix pattern
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"&INCL_CARET_TEST {objDbRef}=^*says*:@pemit #1=IncludeCaretPrefix_Executed_82045"));

		// @include should strip the ^*says*: prefix and execute the remainder
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@include {objDbRef}/INCL_CARET_TEST"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "IncludeCaretPrefix_Executed_82045")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Include_NoPrefix_ExecutesDirectly()
	{
		var testPlayer = await CreateTestPlayerAsync("IncNoPreExe");
		var executor = testPlayer.DbRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "InclNoPrefix");

		// Set an attribute without any prefix pattern
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"&INCL_NOPREFIX_TEST {objDbRef}=@pemit #1=IncludeNoPrefix_Executed_93156"));

		// @include should execute the attribute content as-is
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@include {objDbRef}/INCL_NOPREFIX_TEST"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "IncludeNoPrefix_Executed_93156")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Include_WithNobreakSwitch_PreventsBreakPropagation()
	{
		var testPlayer = await CreateTestPlayerAsync("IncWitNobSwi");
		var executor = testPlayer.DbRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "InclNobreak");

		// Set an attribute that includes an @break
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"&INCL_NOBRK_TEST {objDbRef}=@pemit #1=IncludeNobreak_Before_14267;@break 1"));

		// Use @include/nobreak so @break doesn't propagate, then execute next command
		await Parser.CommandListParse(
			MModule.single($"@include/nobreak {objDbRef}/INCL_NOBRK_TEST;@pemit #1=IncludeNobreak_After_14267"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "IncludeNobreak_Before_14267")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);

		// The command after @include/nobreak should still execute
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "IncludeNobreak_After_14267")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Include_DollarPrefix_WithArguments()
	{
		var testPlayer = await CreateTestPlayerAsync("IncDolPreWit");
		var executor = testPlayer.DbRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "InclDollarArg");

		// Set an attribute with a $...: prefix that uses %0
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"&INCL_DOLLARARG_TEST {objDbRef}=$testarg *:@pemit #1=IncludeDollarArg_%0_25378"));

		// @include with arguments should strip prefix and substitute %0
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@include {objDbRef}/INCL_DOLLARARG_TEST=Hello"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "IncludeDollarArg_Hello_25378")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Break_QueuedSwitch_BreaksCommandList()
	{
		var testPlayer = await CreateTestPlayerAsync("BreQueSwiBre");
		var executor = testPlayer.DbRef;
		// Test that @break/queued with a truthy condition still breaks the command list
		await Parser.CommandListParse(
			MModule.single($"@pemit {testPlayer.DbRef}=BreakQueued_Before_36489;@break/queued 1=@pemit {testPlayer.DbRef}=BreakQueued_Action_36489;@pemit {testPlayer.DbRef}=BreakQueued_After_36489"));

		// The command before @break should execute
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "BreakQueued_Before_36489")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);

		// The command after @break should NOT execute (break stops the list)
		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "BreakQueued_After_36489")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Switch_FirstSwitch_OnlyRunsFirstMatch()
	{
		var testPlayer = await CreateTestPlayerAsync("SwiFirSwiOnl");
		var executor = testPlayer.DbRef;
		// @switch/first: only the first matching action fires, second match should not run.
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@switch/first 1=1,@pemit {testPlayer.DbRef}=SwFirst_A_47592,1,@pemit {testPlayer.DbRef}=SwFirst_B_47592"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "SwFirst_A_47592")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "SwFirst_B_47592")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Switch_AllSwitch_RunsAllMatches()
	{
		var testPlayer = await CreateTestPlayerAsync("SwiAllSwiRun");
		var executor = testPlayer.DbRef;
		// @switch (default) / @switch/all: all matching actions run.
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@switch/all 1=1,@pemit {testPlayer.DbRef}=SwAll_A_58603,1,@pemit {testPlayer.DbRef}=SwAll_B_58603"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "SwAll_A_58603")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "SwAll_B_58603")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Switch_RegexpSwitch_MatchesRegexp()
	{
		var testPlayer = await CreateTestPlayerAsync("SwiRegSwiMat");
		var executor = testPlayer.DbRef;
		// @switch/regexp: patterns are treated as case-insensitive regular expressions.
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@switch/regexp hello=HEL+O,@pemit {testPlayer.DbRef}=SwRegexp_Match_69714,world,@pemit {testPlayer.DbRef}=SwRegexp_NoMatch_69714"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "SwRegexp_Match_69714")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "SwRegexp_NoMatch_69714")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Switch_RegexpSwitch_IsCaseInsensitive()
	{
		var testPlayer = await CreateTestPlayerAsync("SwiRegSwiIs");
		var executor = testPlayer.DbRef;
		// @switch/regexp: per helpfile, matches are case-insensitive.
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@switch/regexp HELLO=hello,@pemit {testPlayer.DbRef}=SwRegexpCI_Match_70825,world,@pemit {testPlayer.DbRef}=SwRegexpCI_NoMatch_70825"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "SwRegexpCI_Match_70825")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "SwRegexpCI_NoMatch_70825")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Switch_HashDollarSubstitution_ReplacedWithTestString()
	{
		var testPlayer = await CreateTestPlayerAsync("SwiHasDolSub");
		var executor = testPlayer.DbRef;
		// #$ in action text should be replaced with the test string before execution.
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@switch hello=hel*,@pemit {testPlayer.DbRef}=SwHashDollar_#$_81936,nomatch,@pemit {testPlayer.DbRef}=SwHashDollar_NoMatch_81936"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "SwHashDollar_hello_81936")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Switch_HashDollarSubstitution_InDefaultAction()
	{
		var testPlayer = await CreateTestPlayerAsync("SwiHasDolSub");
		var executor = testPlayer.DbRef;
		// #$ in default action text should also be replaced with the test string.
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@switch goodbye=hello,@pemit {testPlayer.DbRef}=SwHashDollarDef_NoMatch_92047,@pemit {testPlayer.DbRef}=SwHashDollarDef_#$_92047"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "SwHashDollarDef_goodbye_92047")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Switch_NotifySwitch_RunsActionAndQueuesNotify()
	{
		var testPlayer = await CreateTestPlayerAsync("SwiNotSwiRun");
		var executor = testPlayer.DbRef;
		// @switch/notify: action fires normally; @notify me is also queued.
		// Verify the action itself executes correctly.
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@switch/notify 1=1,@pemit {testPlayer.DbRef}=SwNotify_Match_93158,@pemit {testPlayer.DbRef}=SwNotify_Default_93158"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "SwNotify_Match_93158")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "SwNotify_Default_93158")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}
}
