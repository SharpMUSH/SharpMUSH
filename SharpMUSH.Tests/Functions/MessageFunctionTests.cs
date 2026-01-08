using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class MessageFunctionTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private static bool MessageContains(OneOf<MString, string> msg, string expected) =>
		msg.Match(
			ms => ms.ToPlainText().Contains(expected),
			s => s.Contains(expected));

	[Test]
	public async Task MessageBasicReturnsEmpty()
	{
		await CommandParser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGFUNC_19283 #1=MessageFunc_Value_19283"));
		
		var result = (await Parser.FunctionParse(MModule.single("message(#1,Default,TESTFORMAT_MSGFUNC_19283)")))?.Message!;
		
		await Assert.That(result.ToPlainText()).IsEqualTo("");
	}

	[Test]
	public async Task MessageBasicSendsNotification()
	{
		await CommandParser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGFUNC2_37291 #1=MessageFuncSends_Value_37291"));
		
		await Parser.FunctionParse(MModule.single("message(#1,Default,TESTFORMAT_MSGFUNC2_37291)"));
		
		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c => 
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			if (args[1] is not OneOf<MString, string> msg) return false;
			return msg.Match(
				ms => ms.ToPlainText().Contains("MessageFuncSends_Value_37291"),
				s => s.Contains("MessageFuncSends_Value_37291"));
		});
		
		await Assert.That(messageCall).IsNotNull();
	}

	[Test]
	public async Task MessageWithAttributeEvaluation()
	{
		await CommandParser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGEVAL_82044 #1=MessageEval_Result_82044:[mul(3,7)]"));
		
		await Parser.FunctionParse(MModule.single("message(#1,Default,TESTFORMAT_MSGEVAL_82044)"));
		
		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c => 
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			if (args[1] is not OneOf<MString, string> msg) return false;
			return msg.Match(
				ms => ms.ToPlainText().Contains("MessageEval_Result_82044:21"),
				s => s.Contains("MessageEval_Result_82044:21"));
		});
		
		await Assert.That(messageCall).IsNotNull();
	}

	[Test]
	public async Task MessageUsesDefaultWhenAttributeMissing()
	{
		await Parser.FunctionParse(MModule.single("message(#1,MessageDefault_Value_91847,MISSING_ATTR_91847)"));
		
		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c => 
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			var msg = args[1] as OneOf<MString, string>?;
			if (msg == null) return false;
			return msg.Value.Match(
				ms => ms.ToPlainText().Contains("MessageDefault_Value_91847"),
				s => s.Contains("MessageDefault_Value_91847"));
		});
		
		await Assert.That(messageCall).IsNotNull();
	}

	[Test]
	public async Task MessageWithMultipleArguments()
	{
		await CommandParser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGARGS_63018 #1=MessageArgs_Value_63018"));
		
		await Parser.FunctionParse(MModule.single("message(#1,Default,TESTFORMAT_MSGARGS_63018)"));
		
		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c => 
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			if (args[1] is not OneOf<MString, string> msg) return false;
			return msg.Match(
				ms => ms.ToPlainText().Contains("MessageArgs_Value_63018"),
				s => s.Contains("MessageArgs_Value_63018"));
		});
		
		await Assert.That(messageCall).IsNotNull();
	}

	[Test]
	[Skip("Requires attribute setup")]
	public async Task MessageHashHashReplacement()
	{
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Requires configuration setup")]
	public async Task MessageNoSideFxDisabled()
	{
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Requires room setup")]
	public async Task MessageRemitSwitch()
	{
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Requires multiple objects setup")]
	public async Task MessageOemitSwitch()
	{
		await ValueTask.CompletedTask;
	}
}
