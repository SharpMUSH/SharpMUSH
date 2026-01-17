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
	[ClassDataSource<TestClassFactory>(Shared = SharedType.PerClass)]
	public required TestClassFactory Factory { get; init; }

	private IMUSHCodeParser Parser => Factory.FunctionParser;
	private IMUSHCodeParser CommandParser => Factory.CommandParser;
	private INotifyService NotifyService => Factory.NotifyService;
	private IConnectionService ConnectionService => Factory.Services.GetRequiredService<IConnectionService>();

	[Test]
	public async Task MessageBasicReturnsEmpty()
	{
		// Clear any previous calls to the mock

		await CommandParser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGFUNC_19283 #1=MessageFunc_Value_19283"));
		
		var result = (await Parser.FunctionParse(MModule.single("message(#1,Default,TESTFORMAT_MSGFUNC_19283)")))?.Message!;
		
		await Assert.That(result.ToPlainText()).IsEqualTo("");
	}

	[Test]
	public async Task MessageBasicSendsNotification()
	{
		// Clear any previous calls to the mock

		await CommandParser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGFUNC2_37291 #1=MessageFuncSends_Value_37291"));
		
		await Parser.FunctionParse(MModule.single("message(#1,Default,TESTFORMAT_MSGFUNC2_37291)"));
		
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
		// Clear any previous calls to the mock

		await CommandParser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGEVAL_82044 #1=MessageEval_Result_82044:[mul(3,7)]"));
		
		await Parser.FunctionParse(MModule.single("message(#1,Default,TESTFORMAT_MSGEVAL_82044)"));
		
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
		// Clear any previous calls to the mock

		await Parser.FunctionParse(MModule.single("message(#1,MessageDefault_Value_91847,MISSING_ATTR_91847)"));
		
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
		// Clear any previous calls to the mock

		await CommandParser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGARGS_63018 #1=MessageArgs_Value_63018"));
		
		await Parser.FunctionParse(MModule.single("message(#1,Default,TESTFORMAT_MSGARGS_63018)"));
		
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
