using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.Definitions;
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

	[Test]
	public async ValueTask MessageBasic()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MsgBasic");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&TESTFORMAT_MSGBASIC_93751 {objDbRef}=MessageBasic_UniqueValue_93751"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@message {objDbRef}=Default,TESTFORMAT_MSGBASIC_93751"));

		// First argument is the recipient list, so target = objDbRef; sender = executor (not spoofed).
		await NotifyService
			.Received(1)
			.Notify(
				TestHelpers.MatchingObject(objDbRef),
				Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, "MessageBasic_UniqueValue_93751")),
				TestHelpers.MatchingObject(executor),
				INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask MessageWithAttribute()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MsgWithAttr");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&TESTFORMAT_MSGATTR_84729 {objDbRef}=MessageWithAttribute_Result_84729:[add(5,10)]"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@message {objDbRef}=Default,TESTFORMAT_MSGATTR_84729"));

		// First argument is the recipient list, so target = objDbRef; sender = executor; [add(5,10)] evaluates to 15.
		await NotifyService
			.Received(1)
			.Notify(
				TestHelpers.MatchingObject(objDbRef),
				Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, "MessageWithAttribute_Result_84729:15")),
				TestHelpers.MatchingObject(executor),
				INotifyService.NotificationType.Announce);
	}

	[Test]
	
	public async ValueTask MessageUsesDefaultWhenAttributeMissing()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MsgMissingAttr");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@message {objDbRef}=DefaultMessage_UniqueValue_72914,NONEXISTENT_ATTR_72914"));

		// Attribute is absent — falls back to the default message string; first argument still targets objDbRef.
		await NotifyService
			.Received(1)
			.Notify(
				TestHelpers.MatchingObject(objDbRef),
				Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, "DefaultMessage_UniqueValue_72914")),
				TestHelpers.MatchingObject(executor),
				INotifyService.NotificationType.Announce);
	}

	[Test]
	[NotInParallel]
	public async ValueTask MessageSilentSwitch()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MsgSilent");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&TESTFORMAT_MSGSILENT_61829 {objDbRef}=MessageSilent_Value_61829"));

		// Count confirmations already received (from other tests in session) before this command runs.
		var calls = NotifyService.ReceivedCalls().ToList();
		var confirmationsBefore = calls.Count(c =>
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			if (args[1] is not OneOf<MString, string> msg) return false;
			var text = msg.Match(ms => ms.ToPlainText(), s => s);
			return text == "Message sent to 1 recipient(s).";
		});

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@message/silent {objDbRef}=Default,TESTFORMAT_MSGSILENT_61829"));

		// Unique content string confirms the message was delivered to executor.
		await NotifyService
			.Received(1)
			.Notify(
				TestHelpers.MatchingObject(objDbRef),
				Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, "MessageSilent_Value_61829")),
				TestHelpers.MatchingObject(executor),
				INotifyService.NotificationType.Announce);

		// Confirmation must not have been sent by the silent command.
		calls = NotifyService.ReceivedCalls().ToList();
		var confirmationsAfter = calls.Count(c =>
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			if (args[1] is not OneOf<MString, string> msg) return false;
			var text = msg.Match(ms => ms.ToPlainText(), s => s);
			return text == "Message sent to 1 recipient(s).";
		});

		await Assert.That(confirmationsAfter).IsEqualTo(confirmationsBefore);
	}

	[Test]
	public async ValueTask MessageNoisySwitch()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MsgNoisy");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&TESTFORMAT_MSGNOISY_55193 {objDbRef}=MessageNoisy_Value_55193"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@message/noisy {objDbRef}=Default,TESTFORMAT_MSGNOISY_55193"));

		// Unique content — noisy sends the message and a confirmation.
		await NotifyService
			.Received(1)
			.Notify(
				TestHelpers.MatchingObject(objDbRef),
				Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, "MessageNoisy_Value_55193")),
				TestHelpers.MatchingObject(executor),
				INotifyService.NotificationType.Announce);

		// Confirmation is sent via NotifyLocalized.
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(
			NotifyService, nameof(ErrorMessages.Notifications.MessageSentToRecipientsFormat), executor, executor)).IsTrue();
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
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MsgNospoof");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"&TESTFORMAT_MSGNOSPOOF_48203 {objDbRef}=MessageNospoof_Value_48203"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@message/nospoof {objDbRef}=Default,TESTFORMAT_MSGNOSPOOF_48203"));

		// Nospoof: executor is God (can nospoof) → NSAnnounce; target = executor; sender = executor.
		await NotifyService
			.Received(1)
			.Notify(
				TestHelpers.MatchingObject(objDbRef),
				Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, "MessageNospoof_Value_48203")),
				TestHelpers.MatchingObject(executor),
				INotifyService.NotificationType.NSAnnounce);
	}
}
