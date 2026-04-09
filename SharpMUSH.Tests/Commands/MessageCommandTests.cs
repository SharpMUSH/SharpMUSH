using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests;

namespace SharpMUSH.Tests.Commands;

public class MessageCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private Task<TestIsolationHelpers.TestPlayer> CreateTestPlayerAsync(string namePrefix) =>
		TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, namePrefix);

	[Test]
	public async ValueTask MessageBasic()
	{
		var testPlayer = await CreateTestPlayerAsync("MsgBasic");
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MsgBasic");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"&TESTFORMAT_MSGBASIC_93751 {objDbRef}=MessageBasic_UniqueValue_93751"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@message {objDbRef}=Default,TESTFORMAT_MSGBASIC_93751"));

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
		var testPlayer = await CreateTestPlayerAsync("MsgWithAttr");
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MsgWithAttr");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"&TESTFORMAT_MSGATTR_84729 {objDbRef}=MessageWithAttribute_Result_84729:[add(5,10)]"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@message {objDbRef}=Default,TESTFORMAT_MSGATTR_84729"));

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
		var testPlayer = await CreateTestPlayerAsync("MsgMissAttr");
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MsgMissingAttr");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@message {objDbRef}=DefaultMessage_UniqueValue_72914,NONEXISTENT_ATTR_72914"));

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
	[NotInParallel]
	public async ValueTask MessageSilentSwitch()
	{
		var testPlayer = await CreateTestPlayerAsync("MsgSilent");
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MsgSilent");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"&TESTFORMAT_MSGSILENT_61829 {objDbRef}=MessageSilent_Value_61829"));

		var calls = NotifyService.ReceivedCalls().ToList();
		var confirmationCallsBefore = calls.Count(c =>
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			if (args[1] is not OneOf<MString, string> msg) return false;
			var text = msg.Match(ms => ms.ToPlainText(), s => s);
			return text == "Message sent to 1 recipient(s).";
		});

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@message/silent {objDbRef}=Default,TESTFORMAT_MSGSILENT_61829"));

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
		var testPlayer = await CreateTestPlayerAsync("MsgNoisy");
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MsgNoisy");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"&TESTFORMAT_MSGNOISY_55193 {objDbRef}=MessageNoisy_Value_55193"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@message/noisy {objDbRef}=Default,TESTFORMAT_MSGNOISY_55193"));

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
	[Category("NeedsSetup")]
	[Skip("Requires room setup")]
	public async ValueTask MessageRemitSwitch()
	{
		await ValueTask.CompletedTask;
	}

	[Test]
	[Category("NeedsSetup")]
	[Skip("Requires multiple objects")]
	public async ValueTask MessageOemitSwitch()
	{
		await ValueTask.CompletedTask;
	}

	[Test]
	public async ValueTask MessageNospoofSwitch()
	{
		var testPlayer = await CreateTestPlayerAsync("MsgNospoof");
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MsgNospoof");
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"&TESTFORMAT_MSGNOSPOOF_48203 {objDbRef}=MessageNospoof_Value_48203"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single($"@message/nospoof {objDbRef}=Default,TESTFORMAT_MSGNOSPOOF_48203"));

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
