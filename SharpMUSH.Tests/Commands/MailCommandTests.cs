using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.Core;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using OneOf;

namespace SharpMUSH.Tests.Commands;

public class MailCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	/// <summary>
	/// sharpmail.md:47 lists bare <c>@mail</c> under @MAIL/LIST: "a brief list of all mail in the
	/// current folder". It used to reach the SEND arm instead — <c>arg0?.Length != 0</c> is
	/// <c>int?</c> compared to <c>int</c>, and a null lifts to <c>true</c> — and then throw
	/// <see cref="NullReferenceException"/> inside SendMail on the two null arguments.
	/// A message is sent first so an empty mailbox cannot pass this by accident.
	/// </summary>
	[Test]
	public async ValueTask BareMailListsTheMailboxRatherThanTryingToSend()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MailBareList");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		await parser.CommandParse(player.Handle, ConnectionService,
			MModule.single($"@mail #{player.DbRef.Number}=Bare List Subject/Bare list body."));

		var beforeBare = NotificationsTo(player.DbRef).Length;

		await parser.CommandParse(player.Handle, ConnectionService, MModule.single("@mail"));

		var afterBare = NotificationsTo(player.DbRef).Skip(beforeBare).ToArray();

		await Assert.That(afterBare).DoesNotContain(m => m.StartsWith("#-1 EXCEPTION: "));
		await Assert.That(afterBare).Contains(m => m.Contains("MAIL (folder INBOX)"));
		await Assert.That(afterBare).Contains(m => m.Contains("Bare List Subject"));
	}

	/// <summary>
	/// <c>@mail/clear</c> with no msg-list clears the whole current folder (sharpmail.md:91).
	/// It used to dereference a null <c>arg0</c> in StatusMail.
	/// </summary>
	[Test]
	public async ValueTask ClearWithNoMessageListDoesNotThrow()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MailBareClear");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		await parser.CommandParse(player.Handle, ConnectionService,
			MModule.single($"@mail #{player.DbRef.Number}=Clear Subject/Clear body."));

		var beforeClear = NotificationsTo(player.DbRef).Length;

		await parser.CommandParse(player.Handle, ConnectionService, MModule.single("@mail/clear"));

		var afterClear = NotificationsTo(player.DbRef).Skip(beforeClear).ToArray();

		await Assert.That(afterClear).DoesNotContain(m => m.StartsWith("#-1 EXCEPTION: "));
		await Assert.That(afterClear).Contains(m => m.Contains("updated"));
	}

	private string[] NotificationsTo(DBRef target) =>
		NotifyService.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(INotifyService.Notify))
			.Where(call => call.GetArguments() is [AnySharpObject obj, ..] && obj.Object().DBRef == target)
			.Select(TextOf)
			.Where(text => text is not null)
			.Select(text => text!)
			.ToArray();

	private static string? TextOf(ICall call) =>
		call.GetArguments() is [_, OneOf<MString, string> msg, ..]
			? msg.Match(ms => ms.ToPlainText(), s => s)
			: null;

	[Test]
	public async ValueTask MailCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@mail #1=Test subject/Test message"));

		// Mailing yourself produces both halves of the exchange: the send confirmation and the
		// delivery notice. The original expectation of exactly one is what kept this skipped.
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "MAIL: You sent a message")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "MAIL: You have received a message")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// extmail.c:1379 matches with <c>MAT_ME</c>, so <c>me</c> is a recipient like any other.
	/// A name-only lookup matched nothing here, and the silent filter said nothing either.
	/// </summary>
	[Test]
	public async ValueTask MailToMeDeliversToTheSender()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MailToMe");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		await parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@mail me=Me Subject/Me body."));

		var notifications = NotificationsTo(player.DbRef);

		await Assert.That(notifications).Contains(m => m!.StartsWith("MAIL: You sent a message to "));
		await Assert.That(notifications).Contains(m => m!.StartsWith("MAIL: You have received a message"));
	}

	/// <summary>extmail.c:1382 — an unmatched name is reported, not dropped.</summary>
	[Test]
	public async ValueTask MailToAnUnknownNameSaysSo()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MailToNobody");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		await parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@mail NoSuchPlayerAtAll=Subject/Body."));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedRendering(
			NotifyService,
			nameof(ErrorMessages.Notifications.MailNoSuchUniquePlayer),
			"No such unique player: NoSuchPlayerAtAll.",
			player.DbRef)).IsTrue();

		await Assert.That(NotificationsTo(player.DbRef))
			.DoesNotContain(m => m!.StartsWith("MAIL: You sent a message to "));
	}

	/// <summary>extmail.c:1337 — a doubled cookie is a literal, so a subject may contain a slash.</summary>
	[Test]
	public async ValueTask MailSubjectTakesADoubledSlashAsALiteral()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MailSlashSubj");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		await parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("@mail me=and//or/The body."));

		var mail = await Mediator.Send(new GetMailQuery(
			(await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).AsPlayer, 0, "INBOX"));

		await Assert.That(mail).IsNotNull();
		await Assert.That(mail!.Subject.ToPlainText()).IsEqualTo("and/or");
		await Assert.That(mail.Content.ToPlainText()).IsEqualTo("The body.");
	}

	/// <summary>
	/// extmail.h:71 — with no cookie the whole message is the body and the first SUBJECT_LEN (60)
	/// characters are the subject.
	/// </summary>
	[Test]
	public async ValueTask MailWithoutASubjectTakesSixtyCharacters()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MailImplicitSubj");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		var body = new string('x', 80);
		await parser.CommandParse(player.Handle, ConnectionService,
			MModule.single($"@mail me={body}"));

		var mail = await Mediator.Send(new GetMailQuery(
			(await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).AsPlayer, 0, "INBOX"));

		await Assert.That(mail).IsNotNull();
		await Assert.That(mail!.Subject.ToPlainText()).IsEqualTo(new string('x', 60));
		await Assert.That(mail.Content.ToPlainText()).IsEqualTo(body);
	}

	/// <summary>
	/// real_send_mail (extmail.c:127,138) gates the *sender's* confirmation on silent and notifies
	/// the recipient unconditionally. This was inverted.
	/// </summary>
	[Test]
	public async ValueTask MailSilentSuppressesTheSenderConfirmationNotTheDelivery()
	{
		var sender = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MailSilentFrom");
		var target = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MailSilentTo");
		var parser = WebAppFactoryArg.CommandParserFor(sender.DbRef, sender.Handle);

		await parser.CommandParse(sender.Handle, ConnectionService,
			MModule.single($"@mail/silent #{target.DbRef.Number}=Quiet/Body."));

		await Assert.That(NotificationsTo(sender.DbRef))
			.DoesNotContain(m => m!.StartsWith("MAIL: You sent"));
		await Assert.That(NotificationsTo(target.DbRef))
			.Contains(m => m!.Contains("You have"));
	}

	/// <summary>
	/// fun_mailsend calls do_mail_send (extmail.c:1466), so the function and the command cannot
	/// have different recipient rules. This pins them together.
	/// </summary>
	[Test]
	public async ValueTask MailSendFunctionResolvesMeLikeTheCommand()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MailSendFnMe");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		await parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("think [mailsend(me,Fn Subject/Fn body.)]"));

		var mail = await Mediator.Send(new GetMailQuery(
			(await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).AsPlayer, 0, "INBOX"));

		await Assert.That(mail).IsNotNull();
		await Assert.That(mail!.Subject.ToPlainText()).IsEqualTo("Fn Subject");
		await Assert.That(mail.Content.ToPlainText()).StartsWith("Fn body.");
	}

	/// <summary>
	/// silent=1 (extmail.c:1466) suppresses the sender's confirmation only; the delivery notice is
	/// outside that gate.
	/// </summary>
	[Test]
	public async ValueTask MailSendFunctionNotifiesTheRecipientButNotTheSender()
	{
		var sender = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MailSendFnFrom");
		var target = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MailSendFnTo");
		var parser = WebAppFactoryArg.CommandParserFor(sender.DbRef, sender.Handle);

		await parser.CommandParse(sender.Handle, ConnectionService,
			MModule.single($"think [mailsend(#{target.DbRef.Number},Fn Subject/Fn body.)]"));

		await Assert.That(NotificationsTo(target.DbRef)).Contains(m => m!.Contains("You have"));
		await Assert.That(NotificationsTo(sender.DbRef)).DoesNotContain(m => m!.StartsWith("MAIL: You sent"));
	}

	/// <summary>nosig is 0 at the fun_mailsend call site, so MAILSIGNATURE applies.</summary>
	[Test]
	public async ValueTask MailSendFunctionAppliesTheSendersSignature()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MailSendFnSig");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		await parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("&MAILSIGNATURE me=-- Regards"));
		await parser.CommandParse(player.Handle, ConnectionService,
			MModule.single("think [mailsend(me,Sig Subject/Sig body.)]"));

		var mail = await Mediator.Send(new GetMailQuery(
			(await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).AsPlayer, 0, "INBOX"));

		await Assert.That(mail).IsNotNull();
		await Assert.That(mail!.Content.ToPlainText()).Contains("-- Regards");
	}

	/// <summary>
	/// Delivering to nobody has two causes — no name resolved, or every resolved recipient refuses
	/// mail — and an empty recipient list cannot tell a caller which.
	/// </summary>
	[Test]
	public async ValueTask MailDistinguishesAnUnknownNameFromARefusedOne()
	{
		var sender = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MailRefusedFrom");
		var target = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MailRefusedTo");

		var targetParser = WebAppFactoryArg.CommandParserFor(target.DbRef, target.Handle);
		await targetParser.CommandParse(target.Handle, ConnectionService,
			MModule.single($"@lock/mail me=#{target.DbRef.Number}"));

		var parser = WebAppFactoryArg.CommandParserFor(sender.DbRef, sender.Handle);

		var refused = await parser.CommandParse(sender.Handle, ConnectionService,
			MModule.single($"@mail #{target.DbRef.Number}=Subject/Body."));
		var missing = await parser.CommandParse(sender.Handle, ConnectionService,
			MModule.single("@mail NoSuchPlayerAtAll=Subject/Body."));

		await Assert.That(refused.Message!.ToPlainText())
			.IsEqualTo(ErrorMessages.Returns.RecipientDoesNotAcceptMail);
		await Assert.That(missing.Message!.ToPlainText())
			.IsEqualTo(ErrorMessages.Returns.NoSuchPlayer);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask MaliasCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@malias add all=*"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "@MALIAS/")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}
}
