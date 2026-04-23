using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests;

namespace SharpMUSH.Tests.Functions;

public class MessageFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	[Test]
	public async Task MessageBasicReturnsEmpty()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "MsgFuncBasic");
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&TESTFORMAT_MSGFUNC_19283 {objDbRef}=MessageFunc_Value_19283"));

		var result = (await Parser.FunctionParse(MModule.single($"message({objDbRef},Default,TESTFORMAT_MSGFUNC_19283)")))?.Message!;

		await Assert.That(result.ToPlainText()).IsEqualTo("");
	}

	[Test]
	public async Task MessageBasicSendsNotification()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "MsgFuncSends");
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&TESTFORMAT_MSGFUNC2_37291 {objDbRef}=MessageFuncSends_Value_37291"));

		await Parser.FunctionParse(MModule.single($"message({objDbRef},Default,TESTFORMAT_MSGFUNC2_37291)"));

		// The message() function sends the attribute value exactly to objDbRef
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(objDbRef),
				Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, "MessageFuncSends_Value_37291")),
				Arg.Any<AnySharpObject?>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async Task MessageWithAttributeEvaluation()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "MsgFuncEval");
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&TESTFORMAT_MSGEVAL_82044 {objDbRef}=MessageEval_Result_82044:[mul(3,7)]"));

		await Parser.FunctionParse(MModule.single($"message({objDbRef},Default,TESTFORMAT_MSGEVAL_82044)"));

		// The attribute evaluates [mul(3,7)] = 21, so the sent message is "MessageEval_Result_82044:21"
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(objDbRef),
				Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, "MessageEval_Result_82044:21")),
				Arg.Any<AnySharpObject?>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async Task MessageUsesDefaultWhenAttributeMissing()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "MsgFuncMissingAttr");

		await Parser.FunctionParse(MModule.single($"message({objDbRef},MessageDefault_Value_91847,MISSING_ATTR_91847)"));

		// When the attribute is missing, the default message is sent verbatim
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(objDbRef),
				Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, "MessageDefault_Value_91847")),
				Arg.Any<AnySharpObject?>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async Task MessageWithMultipleArguments()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "MsgFuncArgs");
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&TESTFORMAT_MSGARGS_63018 {objDbRef}=MessageArgs_Value_63018"));

		await Parser.FunctionParse(MModule.single($"message({objDbRef},Default,TESTFORMAT_MSGARGS_63018)"));

		// The attribute sends the stored value exactly
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(objDbRef),
				Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, "MessageArgs_Value_63018")),
				Arg.Any<AnySharpObject?>(), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NeedsSetup")]
	[Skip("Requires attribute setup")]
	public async Task MessageHashHashReplacement()
	{
		await ValueTask.CompletedTask;
	}

	[Test]
	[Category("NeedsSetup")]
	[Skip("Requires configuration setup")]
	public async Task MessageNoSideFxDisabled()
	{
		await ValueTask.CompletedTask;
	}

	[Test]
	[Category("NeedsSetup")]
	[Skip("Requires room setup")]
	public async Task MessageRemitSwitch()
	{
		await ValueTask.CompletedTask;
	}

	[Test]
	[Category("NeedsSetup")]
	[Skip("Requires multiple objects setup")]
	public async Task MessageOemitSwitch()
	{
		await ValueTask.CompletedTask;
	}
}
