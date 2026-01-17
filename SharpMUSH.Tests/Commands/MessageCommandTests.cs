using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Linq;
namespace SharpMUSH.Tests.Commands;

public class MessageCommandTests
{
	[ClassDataSource<TestClassFactory>(Shared = SharedType.PerClass)]
	public required TestClassFactory Factory { get; init; }

	private INotifyService NotifyService => Factory.NotifyService;
	private IConnectionService ConnectionService => Factory.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => Factory.CommandParser;

	[Test]
	public async ValueTask MessageBasic()
	{
		// Clear any previous calls to the mock

		await Parser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGBASIC_93751 #1=MessageBasic_UniqueValue_93751"));
		
		await Parser.CommandParse(1, ConnectionService, MModule.single("@message #1=Default,TESTFORMAT_MSGBASIC_93751"));
		
		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c => 
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			
			if (args[1] is OneOf<MString, string> oneOfMsg)
			{
				return oneOfMsg.Match(
					ms => ms.ToPlainText().Contains("MessageBasic_UniqueValue_93751"),
					s => s.Contains("MessageBasic_UniqueValue_93751"));
			}
			return false;
		});
		
		await Assert.That(messageCall).IsNotNull();
	}

	[Test]
	public async ValueTask MessageWithAttribute()
	{
		// Clear any previous calls to the mock

		await Parser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGATTR_84729 #1=MessageWithAttribute_Result_84729:[add(5,10)]"));
		
		await Parser.CommandParse(1, ConnectionService, MModule.single("@message #1=Default,TESTFORMAT_MSGATTR_84729"));
		
		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c => 
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			var msg = args[1] as OneOf<MString, string>?;
			if (msg == null) return false;
			return msg.Value.Match(
				ms => ms.ToPlainText().Contains("MessageWithAttribute_Result_84729:15"),
				s => s.Contains("MessageWithAttribute_Result_84729:15"));
		});
		
		await Assert.That(messageCall).IsNotNull();
	}

	[Test]
	public async ValueTask MessageUsesDefaultWhenAttributeMissing()
	{
		// Clear any previous calls to the mock
		
		await Parser.CommandParse(1, ConnectionService, MModule.single("@message #1=DefaultMessage_UniqueValue_72914,NONEXISTENT_ATTR_72914"));
		
		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c => 
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			var msg = args[1] as OneOf<MString, string>?;
			if (msg == null) return false;
			return msg.Value.Match(
				ms => ms.ToPlainText().Contains("DefaultMessage_UniqueValue_72914"),
				s => s.Contains("DefaultMessage_UniqueValue_72914"));
		});
		
		await Assert.That(messageCall).IsNotNull();
	}

	[Test]
	public async ValueTask MessageSilentSwitch()
	{
		// Clear any previous calls to the mock

		await Parser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGSILENT_61829 #1=MessageSilent_Value_61829"));
		
		var calls = NotifyService.ReceivedCalls().ToList();
		var confirmationCallsBefore = calls.Count(c => 
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			if (args[1] is not OneOf<MString, string> msg) return false;
			var text = msg.Match(ms => ms.ToPlainText(), s => s);
			return text == "Message sent to 1 recipient(s).";
		});
		
		await Parser.CommandParse(1, ConnectionService, MModule.single("@message/silent #1=Default,TESTFORMAT_MSGSILENT_61829"));
		
		calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c => 
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			if (args[1] is not OneOf<MString, string> msg) return false;
			return TestHelpers.MessageContains(msg, "MessageSilent_Value_61829");
		});
		
		await Assert.That(messageCall).IsNotNull();
		
		var confirmationCallsAfter = calls.Count(c => 
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			if (args[1] is not OneOf<MString, string> msg) return false;
			var text = msg.Match(ms => ms.ToPlainText(), s => s);
			return text == "Message sent to 1 recipient(s).";
		});
		
		await Assert.That(confirmationCallsBefore).IsEqualTo(confirmationCallsAfter);
	}

	[Test]
	public async ValueTask MessageNoisySwitch()
	{
		// Clear any previous calls to the mock

		await Parser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGNOISY_55193 #1=MessageNoisy_Value_55193"));
		
		await Parser.CommandParse(1, ConnectionService, MModule.single("@message/noisy #1=Default,TESTFORMAT_MSGNOISY_55193"));
		
		var calls = NotifyService.ReceivedCalls().ToList();
		
		var messageCall = calls.FirstOrDefault(c => 
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			var msg = args[1] as OneOf<MString, string>?;
			if (msg == null) return false;
			return msg.Value.Match(
				ms => ms.ToPlainText().Contains("MessageNoisy_Value_55193"),
				s => s.Contains("MessageNoisy_Value_55193"));
		});
		
		await Assert.That(messageCall).IsNotNull();
		
		var confirmationCall = calls.FirstOrDefault(c => 
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			var msg = args[1] as OneOf<MString, string>?;
			if (msg == null) return false;
			return msg.Value.Match(
				ms => ms.ToPlainText().Contains("Message sent to"),
				s => s.Contains("Message sent to"));
		});
		
		await Assert.That(confirmationCall).IsNotNull();
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
		// Clear any previous calls to the mock

		await Parser.CommandParse(1, ConnectionService, MModule.single("&TESTFORMAT_MSGNOSPOOF_48203 #1=MessageNospoof_Value_48203"));
		
		await Parser.CommandParse(1, ConnectionService, MModule.single("@message/nospoof #1=Default,TESTFORMAT_MSGNOSPOOF_48203"));

		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c => 
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			var msg = args[1] as OneOf<MString, string>?;
			if (msg == null) return false;
			return msg.Value.Match(
				ms => ms.ToPlainText().Contains("MessageNospoof_Value_48203"),
				s => s.Contains("MessageNospoof_Value_48203"));
		});
		
		await Assert.That(messageCall).IsNotNull();
	}
}
