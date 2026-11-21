using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
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
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&TESTFORMAT_MSGBASIC_93751 {objDbRef}=MessageBasic_UniqueValue_93751"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@message {objDbRef}=Default,TESTFORMAT_MSGBASIC_93751"));

		// First argument is the recipient list, so target = objDbRef; sender = executor (not spoofed).
		await NotifyService
			.Received(1)
			.Notify(
				TestHelpers.MatchingObject(objDbRef),
				Arg.Is<SharpMessage>(msg => TestHelpers.MessagePlainTextEquals(msg, "MessageBasic_UniqueValue_93751")),
				TestHelpers.MatchingObject(executor),
				INotifyService.NotificationType.PrivateEmit);
	}

	[Test]
	public async ValueTask MessageWithAttribute()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MsgWithAttr");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&TESTFORMAT_MSGATTR_84729 {objDbRef}=MessageWithAttribute_Result_84729:[add(5,10)]"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@message {objDbRef}=Default,TESTFORMAT_MSGATTR_84729"));

		// First argument is the recipient list, so target = objDbRef; sender = executor; [add(5,10)] evaluates to 15.
		await NotifyService
			.Received(1)
			.Notify(
				TestHelpers.MatchingObject(objDbRef),
				Arg.Is<SharpMessage>(msg => TestHelpers.MessagePlainTextEquals(msg, "MessageWithAttribute_Result_84729:15")),
				TestHelpers.MatchingObject(executor),
				INotifyService.NotificationType.PrivateEmit);
	}

	[Test]
	public async ValueTask MessageRecipientTokenIsShortDbref()
	{
		// PennMUSH replaces a `##` argument with unparse_dbref(recipient) -- "#N", not the objid
		// (src/notify.c) -- so FORMAT`SAY's strmatch(%1,%#) can tell the speaker apart.
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MsgRecipientToken");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&TESTFORMAT_MSGTOKEN_51837 {objDbRef}=Token_51837:%0"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@message {objDbRef}=Default,TESTFORMAT_MSGTOKEN_51837,##"));

		await NotifyService
			.Received(1)
			.Notify(
				TestHelpers.MatchingObject(objDbRef),
				Arg.Is<SharpMessage>(msg => TestHelpers.MessagePlainTextEquals(msg, $"Token_51837:#{objDbRef.Number}")),
				TestHelpers.MatchingObject(executor),
				INotifyService.NotificationType.PrivateEmit);
	}

	[Test]

	public async ValueTask MessageUsesDefaultWhenAttributeMissing()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MsgMissingAttr");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@message {objDbRef}=DefaultMessage_UniqueValue_72914,NONEXISTENT_ATTR_72914"));

		// Attribute is absent — falls back to the default message string; first argument still targets objDbRef.
		await NotifyService
			.Received(1)
			.Notify(
				TestHelpers.MatchingObject(objDbRef),
				Arg.Is<SharpMessage>(msg => TestHelpers.MessagePlainTextEquals(msg, "DefaultMessage_UniqueValue_72914")),
				TestHelpers.MatchingObject(executor),
				INotifyService.NotificationType.PrivateEmit);
	}

	[Test]
	public async ValueTask MessageSilentSwitch()
	{
		// A wizard of the test's own sends it, so a confirmation would land in a bucket nobody else writes.
		var sender = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, WebAppFactoryArg.Services.GetRequiredService<Mediator.IMediator>(), ConnectionService, "MsgSilentSender");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {sender.DbRef}=WIZARD"));
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MsgSilent");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&TESTFORMAT_MSGSILENT_61829 {objDbRef}=MessageSilent_Value_61829"));

		await Parser.CommandParse(sender.Handle, ConnectionService, MarkupText.Plain($"@message/silent {objDbRef}=Default,TESTFORMAT_MSGSILENT_61829"));

		// Unique content string confirms the message was delivered.
		await Assert.That(WebAppFactoryArg.Notifications.DeliveriesFor(objDbRef)
				.Count(delivery => delivery.Message == "MessageSilent_Value_61829"
					&& delivery.Sender == sender.DbRef
					&& delivery.Type == INotifyService.NotificationType.PrivateEmit))
			.IsEqualTo(1);

		// Confirmation must not have been sent by the silent command.
		await Assert.That(WebAppFactoryArg.Notifications.For(sender.DbRef).Any(line => line.Contains("MessageSilent_Value_61829"))).IsFalse();
	}

	[Test]
	public async ValueTask MessageConfirmsByDefault()
	{
		// PennMUSH's @message is @pemit/list: unless silent_pemit is on, the sender is told what went to whom.
		var sender = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, WebAppFactoryArg.Services.GetRequiredService<Mediator.IMediator>(), ConnectionService, "MsgDefaultSender");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {sender.DbRef}=WIZARD"));
		var target = TestIsolationHelpers.GenerateUniqueName("MsgDefault");
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, target);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&TESTFORMAT_MSGDEFAULT_30417 {objDbRef}=MessageDefault_Value_30417"));

		await Parser.CommandParse(sender.Handle, ConnectionService, MarkupText.Plain($"@message {objDbRef}=Fallback_30417,TESTFORMAT_MSGDEFAULT_30417"));

		// The confirmation quotes the default message, since each recipient's own may differ.
		await Assert.That(WebAppFactoryArg.Notifications.For(sender.DbRef))
			.Contains(line => line.StartsWith("You pemit \"Fallback_30417\" to ") && line.Contains(target));
	}

	[Test]
	public async ValueTask MessageNoisySwitch()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MsgNoisy");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&TESTFORMAT_MSGNOISY_55193 {objDbRef}=MessageNoisy_Value_55193"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@message/noisy {objDbRef}=Default,TESTFORMAT_MSGNOISY_55193"));

		// Unique content — noisy sends the message and a confirmation.
		await NotifyService
			.Received(1)
			.Notify(
				TestHelpers.MatchingObject(objDbRef),
				Arg.Is<SharpMessage>(msg => TestHelpers.MessagePlainTextEquals(msg, "MessageNoisy_Value_55193")),
				TestHelpers.MatchingObject(executor),
				INotifyService.NotificationType.PrivateEmit);

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(
			NotifyService, nameof(ErrorMessages.Notifications.YouPemitToObjectFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask MessageNospoofSwitch()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "MsgNospoof");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&TESTFORMAT_MSGNOSPOOF_48203 {objDbRef}=MessageNospoof_Value_48203"));

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@message/nospoof {objDbRef}=Default,TESTFORMAT_MSGNOSPOOF_48203"));

		// Nospoof: executor is God (can nospoof) → NSPrivateEmit; target = executor; sender = executor.
		await NotifyService
			.Received(1)
			.Notify(
				TestHelpers.MatchingObject(objDbRef),
				Arg.Is<SharpMessage>(msg => TestHelpers.MessagePlainTextEquals(msg, "MessageNospoof_Value_48203")),
				TestHelpers.MatchingObject(executor),
				INotifyService.NotificationType.NSPrivateEmit);
	}
}
