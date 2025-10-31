using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class MessageCommandTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	private static bool MessageContains(OneOf<MString, string> msg, string expected) =>
		msg.Match(
			ms => ms.ToPlainText().Contains(expected),
			s => s.Contains(expected));

	private static bool MessageEquals(OneOf<MString, string> msg, string expected) =>
		msg.Match(
			ms => ms.ToPlainText() == expected,
			s => s == expected);

	[Test]
	public async ValueTask MessageBasic()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGBASIC #1=Formatted: %0"));
		NotifyService.ClearReceivedCalls();
		
		await Parser.CommandParse(1, ConnectionService, MModule.single("@message #1=Default message,TESTFORMAT_MSGBASIC,TestArg"));
		
		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c => 
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			var msg = args[1] as OneOf<MString, string>?;
			if (msg == null) return false;
			return msg.Value.Match(
				ms => ms.ToPlainText().Contains("Formatted: TestArg"),
				s => s.Contains("Formatted: TestArg"));
		});
		
		await Assert.That(messageCall).IsNotNull();
	}

	[Test]
	public async ValueTask MessageWithAttribute()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGATTR #1=Custom format: [add(%0,%1)]"));
		NotifyService.ClearReceivedCalls();
		
		await Parser.CommandParse(1, ConnectionService, MModule.single("@message #1=Default,#1/TESTFORMAT_MSGATTR,5,10"));
		
		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c => 
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			var msg = args[1] as OneOf<MString, string>?;
			if (msg == null) return false;
			return msg.Value.Match(
				ms => ms.ToPlainText().Contains("Custom format: 15"),
				s => s.Contains("Custom format: 15"));
		});
		
		await Assert.That(messageCall).IsNotNull();
	}

	[Test]
	public async ValueTask MessageUsesDefaultWhenAttributeMissing()
	{
		NotifyService.ClearReceivedCalls();
		
		await Parser.CommandParse(1, ConnectionService, MModule.single("@message #1=Default message shown,NONEXISTENT_ATTR,TestArg"));
		
		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c => 
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			var msg = args[1] as OneOf<MString, string>?;
			if (msg == null) return false;
			return msg.Value.Match(
				ms => ms.ToPlainText().Contains("Default message shown"),
				s => s.Contains("Default message shown"));
		});
		
		await Assert.That(messageCall).IsNotNull();
	}

	[Test]
	public async ValueTask MessageSilentSwitch()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGSILENT #1=Silent: %0"));
		NotifyService.ClearReceivedCalls();
		
		await Parser.CommandParse(1, ConnectionService, MModule.single("@message/silent #1=Test,TESTFORMAT_MSGSILENT,TestValue"));
		
		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c => 
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			var msg = args[1] as OneOf<MString, string>?;
			if (msg == null) return false;
			return msg.Value.Match(
				ms => ms.ToPlainText().Contains("Silent: TestValue"),
				s => s.Contains("Silent: TestValue"));
		});
		
		await Assert.That(messageCall).IsNotNull();
	}

	[Test]
	public async ValueTask MessageNoisySwitch()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGNOISY #1=Noisy: %0"));
		NotifyService.ClearReceivedCalls();
		
		await Parser.CommandParse(1, ConnectionService, MModule.single("@message/noisy #1=Test,TESTFORMAT_MSGNOISY,TestValue"));
		
		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c => 
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			var msg = args[1] as OneOf<MString, string>?;
			if (msg == null) return false;
			return msg.Value.Match(
				ms => ms.ToPlainText().Contains("Noisy: TestValue"),
				s => s.Contains("Noisy: TestValue"));
		});
		
		await Assert.That(messageCall).IsNotNull();
	}

	[Test]
	[Skip("Requires room setup")]
	public async ValueTask MessageRemitSwitch()
	{
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Requires multiple objects")]
	public async ValueTask MessageOemitSwitch()
	{
		await ValueTask.CompletedTask;
	}

	[Test]
	public async ValueTask MessageNospoofSwitch()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGNOSPOOF #1=Nospoof: %0"));
		NotifyService.ClearReceivedCalls();
		
		await Parser.CommandParse(1, ConnectionService, MModule.single("@message/nospoof #1=Test,TESTFORMAT_MSGNOSPOOF,TestValue"));

		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c => 
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			var msg = args[1] as OneOf<MString, string>?;
			if (msg == null) return false;
			return msg.Value.Match(
				ms => ms.ToPlainText().Contains("Nospoof: TestValue"),
				s => s.Contains("Nospoof: TestValue"));
		});
		
		await Assert.That(messageCall).IsNotNull();
	}
}
