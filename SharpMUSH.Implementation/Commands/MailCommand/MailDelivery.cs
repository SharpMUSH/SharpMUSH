using DotNext.Threading;
using Mediator;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.MailCommand;

/// <summary>
/// The one way a message reaches a mailbox. <c>@mail</c>, <c>mailsend()</c> and <c>@mail/fwd</c> all end
/// here, as every PennMUSH sender ends in <c>send_mail</c> (<c>extmail.c:1472</c>), which delivers
/// through <c>real_send_mail</c> (<c>extmail.c:1557</c>). Every refusal is decided before the store is
/// written, so a message is either delivered with its notices or not delivered at all.
/// </summary>
public static class MailDelivery
{
	public sealed record Services(
		IPermissionService Permissions,
		IMediator Mediator,
		INotifyService Notify,
		IDidItService DidIt,
		IOptionsWrapper<SharpMUSHOptions> Configuration);

	/// <summary>A message on its way to one or more mailboxes.</summary>
	/// <param name="Body">The text as the sender wrote it, which is what the <c>clear</c> check reads.</param>
	/// <param name="Signature">Appended to <paramref name="Body"/> when stored; empty for a forward.</param>
	public sealed record Letter(MString Subject, MString Body, MString Signature, bool Urgent, bool Forwarded);

	private const string Inbox = "INBOX";

	/// <summary>
	/// PennMUSH <c>send_mail</c>. Returns the mailboxes the message landed in, which is empty when it was
	/// refused. <paramref name="silent"/> suppresses the sender's confirmation and refusals, never the
	/// recipient's notice.
	/// </summary>
	public static async ValueTask<SharpPlayer[]> SendAsync(IMUSHCodeParser parser, Services services,
		AnySharpObject sender, SharpPlayer target, Letter letter, bool silent)
		=> await DeliverAsync(parser, services, sender, target, letter, silent) ? [target] : [];

	/// <summary>PennMUSH <c>real_send_mail</c>: one message into one mailbox, or a refusal.</summary>
	private static async ValueTask<bool> DeliverAsync(IMUSHCodeParser parser, Services services,
		AnySharpObject sender, SharpPlayer target, Letter letter, bool silent)
	{
		var recipient = new AnySharpObject(target);

		// extmail.c:1570 — unconditional, silent or not.
		if (letter.Body.ToPlainText().Equals("clear", StringComparison.OrdinalIgnoreCase))
		{
			await services.Notify.Notify(sender, "MAIL: You probably don't wanna send mail saying 'clear'.", sender);
			return false;
		}

		// extmail.c:1578 — fail_lock shows the lock's own FAILURE attribute, silent or not; only the
		// default wording is withheld from a silent send.
		if (!await sender.IsPriv() && !await services.Permissions.PassesLock(sender, recipient, LockType.Mail))
		{
			await services.DidIt.FailLock(parser, sender, recipient, LockType.Mail,
				silent ? null : MarkupText.Plain($"MAIL: {target.Object.Name} is not accepting mail from you right now."));
			return false;
		}

		var inboxCount = await services.Mediator.CreateStream(new GetMailListQuery(target, Inbox)).CountAsync();

		await services.Mediator.Send(new SendMailCommand(sender.Object(), target, new SharpMail
		{
			DateSent = DateTimeOffset.UtcNow,
			Fresh = true,
			Read = false,
			Tagged = false,
			Urgent = letter.Urgent,
			Cleared = false,
			Forwarded = letter.Forwarded,
			Folder = Inbox,
			Content = letter.Signature.Length > 0
				? MarkupText.Concat([letter.Body, MarkupText.NewLine, letter.Signature])
				: letter.Body,
			Subject = letter.Subject,
			From = new AsyncLazy<AnyOptionalSharpObject>(_ => Task.FromResult(sender.WithNoneOption())),
		}));

		if (!silent)
		{
			// extmail.c:1680 — can_mail_to(target, player): would a reply get back?
			var canReply = await recipient.IsPriv() || await services.Permissions.PassesLock(recipient, sender, LockType.Mail);
			await services.Notify.Notify(sender, canReply
				? $"MAIL: You sent your message to {target.Object.Name}."
				: $"MAIL: You sent your message to {target.Object.Name}, but they can't mail you!", sender);
		}

		await services.Notify.Notify(target,
			$"MAIL: You have a new message ({inboxCount + 1}) from {sender.Object().Name}.", sender);

		// extmail.c:1700 — a privileged recipient's AMAIL, never for mail to oneself, queued by did_it.
		if (services.Configuration.CurrentValue.Attribute.AMail
				&& sender.Object().DBRef != target.Object.DBRef
				&& await recipient.IsPriv())
		{
			await services.DidIt.DidIt(parser, new DidItRequest(sender, recipient, AWhat: "AMAIL"));
		}

		return true;
	}
}
