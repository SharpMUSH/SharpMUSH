using System.Globalization;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.MailCommand;

/// <summary>
/// TinyMUX's <c>@mail/reply</c> and <c>@mail/replyall</c>, with <c>/quote</c>: an answer to one message of the
/// reader's own, under its subject marked <c>Re: </c>, in its conversation. <c>/reply</c> mails the sender;
/// <c>/replyall</c> mails the sender and everyone the sender addressed, a mail alias as the alias. The message is
/// named by number, or by its id (<see cref="SharpMail.Message"/>), which a renumbered mailbox does not change.
/// </summary>
public static class ReplyMail
{
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, IMediator mediator, INotifyService notifyService,
		MailDelivery.Services delivery, MString msgList, MString message, string[] switches)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		if (executor is not SharpPlayer player)
		{
			throw new InvalidOperationException("@mail/reply answers a player's own mail, and its dispatcher routes only players here.");
		}

		// A message's id names the same message however the mailbox is renumbered.
		if (Guid.TryParse(msgList.ToPlainText().Trim(), out var id))
		{
			return await mediator.CreateStream(new GetAllMailListQuery(player))
				.FirstOrDefaultAsync(mail => mail.Message == id) switch
			{
				{ } original => await ReplyAsync(parser, mediator, notifyService, delivery, player, original, message, switches),
				null => await MessageListHelper.RefuseAsync(notifyService, executor, "MAIL: You can't reply to nonexistent mail.")
			};
		}

		return await MessageListHelper.Handle(parser, delivery.ObjectData, mediator, notifyService, msgList, executor) switch
		{
			IAsyncEnumerable<SharpMail> list => await OneMessageAsync(list) switch
			{
				SharpMail original => await ReplyAsync(parser, mediator, notifyService, delivery, player, original, message, switches),
				Error<string> error => await MessageListHelper.RefuseAsync(notifyService, executor, error.Value)
			},
			Error<string> error => await MessageListHelper.RefuseAsync(notifyService, executor, error.Value)
		};
	}

	/// <summary>The one message a reply answers: a list naming none, or more than one, is refused.</summary>
	private static async ValueTask<Result<SharpMail>> OneMessageAsync(IAsyncEnumerable<SharpMail> list)
		=> await list.Take(2).ToListAsync() switch
		{
			[var original] => original,
			[] => new Error<string>("MAIL: You can't reply to nonexistent mail."),
			_ => new Error<string>("MAIL: You can only reply to one message at a time.")
		};

	private static async ValueTask<MString> ReplyAsync(IMUSHCodeParser parser, IMediator mediator,
		INotifyService notifyService, MailDelivery.Services delivery, SharpPlayer replier, SharpMail original,
		MString message, string[] switches)
	{
		var everyone = switches.Contains("REPLYALL");
		var silent = switches.Contains("SILENT");
		var subject = SendMail.ReplySubject(original.Subject);
		var self = new AnySharpObject(replier);

		var recipients = new List<SendMail.Recipient>();
		var addressed = new List<string>();
		var reached = new HashSet<int>();

		void Add(SharpPlayer player, bool quiet)
		{
			if (player.Object.DBRef.Number != replier.Object.DBRef.Number && reached.Add(player.Object.DBRef.Number))
			{
				recipients.Add(new SendMail.Recipient(player, quiet, subject));
			}
		}

		// /reply needs a sender to answer; /replyall still reaches the others when there is none.
		if (!everyone)
		{
			if (await SendMail.AnsweredAsync(notifyService, self, original) is not SendMail.Answered answered)
			{
				return MarkupText.Plain(ErrorMessages.Returns.NoSuchPlayer);
			}

			recipients.Add(new SendMail.Recipient(answered.Sender, silent, subject));
			addressed.Add(answered.Sender.Object.DBRef.ToString());
		}
		else
		{
			if (!original.Anonymous && await original.From.WithCancellation(CancellationToken.None) is AnySharpObject and SharpPlayer from)
			{
				Add(from, silent);
				if (from.Object.DBRef.Number != replier.Object.DBRef.Number)
				{
					addressed.Add(from.Object.DBRef.ToString());
				}
			}

			await AddAddressedAsync(parser, mediator, notifyService, delivery, replier, original, silent, Add, addressed);

			if (recipients.Count == 0)
			{
				await notifyService.Notify(self, "MAIL: There is no one else to reply to.", self);
				return MarkupText.Plain(ErrorMessages.Returns.NoSuchPlayer);
			}
		}

		var body = switches.Contains("QUOTE")
			? MarkupText.Concat([message, MarkupText.Plain(await QuoteAsync(original))])
			: message;

		return await SendMail.DeliverAsync(parser, delivery, self, recipients, body,
			signed: !switches.Contains("NOSIG"), switches.Contains("URGENT"),
			MailDelivery.Threading.Answering(original, addressed));
	}

	/// <summary>
	/// Everyone <paramref name="original"/>'s sender addressed, other than the replier: each player who still exists,
	/// and each alias the replier may use, mailed to its members as a send to the alias would be.
	/// </summary>
	private static async ValueTask AddAddressedAsync(IMUSHCodeParser parser, IMediator mediator,
		INotifyService notifyService, MailDelivery.Services delivery, SharpPlayer replier, SharpMail original, bool silent,
		Action<SharpPlayer, bool> add, List<string> addressed)
	{
		var self = new AnySharpObject(replier);
		var aliases = new MailAliases.Services(delivery.Mediator, delivery.Notify, delivery.Permissions,
			delivery.Connections, parser.ServiceProvider.GetRequiredService<IHouseLookService>());

		foreach (var entry in original.Addressed)
		{
			if (entry.StartsWith(MailAliases.Token))
			{
				if (await MailAliases.RecipientsAsync(aliases, self, entry) is MailAliases.Recipients members)
				{
					foreach (var member in members.Members)
					{
						add(member, silent || members.Silent);
					}

					addressed.Add(members.Name);
				}
				else
				{
					await notifyService.NotifyLocalized(self, nameof(ErrorMessages.Notifications.MailNoSuchUniquePlayer), self, entry);
				}

				continue;
			}

			// A player addressed by objid: one destroyed since, or whose number went to someone else, is left out.
			if (DBRef.TryParse(entry, out var parsed) && parsed is { } reference
					&& reference.Number != replier.Object.DBRef.Number
					&& await mediator.Send(new GetObjectNodeQuery(reference)) is AnySharpObject and SharpPlayer player
					&& player.Object.DBRef.SameObjectAs(reference))
			{
				add(player, silent);
				addressed.Add(player.Object.DBRef.ToString());
			}
		}
	}

	/// <summary>The original under the reply, each line marked <c>&gt; </c>, below a line naming who wrote it and when.</summary>
	private static async ValueTask<string> QuoteAsync(SharpMail original)
	{
		var from = await MailLayout.SenderName(original);
		var when = original.DateSent.ToString("ddd MMM dd HH:mm yyyy", CultureInfo.InvariantCulture);
		var lines = original.Content.ToPlainText().Split('\n').Select(line => "> " + line.TrimEnd('\r'));
		return $"\n\nOn {when}, {from} wrote:\n{string.Join('\n', lines)}";
	}
}
