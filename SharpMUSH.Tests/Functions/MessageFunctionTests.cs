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
		await CommandParser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGFUNC #1=Function: %0"));
		
		var result = (await Parser.FunctionParse(MModule.single("message(#1,Default,TESTFORMAT_MSGFUNC,TestValue)")))?.Message!;
		
		await Assert.That(result.ToPlainText()).IsEqualTo("");
	}

	[Test]
	public async Task MessageBasicSendsNotification()
	{
		await CommandParser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGFUNC2 #1=Function result: %0"));
		NotifyService.ClearReceivedCalls();
		
		await Parser.FunctionParse(MModule.single("message(#1,Default,TESTFORMAT_MSGFUNC2,TestValue)"));
		
		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c => 
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			var msg = args[1] as OneOf<MString, string>?;
			if (msg == null) return false;
			return msg.Value.Match(
				ms => ms.ToPlainText().Contains("Function result: TestValue"),
				s => s.Contains("Function result: TestValue"));
		});
		
		await Assert.That(messageCall).IsNotNull();
	}

	[Test]
	public async Task MessageWithAttributeEvaluation()
	{
		await CommandParser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGEVAL #1=Result: [mul(%0,%1)]"));
		NotifyService.ClearReceivedCalls();
		
		await Parser.FunctionParse(MModule.single("message(#1,Default,TESTFORMAT_MSGEVAL,3,7)"));
		
		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c => 
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			var msg = args[1] as OneOf<MString, string>?;
			if (msg == null) return false;
			return msg.Value.Match(
				ms => ms.ToPlainText().Contains("Result: 21"),
				s => s.Contains("Result: 21"));
		});
		
		await Assert.That(messageCall).IsNotNull();
	}

	[Test]
	public async Task MessageUsesDefaultWhenAttributeMissing()
	{
		NotifyService.ClearReceivedCalls();
		
		await Parser.FunctionParse(MModule.single("message(#1,Default shown here,MISSING_ATTR,Arg1)"));
		
		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c => 
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			var msg = args[1] as OneOf<MString, string>?;
			if (msg == null) return false;
			return msg.Value.Match(
				ms => ms.ToPlainText().Contains("Default shown here"),
				s => s.Contains("Default shown here"));
		});
		
		await Assert.That(messageCall).IsNotNull();
	}

	[Test]
	public async Task MessageWithMultipleArguments()
	{
		await CommandParser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGARGS #1=Args: %0 %1 %2"));
		NotifyService.ClearReceivedCalls();
		
		await Parser.FunctionParse(MModule.single("message(#1,Default,TESTFORMAT_MSGARGS,First,Second,Third)"));
		
		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c => 
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			var msg = args[1] as OneOf<MString, string>?;
			if (msg == null) return false;
			return msg.Value.Match(
				ms => ms.ToPlainText().Contains("Args: First Second Third"),
				s => s.Contains("Args: First Second Third"));
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
