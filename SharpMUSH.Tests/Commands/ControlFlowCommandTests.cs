using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
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

	private static string? ExtractMessageForExecutor(object?[] args, DBRef executor)
	{
		if (args.Length < 2)
		{
			return null;
		}

		if (args[0] is not AnySharpObject who || who.Object().DBRef != executor)
		{
			return null;
		}

		if (args[1] is not OneOf<MString, string> msg)
		{
			return null;
		}

		return msg.Match(m => m.ToString(), s => s);
	}

	[Test]
	public async ValueTask SelectCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@select 1=1,@pemit #1=SelectCommand_One,@pemit #1=SelectCommand_Other"));

		// No /inline, so the matched action is a new queue entry -- poll rather than race it.
		await Assert.That(await WaitForMessage(executor, "SelectCommand_One")).IsTrue();

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("SelectCommand_Other"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask SwitchCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@switch 1=1,@pemit #1=SwitchCommand_One,@pemit #1=SwitchCommand_Other"));

		// No /inline, so the matched action is a new queue entry -- poll rather than race it.
		await Assert.That(await WaitForMessage(executor, "SwitchCommand_One")).IsTrue();

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("SwitchCommand_Other"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask BreakCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@break"));

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject?>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask AssertCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@assert 1"));

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Any<OneOf<MString, string>>(), Arg.Any<AnySharpObject?>(), Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask RetryCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@retry 1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("Nothing to retry."), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask SkipCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@skip 0=@pemit #1=SkipCommand False; @pemit #1=SkipCommand Rest"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("SkipCommand False"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("SkipCommand Rest"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask IncludeCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@include #1/ATTRIBUTE"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("No such attribute: ATTRIBUTE"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask IfElseCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@ifelse 1=@pemit #1=IfElseCommand True,@pemit #1=IfElseCommand False"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("IfElseCommand True"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Include_StripsDollarCommandPrefix()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "InclDollar");

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&INCL_DOLLAR_TEST {objDbRef}=$testcmd *:@pemit #1=IncludeDollarPrefix_Executed_71934"));

		// @include should strip the $testcmd *: prefix and execute the remainder
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@include {objDbRef}/INCL_DOLLAR_TEST"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "IncludeDollarPrefix_Executed_71934")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Include_StripsCaretListenPrefix()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "InclCaret");

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&INCL_CARET_TEST {objDbRef}=^*says*:@pemit #1=IncludeCaretPrefix_Executed_82045"));

		// @include should strip the ^*says*: prefix and execute the remainder
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@include {objDbRef}/INCL_CARET_TEST"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "IncludeCaretPrefix_Executed_82045")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Include_NoPrefix_ExecutesDirectly()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "InclNoPrefix");

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&INCL_NOPREFIX_TEST {objDbRef}=@pemit #1=IncludeNoPrefix_Executed_93156"));

		// @include should execute the attribute content as-is
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@include {objDbRef}/INCL_NOPREFIX_TEST"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "IncludeNoPrefix_Executed_93156")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Include_WithNobreakSwitch_PreventsBreakPropagation()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "InclNobreak");

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&INCL_NOBRK_TEST {objDbRef}=@pemit #1=IncludeNobreak_Before_14267;@break 1"));

		// Use @include/nobreak so @break doesn't propagate, then execute next command
		await Parser.CommandListParse(
			MarkupText.Plain($"@include/nobreak {objDbRef}/INCL_NOBRK_TEST;@pemit #1=IncludeNobreak_After_14267"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "IncludeNobreak_Before_14267")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "IncludeNobreak_After_14267")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Include_DollarPrefix_WithArguments()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "InclDollarArg");

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&INCL_DOLLARARG_TEST {objDbRef}=$testarg *:@pemit #1=IncludeDollarArg_%0_25378"));

		// @include with arguments should strip prefix and substitute %0
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@include {objDbRef}/INCL_DOLLARARG_TEST=Hello"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "IncludeDollarArg_Hello_25378")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Break_QueuedSwitch_BreaksCommandList()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var baselineNotificationCount = NotifyService.ReceivedCalls().Count();
		await Parser.CommandListParse(
			MarkupText.Plain("@pemit #1=BreakQueued_Before_36489;@break/queued 1=@pemit #1=BreakQueued_Action_36489;@pemit #1=BreakQueued_After_36489"));

		var newCalls = NotifyService.ReceivedCalls().Skip(baselineNotificationCount).ToList();
		var newMessages = newCalls
			.Where(call => call.GetMethodInfo().Name == nameof(INotifyService.Notify))
			.Select(call => call.GetArguments())
			.Select(args => ExtractMessageForExecutor(args, executor))
			.Where(message => message is not null)
			.Select(message => message!)
			.ToList();

		await Assert.That(newMessages).Contains("BreakQueued_Before_36489");
		await Assert.That(newMessages).DoesNotContain("BreakQueued_After_36489");
	}

	[Test]
	public async ValueTask Switch_FirstSwitch_OnlyRunsFirstMatch()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// @switch/first: only the first matching action fires, second match should not run.
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain("@switch/first/inline 1=1,@pemit #1=SwFirst_A_47592,1,@pemit #1=SwFirst_B_47592"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "SwFirst_A_47592")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "SwFirst_B_47592")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Switch_AllSwitch_RunsAllMatches()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// @switch (default) / @switch/all: all matching actions run.
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain("@switch/all/inline 1=1,@pemit #1=SwAll_A_58603,1,@pemit #1=SwAll_B_58603"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "SwAll_A_58603")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "SwAll_B_58603")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Switch_RegexpSwitch_MatchesRegexp()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// @switch/regexp: patterns are treated as case-insensitive regular expressions.
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain("@switch/regexp/inline hello=HEL+O,@pemit #1=SwRegexp_Match_69714,world,@pemit #1=SwRegexp_NoMatch_69714"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "SwRegexp_Match_69714")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "SwRegexp_NoMatch_69714")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Switch_RegexpSwitch_IsCaseInsensitive()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// @switch/regexp: per helpfile, matches are case-insensitive.
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain("@switch/regexp/inline HELLO=hello,@pemit #1=SwRegexpCI_Match_70825,world,@pemit #1=SwRegexpCI_NoMatch_70825"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "SwRegexpCI_Match_70825")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "SwRegexpCI_NoMatch_70825")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Switch_HashDollarSubstitution_ReplacedWithTestString()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// #$ in action text should be replaced with the test string before execution.
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain("@switch/inline hello=hel*,@pemit #1=SwHashDollar_#$_81936,nomatch,@pemit #1=SwHashDollar_NoMatch_81936"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "SwHashDollar_hello_81936")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Switch_HashDollarSubstitution_InDefaultAction()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// #$ in default action text should also be replaced with the test string.
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain("@switch/inline goodbye=hello,@pemit #1=SwHashDollarDef_NoMatch_92047,@pemit #1=SwHashDollarDef_#$_92047"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "SwHashDollarDef_goodbye_92047")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Switch_NotifySwitch_RunsActionAndQueuesNotify()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// @switch/notify: the action fires normally, and "@notify me" is queued after it,
		// releasing a task parked on the executor's semaphore.
		var id = Guid.NewGuid().ToString("N")[..8];
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@drain/all me"));

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@wait me/SEMAPHORE=@pemit #1=SwNotify_Released_{id}"));

		await Assert.That(await WaitForMessage(executor, $"SwNotify_Released_{id}", 500)).IsFalse()
			.Because("the parked task must still be waiting before @switch/notify runs");

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@switch/notify 1=1,@pemit #1=SwNotify_Match_{id},@pemit #1=SwNotify_Default_{id}"));

		await Assert.That(await WaitForMessage(executor, $"SwNotify_Match_{id}")).IsTrue();
		await Assert.That(await WaitForMessage(executor, $"SwNotify_Released_{id}")).IsTrue()
			.Because("@switch/notify queues '@notify me', which releases the waiting semaphore task");

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, $"SwNotify_Default_{id}")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask IncludeChain_RunsAllLinksInOrder_SharingRegisters()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "ChainAll");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&STEP1 {obj}=think setq(acc, a)"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&STEP2 {obj}=think setq(acc, %q<acc>b)"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&STEP3 {obj}=@pemit #1=ChainAll_%q<acc>c_71204"));

		// All three links run in order, and q-registers are shared between them (STEP2 sees STEP1's setq).
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@include/chain {obj}/STEP1 {obj}/STEP2 {obj}/STEP3"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "ChainAll_abc_71204")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask IncludeChain_BreakInLink_StopsChain()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "ChainBrk");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&S1 {obj}=@pemit #1=ChainBrk_S1_55019;@break 1"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&S2 {obj}=@pemit #1=ChainBrk_S2_55019"));

		// Default (no /nobreak): the @break in S1 short-circuits the chain, so S2 never runs.
		await Parser.CommandListParse(MarkupText.Plain($"@include/chain {obj}/S1 {obj}/S2"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "ChainBrk_S1_55019")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "ChainBrk_S2_55019")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask IncludeChain_PassesSameArgsToEveryLink()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "ChainArg");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&P1 {obj}=@pemit #1=ChainArg_P1_%0_88431"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&P2 {obj}=@pemit #1=ChainArg_P2_%0_88431"));

		// The =HELLO argument reaches every link as %0.
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@include/chain {obj}/P1 {obj}/P2=HELLO"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "ChainArg_P1_HELLO_88431")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "ChainArg_P2_HELLO_88431")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask IncludeChainNobreak_ContinuesPastBreak()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "ChainNB");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&N1 {obj}=@pemit #1=ChainNB_N1_31776;@break 1"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&N2 {obj}=@pemit #1=ChainNB_N2_31776"));

		// /nobreak confines the @break to N1, so the chain carries on to N2.
		await Parser.CommandListParse(MarkupText.Plain($"@include/chain/nobreak {obj}/N1 {obj}/N2"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "ChainNB_N1_31776")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "ChainNB_N2_31776")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask IncludeChain_SingleLinkSelfRecursion_IsCaught()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "ChainRecSelf");

		// The recursion originates in ONE link of the group: S2 includes ITSELF. That nested single-target
		// @include runs through RunOne, tracked under S2's own LongName — independent of the chain key — so
		// it hits the recursion limit and the chain RETURNS instead of looping forever.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&S1 {obj}=@pemit #1=ChainRecSelf_S1_88011"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&S2 {obj}=@pemit #1=ChainRecSelf_S2_88011;@include {obj}/S2"));

		// If single-link recursion were NOT caught, this call would never return (stack overflow / hang).
		await Parser.CommandListParse(MarkupText.Plain($"@include/chain {obj}/S1 {obj}/S2"));

		// S1 ran once (the chain executed) ...
		await NotifyService.Received(1).Notify(
			TestHelpers.MatchingObject(executor),
			Arg.Is<OneOf<MString, string>>(m => TestHelpers.MessagePlainTextEquals(m, "ChainRecSelf_S1_88011")),
			TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
		// ... and S2 genuinely recursed (its marker fired), yet the call returned — proving it was bounded.
		await NotifyService.Received().Notify(
			TestHelpers.MatchingObject(executor),
			Arg.Is<OneOf<MString, string>>(m => TestHelpers.MessagePlainTextEquals(m, "ChainRecSelf_S2_88011")),
			TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask IncludeChain_OneLinkReentersWholeChain_IsCaught()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "ChainRecWhole");

		// The recursion again originates in ONE link, but this time S2 re-runs the WHOLE chain (the same
		// target list). That is tracked under the chain's target-list key, which is identical on every
		// re-entry, so the counter climbs and the recursion limit fires — the chain RETURNS.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&S1 {obj}=@pemit #1=ChainRecWhole_S1_43307"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&S2 {obj}=@include/chain {obj}/S1 {obj}/S2"));

		await Parser.CommandListParse(MarkupText.Plain($"@include/chain {obj}/S1 {obj}/S2"));

		// S1 fired (the chain ran, repeatedly) and the call returned — proving whole-chain recursion driven
		// by a single link is bounded/caught, not infinite.
		await NotifyService.Received().Notify(
			TestHelpers.MatchingObject(executor),
			Arg.Is<OneOf<MString, string>>(m => TestHelpers.MessagePlainTextEquals(m, "ChainRecWhole_S1_43307")),
			TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	// ---- @select / @switch: PennMUSH parity (issue #958, sweep item #7) ----

	/// <summary>
	/// The eleven resource keys @SELECT used to emit on every invocation. PennMUSH's do_switch
	/// (src/predicat.c:1081) prints nothing at all, so none of these may reach a player again.
	/// Spelled out as literals: the constants they name are deleted.
	/// </summary>
	private static readonly string[] SelectDebugInstrumentationKeys =
	[
		"SelectTestingStringFormat", "SelectExpressionActionPairsFormat", "SelectHasDefaultAction",
		"SelectModeRegexp", "SelectModeWildcard", "SelectExecutionInline", "SelectNoBreakWontPropagate",
		"SelectQregistersLocalized", "SelectQregistersCleared", "SelectExecutionQueued", "SelectWillQueueNotify"
	];

	private List<string> MessagesFor(DBRef executor) =>
		NotifyService.ReceivedCalls()
			.ToList()
			.Where(call => call.GetMethodInfo().Name == nameof(INotifyService.Notify))
			.Select(call => ExtractMessageForExecutor(call.GetArguments(), executor))
			.Where(message => message is not null)
			.Select(message => message!)
			.ToList();

	/// <summary>Polls the notify mock until <paramref name="expected"/> has been delivered to the executor.</summary>
	private async Task<bool> WaitForMessage(DBRef executor, string expected, int timeoutMs = 10000)
	{
		var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
		while (DateTime.UtcNow < deadline)
		{
			if (MessagesFor(executor).Contains(expected)) return true;
			await Task.Delay(50);
		}

		return MessagesFor(executor).Contains(expected);
	}

	[Test]
	public async ValueTask Select_EmitsNoDebugInstrumentation()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var id = Guid.NewGuid().ToString("N")[..8];
		var baseline = NotifyService.ReceivedCalls().Count();

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@select/inline test=t*,@pemit #1=SelNoDebug_Match_{id},@pemit #1=SelNoDebug_Default_{id}"));

		var localizedKeys = NotifyService.ReceivedCalls()
			.ToList()
			.Skip(baseline)
			.Where(call => call.GetMethodInfo().Name is "NotifyLocalized" or "NotifyLocalizedMarkup")
			.Select(call => call.GetArguments().Length >= 2 ? call.GetArguments()[1] as string : null)
			.Where(key => key is not null)
			.Select(key => key!)
			.ToList();

		// The action itself still runs ...
		await Assert.That(MessagesFor(executor)).Contains($"SelNoDebug_Match_{id}");
		// ... but none of the development instrumentation is shipped to the player.
		await Assert.That(localizedKeys.Intersect(SelectDebugInstrumentationKeys).ToList()).IsEmpty();
	}

	[Test]
	public async ValueTask Select_Default_QueuesActionInsteadOfRunningInline()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var id = Guid.NewGuid().ToString("N")[..8];

		// PennMUSH: @select with no /inline makes the matched action a NEW queue entry
		// (do_switch -> new_queue_actionlist with QUEUE_DEFAULT). Its @break therefore cannot
		// stop the list that ran the @select.
		await Parser.CommandListParse(
			MarkupText.Plain(
				$"@select 1=1,{{@pemit #1=SelQueued_Action_{id};@break 1}};@pemit #1=SelQueued_After_{id}"));

		await Assert.That(await WaitForMessage(executor, $"SelQueued_Action_{id}")).IsTrue()
			.Because("the queued action must actually run, not park on a semaphore that is never notified");

		await Assert.That(MessagesFor(executor)).Contains($"SelQueued_After_{id}")
			.Because("a queued action is its own queue entry, so its @break cannot stop the calling list");
	}

	[Test]
	public async ValueTask Select_Inline_BreakInActionStopsTheCaller()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var id = Guid.NewGuid().ToString("N")[..8];

		// 'help @switch2': with /inline "an @break in an <action> will stop the calling action list".
		await Parser.CommandListParse(
			MarkupText.Plain(
				$"@select/inline 1=1,{{@pemit #1=SelInlineBrk_Action_{id};@break 1}};@pemit #1=SelInlineBrk_After_{id}"));

		var messages = MessagesFor(executor);
		await Assert.That(messages).Contains($"SelInlineBrk_Action_{id}");
		await Assert.That(messages).DoesNotContain($"SelInlineBrk_After_{id}");
	}

	[Test]
	public async ValueTask Select_Inline_RunsActionInPlace()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var id = Guid.NewGuid().ToString("N")[..8];

		await Parser.CommandListParse(
			MarkupText.Plain($"@select/inline 1=1,@pemit #1=SelInline_Action_{id};@pemit #1=SelInline_After_{id}"));

		var messages = MessagesFor(executor);
		await Assert.That(messages).Contains($"SelInline_Action_{id}");
		await Assert.That(messages.IndexOf($"SelInline_Action_{id}"))
			.IsLessThan(messages.IndexOf($"SelInline_After_{id}"))
			.Because("/inline runs the action in place, before the rest of the calling action list");
	}

	[Test]
	public async ValueTask Select_NotifySwitch_ActuallyQueuesTheNotify()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var id = Guid.NewGuid().ToString("N")[..8];

		// Clear any semaphore task another test parked on the executor, and reset the count, so the
		// single '@notify me' this queues can only release the task parked below.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@drain/all me"));

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@wait me/SEMAPHORE=@pemit #1=SelNotify_Released_{id}"));

		await Assert.That(await WaitForMessage(executor, $"SelNotify_Released_{id}", 500)).IsFalse()
			.Because("the parked task must still be waiting before @select/notify runs");

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@select/notify 1=1,@pemit #1=SelNotify_Match_{id}"));

		await Assert.That(await WaitForMessage(executor, $"SelNotify_Released_{id}")).IsTrue()
			.Because("@select/notify queues '@notify me', which releases the waiting semaphore task");
	}

	[Test]
	public async ValueTask Switch_Default_QueuesActionInsteadOfRunningInline()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var id = Guid.NewGuid().ToString("N")[..8];

		// 'help @switch4' contrasts "@switch %0=1,think one ; think after" (before / after / one)
		// with the /inline form. The ordering itself races this engine's concurrent queue consumer,
		// so assert the property that does not: a new queue entry's @break cannot reach the caller.
		await Parser.CommandListParse(
			MarkupText.Plain(
				$"@switch 1=1,{{@pemit #1=SwQueued_Action_{id};@break 1}};@pemit #1=SwQueued_After_{id}"));

		await Assert.That(await WaitForMessage(executor, $"SwQueued_Action_{id}")).IsTrue();

		await Assert.That(MessagesFor(executor)).Contains($"SwQueued_After_{id}")
			.Because("@switch without /inline queues its actions as new queue entries");
	}

	[Test]
	public async ValueTask Switch_Inline_BreakInActionStopsTheCaller()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var id = Guid.NewGuid().ToString("N")[..8];

		// 'help @switch2': with /inline "an @break in an <action> will stop the calling action list
		// (and any further <action>s) from running".
		await Parser.CommandListParse(
			MarkupText.Plain(
				$"@switch/inline 1=1,{{@pemit #1=SwInlineBrk_Action_{id};@break 1}};@pemit #1=SwInlineBrk_After_{id}"));

		var messages = MessagesFor(executor);
		await Assert.That(messages).Contains($"SwInlineBrk_Action_{id}");
		await Assert.That(messages).DoesNotContain($"SwInlineBrk_After_{id}");
	}

	[Test]
	public async ValueTask Switch_Inline_RunsActionInPlace()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var id = Guid.NewGuid().ToString("N")[..8];

		// 'help @switch4': "@switch/inline %0=1,think one ; think after" prints before / one / after.
		await Parser.CommandListParse(
			MarkupText.Plain($"@switch/inline 1=1,@pemit #1=SwInline_Action_{id};@pemit #1=SwInline_After_{id}"));

		var messages = MessagesFor(executor);
		await Assert.That(messages).Contains($"SwInline_Action_{id}");
		await Assert.That(messages.IndexOf($"SwInline_Action_{id}"))
			.IsLessThan(messages.IndexOf($"SwInline_After_{id}"))
			.Because("/inline runs the action in place");
	}

	[Test]
	public async ValueTask Switch_Inplace_IsInlineNobreakLocalize()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var id = Guid.NewGuid().ToString("N")[..8];

		// 'help @switch2': "@switch/inplace is an alias for @switch/inline/nobreak/localize."
		// The @break inside the action must NOT stop the calling action list, and the setq
		// inside it must not survive the switch.
		await Parser.CommandListParse(
			MarkupText.Plain(
				$"@switch/inplace 1=1,{{@pemit #1=SwInplace_Action_{id};think setq(sw,{id});@break 1}};@pemit #1=SwInplace_After_%q<sw>_{id}"));

		var messages = MessagesFor(executor);
		await Assert.That(messages).Contains($"SwInplace_Action_{id}");
		await Assert.That(messages).Contains($"SwInplace_After__{id}")
			.Because("/inplace implies /nobreak (the caller keeps running) and /localize (q-registers are restored)");
	}
}
