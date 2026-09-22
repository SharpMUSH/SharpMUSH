using DotNext.Threading;
using Mediator;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SharpMUSH.Implementation.Commands.MailCommand;

/// <summary>
/// The one way a message reaches a mailbox. <c>@mail</c>, <c>mailsend()</c> and <c>@mail/fwd</c> all end
/// here, as every PennMUSH sender ends in <c>send_mail</c> (<c>extmail.c:1472</c>), which delivers
/// through <c>real_send_mail</c> (<c>extmail.c:1557</c>). Every refusal is decided before the store is
/// written, so a message is either delivered with its notices or not delivered at all.
/// </summary>
public static partial class MailDelivery
{
	public sealed record Services(
		IPermissionService Permissions,
		IMediator Mediator,
		INotifyService Notify,
		IDidItService DidIt,
		IAttributeService Attributes,
		IExpandedObjectDataService ObjectData,
		IOptionsWrapper<SharpMUSHOptions> Configuration);

	/// <summary>A message on its way to one or more mailboxes.</summary>
	/// <param name="Body">The text as the sender wrote it, which is what the <c>clear</c> check reads.</param>
	/// <param name="Signature">Appended to <paramref name="Body"/> when stored; empty for a forward.</param>
	public sealed record Letter(MString Subject, MString Body, MString Signature, bool Urgent, bool Forwarded);

	private const string Inbox = "INBOX";

	/// <summary><c>is_objid</c> (<c>src/parse.c:351</c>): what a forward list may name.</summary>
	[GeneratedRegex(@"^#-?\d+(?::\d+)?$")]
	private static partial Regex ObjidPattern();

	/// <summary><c>extmail.c:333</c> — a folder name is alphanumeric.</summary>
	[GeneratedRegex(@"^[A-Za-z0-9]+$")]
	private static partial Regex FolderNamePattern();

	/// <summary>
	/// Set while a MAILFILTER is being evaluated. Mail that filter sends is delivered without running
	/// filters, so a filter that mails its owner cannot recurse — PennMUSH does not bound this and the
	/// captured run crashed the server.
	/// </summary>
	private static readonly AsyncLocal<bool> Filtering = new();

	/// <summary>
	/// PennMUSH <c>send_mail</c>. Returns the mailboxes the message landed in, which is empty when it was
	/// refused. <paramref name="silent"/> suppresses the sender's confirmation and refusals, never the
	/// recipient's notice.
	/// </summary>
	/// <remarks>
	/// A <c>MAILFORWARDLIST</c> on <paramref name="target"/> replaces delivery to it (<c>extmail.c:1483</c>):
	/// each listed player that passes <see cref="MayForwardTo"/> gets the message silently, and the list's
	/// owner hears about each one that does not. The targets' own lists are not read — "don't check
	/// mailforward further" (<c>extmail.c:1479</c>) — which is what keeps two lists naming each other from
	/// bouncing a message.
	/// </remarks>
	public static async ValueTask<SharpPlayer[]> SendAsync(IMUSHCodeParser parser, Services services,
		AnySharpObject sender, SharpPlayer target, Letter letter, bool silent)
	{
		if (await ForwardListAsync(services, target) is not { } forwardList)
		{
			return await DeliverAsync(parser, services, sender, target, letter, silent) ? [target] : [];
		}

		var delivered = new List<SharpPlayer>();
		foreach (var entry in forwardList.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			if (!ObjidPattern().IsMatch(entry))
			{
				continue;
			}

			switch (await ForwardTargetAsync(services, entry))
			{
				case AnySharpObject and SharpPlayer forward when await MayForwardTo(services, target, forward):
					if (await DeliverAsync(parser, services, sender, forward, letter, silent: true))
					{
						delivered.Add(forward);
					}

					break;
				case AnySharpObject other:
					await services.Notify.Notify(target, $"Failed attempt to forward @mail to #{other.Object().DBRef.Number}");
					break;
				default:
					await services.Notify.Notify(target, "Failed attempt to forward @mail to #-1");
					break;
			}
		}

		if (!silent)
		{
			await services.Notify.Notify(sender, delivered.Count > 0
				? $"MAIL: You sent your message to {target.Object.Name}."
				: $"MAIL: Your message was not sent to {target.Object.Name} due to a mail forwarding problem.", sender);
		}

		return [.. delivered];
	}

	/// <summary><c>atr_get_noparent(target, "MAILFORWARDLIST")</c>: a parent's list does not forward.</summary>
	private static async ValueTask<string?> ForwardListAsync(Services services, SharpPlayer target)
	{
		var owner = new AnySharpObject(target);
		return await services.Attributes.GetAttributeAsync(owner, owner, "MAILFORWARDLIST",
				IAttributeService.AttributeMode.Read, parent: false) is SharpAttribute[] chain
			? chain.Last().Value.ToPlainText()
			: null;
	}

	/// <summary><c>parse_objid</c>: an objid whose creation time does not match names nothing.</summary>
	private static async ValueTask<AnyOptionalSharpObject> ForwardTargetAsync(Services services, string entry)
	{
		if (!DBRef.TryParse(entry, out var dbref) || dbref is not { } reference)
		{
			return new None();
		}

		return await services.Mediator.Send(new GetObjectNodeQuery(reference)) switch
		{
			AnySharpObject found when found.Object().DBRef.SameObjectAs(reference) => found,
			_ => new None()
		};
	}

	/// <summary>
	/// <c>Can_MailForward</c> (<c>hdrs/mushdb.h:130</c>): the list's owner controls the target, or the
	/// target has <em>set</em> a mailforward lock the owner passes. An unset lock evaluates true, so its
	/// verdict alone would let anyone fill another player's mailbox.
	/// </summary>
	private static async ValueTask<bool> MayForwardTo(Services services, SharpPlayer owner, SharpPlayer forward)
	{
		var from = new AnySharpObject(owner);
		var to = new AnySharpObject(forward);

		return await services.Permissions.Controls(from, to)
					 || (forward.Object.Locks.ContainsKey(nameof(LockType.MailForward))
							 && await services.Permissions.PassesLock(from, to, LockType.MailForward));
	}

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

		// extmail.c:1593 — only the inbox counts, and no sender, however privileged, passes a full one.
		var inboxCount = await services.Mediator.CreateStream(new GetMailListQuery(target, Inbox)).CountAsync();
		if (inboxCount >= await MailLimitAsync(services, target))
		{
			if (!silent)
			{
				await services.Notify.Notify(sender, $"MAIL: {target.Object.Name}'s mailbox is full. Can't send.", sender);
			}

			return false;
		}

		// Chosen before the store, which hands back no id to move the message by afterwards, so the
		// message is written once into the folder it belongs in.
		var (folder, filterNotice) = await FolderForAsync(parser, services, sender, target, letter, inboxCount + 1);

		await services.Mediator.Send(new SendMailCommand(sender.Object(), target, new SharpMail
		{
			DateSent = DateTimeOffset.UtcNow,
			Fresh = true,
			Read = false,
			Tagged = false,
			Urgent = letter.Urgent,
			Cleared = false,
			Forwarded = letter.Forwarded,
			Folder = folder,
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

		if (filterNotice is not null)
		{
			await services.Notify.Notify(target, filterNotice);
		}

		// extmail.c:1700 — a privileged recipient's AMAIL, never for mail to oneself, queued by did_it.
		if (services.Configuration.CurrentValue.Attribute.AMail
				&& sender.Object().DBRef != target.Object.DBRef
				&& await recipient.IsPriv())
		{
			await services.DidIt.DidIt(parser, new DidItRequest(sender, recipient, AWhat: "AMAIL"));
		}

		return true;
	}

	/// <summary><c>extmail.c:1547</c> — the hard cap a MAILQUOTA cannot exceed.</summary>
	private const int QuotaCeiling = 50000;

	/// <summary>
	/// <c>mail_limit</c> (<c>extmail.c:1534</c>): the recipient's MAILQUOTA, parents included, when it is a
	/// positive integer, capped at <see cref="QuotaCeiling"/>; otherwise the configured <c>mail_limit</c>.
	/// </summary>
	private static async ValueTask<long> MailLimitAsync(Services services, SharpPlayer target)
	{
		var configured = services.Configuration.CurrentValue.Limit.MailLimit;
		var owner = new AnySharpObject(target);

		return await services.Attributes.GetAttributeAsync(owner, owner, "MAILQUOTA",
				IAttributeService.AttributeMode.Read, parent: true) is SharpAttribute[] chain
			&& int.TryParse(chain.Last().Value.ToPlainText(), NumberStyles.Integer, CultureInfo.InvariantCulture,
				out var quota)
			&& quota > 0
				? Math.Min(quota, QuotaCeiling)
				: configured;
	}

	/// <summary>
	/// <c>filter_mail</c> (<c>extmail.c:3289</c>): the recipient's MAILFILTER, parents included, runs as the
	/// recipient with the sender as enactor, and a non-empty result names the folder the message is filed in.
	/// Returns the folder and the notice filing it produced, if any.
	/// </summary>
	private static async ValueTask<(string Folder, string? Notice)> FolderForAsync(IMUSHCodeParser parser,
		Services services, AnySharpObject sender, SharpPlayer target, Letter letter, int messageNumber)
	{
		if (Filtering.Value)
		{
			return (Inbox, null);
		}

		Filtering.Value = true;
		try
		{
			var recipient = new AnySharpObject(target);
			var arguments = new Dictionary<string, CallState>
			{
				["0"] = new(MarkupText.Plain($"#{sender.Object().DBRef.Number}")),
				["1"] = new(letter.Subject),
				["2"] = new(letter.Body),
				["3"] = new(MarkupText.Plain((letter.Urgent ? "U" : string.Empty) + (letter.Forwarded ? "F" : string.Empty)))
			};

			var result = await parser.With(
				state => state with { Executor = target.Object.DBRef, Enactor = sender.Object().DBRef, Caller = target.Object.DBRef },
				filterParser => services.Attributes.EvaluateAttributeFunctionAsync(filterParser, recipient, recipient,
					"MAILFILTER", arguments, evalParent: true, ignorePermissions: true));

			var folder = result.ToPlainText().Trim();
			if (folder.Length == 0)
			{
				return (Inbox, null);
			}

			if (!FolderNamePattern().IsMatch(folder))
			{
				return (Inbox, "MAIL: Invalid folder specification");
			}

			if (folder.Equals(Inbox, StringComparison.OrdinalIgnoreCase))
			{
				return (Inbox, $"MAIL: Msg {messageNumber} filed in folder {Inbox}.");
			}

			// As @mail/file does, filing into a folder makes it one of the player's folders.
			var folders = await services.ObjectData.GetExpandedDataAsync<ExpandedMailData>(target.Object);
			await services.ObjectData.SetExpandedDataAsync(
				new ExpandedMailData(Folders: [.. (folders?.Folders ?? []).Append(folder).Distinct()]),
				target.Object, ignoreNull: true);

			return (folder, $"MAIL: Msg {messageNumber} filed in folder {folder}.");
		}
		finally
		{
			Filtering.Value = false;
		}
	}
}
