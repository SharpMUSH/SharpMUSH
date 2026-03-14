using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

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
		await Parser.CommandParse(1, ConnectionService, MModule.single("@select 1=1,@pemit #1=One,@pemit #1=Other"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask SwitchCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@switch 1=1,@pemit #1=One,@pemit #1=Other"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask BreakCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@break"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask AssertCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@assert 1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask RetryCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@retry 1"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask SkipCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@skip 0=@pemit #1=SkipCommand False; @pemit #1=SkipCommand Rest"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), "SkipCommand False", Arg.Any<AnySharpObject>());

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), "SkipCommand Rest", Arg.Any<AnySharpObject>());
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask IncludeCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@include #1/ATTRIBUTE"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	public async ValueTask IfElseCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@ifelse 1=@pemit #1=IfElseCommand True,@pemit #1=IfElseCommand False"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), "IfElseCommand True", Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Include_StripsDollarCommandPrefix()
	{
		// Set an attribute with a $...: command prefix pattern
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("&INCL_DOLLAR_TEST #1=$testcmd *:@pemit #1=IncludeDollarPrefix_Executed_71934"));

		// @include should strip the $testcmd *: prefix and execute the remainder
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("@include #1/INCL_DOLLAR_TEST"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "IncludeDollarPrefix_Executed_71934")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Include_StripsCaretListenPrefix()
	{
		// Set an attribute with a ^...: listen prefix pattern
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("&INCL_CARET_TEST #1=^*says*:@pemit #1=IncludeCaretPrefix_Executed_82045"));

		// @include should strip the ^*says*: prefix and execute the remainder
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("@include #1/INCL_CARET_TEST"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "IncludeCaretPrefix_Executed_82045")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Include_NoPrefix_ExecutesDirectly()
	{
		// Set an attribute without any prefix pattern
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("&INCL_NOPREFIX_TEST #1=@pemit #1=IncludeNoPrefix_Executed_93156"));

		// @include should execute the attribute content as-is
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("@include #1/INCL_NOPREFIX_TEST"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "IncludeNoPrefix_Executed_93156")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Include_WithNobreakSwitch_PreventsBreakPropagation()
	{
		// Set an attribute that includes an @break
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("&INCL_NOBRK_TEST #1=@pemit #1=IncludeNobreak_Before_14267;@break 1"));

		// Use @include/nobreak so @break doesn't propagate, then execute next command
		await Parser.CommandListParse(
			MModule.single("@include/nobreak #1/INCL_NOBRK_TEST;@pemit #1=IncludeNobreak_After_14267"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "IncludeNobreak_Before_14267")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);

		// The command after @include/nobreak should still execute
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "IncludeNobreak_After_14267")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("KnownBug")]
	[Skip("TODO: @include argument substitution for %0-%9 not yet fully implemented in the hook system")]
	public async ValueTask Include_DollarPrefix_WithArguments()
	{
		// Set an attribute with a $...: prefix that uses %0
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("&INCL_DOLLARARG_TEST #1=$testarg *:@pemit #1=IncludeDollarArg_%0_25378"));

		// @include with arguments should strip prefix and substitute %0
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("@include #1/INCL_DOLLARARG_TEST=Hello"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "IncludeDollarArg_Hello_25378")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Break_QueuedSwitch_BreaksCommandList()
	{
		// Test that @break/queued with a truthy condition still breaks the command list
		await Parser.CommandListParse(
			MModule.single("@pemit #1=BreakQueued_Before_36489;@break/queued 1=@pemit #1=BreakQueued_Action_36489;@pemit #1=BreakQueued_After_36489"));

		// The command before @break should execute
		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "BreakQueued_Before_36489")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);

		// The command after @break should NOT execute (break stops the list)
		await NotifyService
			.DidNotReceive()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessageEquals(msg, "BreakQueued_After_36489")), Arg.Any<AnySharpObject>(), INotifyService.NotificationType.Announce);
	}
}
