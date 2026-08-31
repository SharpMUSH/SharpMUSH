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
	/// PennMUSH resolves each @mail recipient with
	/// <c>match_result(player, current, TYPE_PLAYER, MAT_ME | MAT_ABSOLUTE | MAT_PLAYER)</c>
	/// (extmail.c:1379), so <c>me</c> is a recipient exactly like <c>#dbref</c> or a player name.
	/// SharpMUSH looked the name up as a player name only, so <c>@mail me=...</c> matched nothing —
	/// and, because the unresolved entry was silently filtered out, said nothing either.
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

	/// <summary>
	/// extmail.c:1382 — when nothing matches, PennMUSH says <c>No such unique player: %s.</c>
	/// SharpMUSH dropped the unmatched name on the floor and reported success for the empty list.
	/// </summary>
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

	/// <summary>
	/// extmail.c:1337 — a doubled subject cookie is a literal <c>/</c> and does not end the subject;
	/// only a single one does. SharpMUSH split on the first <c>/</c> unconditionally, so a subject
	/// could not contain one at all.
	/// </summary>
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
	/// SUBJECT_LEN is 60 (extmail.h:71). With no cookie the whole message is the body and the first
	/// SUBJECT_LEN characters are the subject; SharpMUSH truncated at 20.
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
	/// the recipient unconditionally. SharpMUSH had it exactly inverted, which also meant
	/// <c>mailsend()</c> — which PennMUSH calls with silent=1 — could not notify its recipient.
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
	/// fun_mailsend is not its own mail implementation in PennMUSH — it calls do_mail_send, the same
	/// function @mail uses (extmail.c:1466). SharpMUSH's mailsend() had a third one, and while its
	/// bare PlayersPreference locate did resolve <c>me</c>, nothing held the two implementations to
	/// the same recipient rules. This pins them together now that there is only one.
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
	/// do_mail_send is called with silent=1 (extmail.c:1466), which suppresses the sender's
	/// confirmation only — the recipient's delivery notice is outside that gate. mailsend() used to
	/// notify nobody at all.
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

	/// <summary>
	/// nosig is 0 at the fun_mailsend call site, so a signature applies exactly as it does for the
	/// command. mailsend() never read MAILSIGNATURE.
	/// </summary>
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
