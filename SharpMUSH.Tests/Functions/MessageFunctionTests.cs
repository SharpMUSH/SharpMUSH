using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
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

		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c =>
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			if (args[1] is not OneOf<MString, string> msg) return false;
			return TestHelpers.MessageContains(msg, "MessageFuncSends_Value_37291");
		});

		await Assert.That(messageCall).IsNotNull();
	}

	[Test]
	public async Task MessageWithAttributeEvaluation()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "MsgFuncEval");
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&TESTFORMAT_MSGEVAL_82044 {objDbRef}=MessageEval_Result_82044:[mul(3,7)]"));

		await Parser.FunctionParse(MModule.single($"message({objDbRef},Default,TESTFORMAT_MSGEVAL_82044)"));

		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c =>
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			if (args[1] is not OneOf<MString, string> msg) return false;
			return TestHelpers.MessageContains(msg, "MessageEval_Result_82044:21");
		});

		await Assert.That(messageCall).IsNotNull();
	}

	[Test]
	public async Task MessageUsesDefaultWhenAttributeMissing()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "MsgFuncMissingAttr");

		await Parser.FunctionParse(MModule.single($"message({objDbRef},MessageDefault_Value_91847,MISSING_ATTR_91847)"));

		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c =>
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			if (args[1] is not OneOf<MString, string> msg) return false;
			return TestHelpers.MessageContains(msg, "MessageDefault_Value_91847");
		});

		await Assert.That(messageCall).IsNotNull();
	}

	[Test]
	public async Task MessageWithMultipleArguments()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "MsgFuncArgs");
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&TESTFORMAT_MSGARGS_63018 {objDbRef}=MessageArgs_Value_63018"));

		await Parser.FunctionParse(MModule.single($"message({objDbRef},Default,TESTFORMAT_MSGARGS_63018)"));

		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c =>
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			if (args[1] is not OneOf<MString, string> msg) return false;
			return TestHelpers.MessageContains(msg, "MessageArgs_Value_63018");
		});

		await Assert.That(messageCall).IsNotNull();
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
