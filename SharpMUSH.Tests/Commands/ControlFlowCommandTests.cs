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

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask SelectCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@select 1=1,@pemit #1=One,@pemit #1=Other"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), "One", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask SwitchCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@switch 1=1,@pemit #1=One,@pemit #1=Other"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), "One", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask BreakCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@break"));

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
		await Parser.CommandParse(1, ConnectionService, MModule.single("@assert 1"));

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
		await Parser.CommandParse(1, ConnectionService, MModule.single("@retry 1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), "Nothing to retry.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask SkipCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@skip 0=@pemit #1=SkipCommand False; @pemit #1=SkipCommand Rest"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), "SkipCommand False", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), "SkipCommand Rest", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask IncludeCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@include #1/ATTRIBUTE"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), "No such attribute: ATTRIBUTE", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask IfElseCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@ifelse 1=@pemit #1=IfElseCommand True,@pemit #1=IfElseCommand False"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(TestHelpers.MatchingObject(executor), "IfElseCommand True", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Include_StripsDollarCommandPrefix()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "InclDollar");

		// Set an attribute with a $...: command prefix pattern
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&INCL_DOLLAR_TEST {objDbRef}=$testcmd *:@pemit #1=IncludeDollarPrefix_Executed_71934"));

		// @include should strip the $testcmd *: prefix and execute the remainder
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@include {objDbRef}/INCL_DOLLAR_TEST"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "IncludeDollarPrefix_Executed_71934")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Include_StripsCaretListenPrefix()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "InclCaret");

		// Set an attribute with a ^...: listen prefix pattern
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&INCL_CARET_TEST {objDbRef}=^*says*:@pemit #1=IncludeCaretPrefix_Executed_82045"));

		// @include should strip the ^*says*: prefix and execute the remainder
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@include {objDbRef}/INCL_CARET_TEST"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "IncludeCaretPrefix_Executed_82045")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Include_NoPrefix_ExecutesDirectly()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "InclNoPrefix");

		// Set an attribute without any prefix pattern
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&INCL_NOPREFIX_TEST {objDbRef}=@pemit #1=IncludeNoPrefix_Executed_93156"));

		// @include should execute the attribute content as-is
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@include {objDbRef}/INCL_NOPREFIX_TEST"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "IncludeNoPrefix_Executed_93156")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Include_WithNobreakSwitch_PreventsBreakPropagation()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "InclNobreak");

		// Set an attribute that includes an @break
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&INCL_NOBRK_TEST {objDbRef}=@pemit #1=IncludeNobreak_Before_14267;@break 1"));

		// Use @include/nobreak so @break doesn't propagate, then execute next command
		await Parser.CommandListParse(
			MModule.single($"@include/nobreak {objDbRef}/INCL_NOBRK_TEST;@pemit #1=IncludeNobreak_After_14267"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "IncludeNobreak_Before_14267")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		// The command after @include/nobreak should still execute
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "IncludeNobreak_After_14267")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Include_DollarPrefix_WithArguments()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "InclDollarArg");

		// Set an attribute with a $...: prefix that uses %0
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"&INCL_DOLLARARG_TEST {objDbRef}=$testarg *:@pemit #1=IncludeDollarArg_%0_25378"));

		// @include with arguments should strip prefix and substitute %0
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@include {objDbRef}/INCL_DOLLARARG_TEST=Hello"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "IncludeDollarArg_Hello_25378")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Break_QueuedSwitch_BreaksCommandList()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Test that @break/queued with a truthy condition still breaks the command list
		await Parser.CommandListParse(
			MModule.single("@pemit #1=BreakQueued_Before_36489;@break/queued 1=@pemit #1=BreakQueued_Action_36489;@pemit #1=BreakQueued_After_36489"));

		// The command before @break should execute
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "BreakQueued_Before_36489")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		// The command after @break should NOT execute (break stops the list)
		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "BreakQueued_After_36489")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Switch_FirstSwitch_OnlyRunsFirstMatch()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// @switch/first: only the first matching action fires, second match should not run.
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("@switch/first 1=1,@pemit #1=SwFirst_A_47592,1,@pemit #1=SwFirst_B_47592"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "SwFirst_A_47592")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "SwFirst_B_47592")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Switch_AllSwitch_RunsAllMatches()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// @switch (default) / @switch/all: all matching actions run.
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("@switch/all 1=1,@pemit #1=SwAll_A_58603,1,@pemit #1=SwAll_B_58603"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "SwAll_A_58603")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "SwAll_B_58603")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Switch_RegexpSwitch_MatchesRegexp()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// @switch/regexp: patterns are treated as case-insensitive regular expressions.
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("@switch/regexp hello=HEL+O,@pemit #1=SwRegexp_Match_69714,world,@pemit #1=SwRegexp_NoMatch_69714"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "SwRegexp_Match_69714")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "SwRegexp_NoMatch_69714")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Switch_RegexpSwitch_IsCaseInsensitive()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// @switch/regexp: per helpfile, matches are case-insensitive.
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("@switch/regexp HELLO=hello,@pemit #1=SwRegexpCI_Match_70825,world,@pemit #1=SwRegexpCI_NoMatch_70825"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "SwRegexpCI_Match_70825")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "SwRegexpCI_NoMatch_70825")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Switch_HashDollarSubstitution_ReplacedWithTestString()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// #$ in action text should be replaced with the test string before execution.
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("@switch hello=hel*,@pemit #1=SwHashDollar_#$_81936,nomatch,@pemit #1=SwHashDollar_NoMatch_81936"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "SwHashDollar_hello_81936")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Switch_HashDollarSubstitution_InDefaultAction()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// #$ in default action text should also be replaced with the test string.
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("@switch goodbye=hello,@pemit #1=SwHashDollarDef_NoMatch_92047,@pemit #1=SwHashDollarDef_#$_92047"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "SwHashDollarDef_goodbye_92047")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Switch_NotifySwitch_RunsActionAndQueuesNotify()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// @switch/notify: action fires normally; @notify me is also queued.
		// Verify the action itself executes correctly.
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("@switch/notify 1=1,@pemit #1=SwNotify_Match_93158,@pemit #1=SwNotify_Default_93158"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "SwNotify_Match_93158")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await NotifyService
			.DidNotReceive()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "SwNotify_Default_93158")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}
}
