using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.MailCommand;

/// <summary>A conversation as one reader sees it, shared by <c>@mail/thread</c> and the portal.</summary>
public static class MailThreads
{
	/// <summary>
	/// One message of a conversation: the copies of one send the reader sent or holds.
	/// </summary>
	/// <param name="First">The first such copy, which carries the subject, text and time all copies share.</param>
	/// <param name="Position">Where the reader keeps its own copy, when they hold one.</param>
	/// <param name="From">Who it is shown as from.</param>
	/// <param name="To">Who it was addressed to: players by name, mail aliases as <c>+alias</c>.</param>
	/// <param name="Sent">The reader sent it.</param>
	public sealed record Message(SharpMail First, MailPosition? Position, string From, IReadOnlyList<string> To, bool Sent);

	/// <summary>
	/// The reader's view of <paramref name="thread"/>, a message for each send, in the order they were sent. A send the
	/// reader neither sent nor received is not in it.
	/// </summary>
	public static async ValueTask<List<Message>> ReadAsync(IExpandedObjectDataService objectDataService, IMediator mediator,
		SharpPlayer reader, Guid thread)
	{
		var copies = await mediator.CreateStream(new GetMailThreadQuery(reader.Object, thread)).ToListAsync();
		if (copies.Count == 0)
		{
			return [];
		}

		var folders = await MailFolders.LoadAsync(objectDataService, reader);
		var positions = (await MessageListHelper.WithPositionsAsync(mediator, reader, folders,
				copies.Where(copy => copy.Held).Select(copy => copy.Mail).ToAsyncEnumerable()))
			.ToDictionary(position => position.Mail.Id ?? string.Empty);

		var names = new Dictionary<string, string>(StringComparer.Ordinal);
		var messages = new List<Message>();
		foreach (var send in copies.GroupBy(copy => copy.Mail.Message?.ToString() ?? copy.Mail.Id ?? string.Empty))
		{
			var first = send.First().Mail;
			var held = send.FirstOrDefault(copy => copy.Held);
			MailPosition? position = held is not null && positions.TryGetValue(held.Mail.Id ?? string.Empty, out var found)
				? found
				: null;
			var sent = send.Any(copy => copy.Sent);
			var from = sent ? reader.Object.Name : await MailLayout.SenderName(first);
			messages.Add(new Message(first, position, from, await RecipientsAsync(mediator, reader, send, names), sent));
		}

		return messages;
	}

	/// <summary>
	/// Who a send was addressed to. Mail from before the store kept that names the mailboxes the reader can see it in:
	/// every one for mail the reader sent, the reader's own otherwise.
	/// </summary>
	private static async ValueTask<IReadOnlyList<string>> RecipientsAsync(IMediator mediator, SharpPlayer reader,
		IEnumerable<MailThreadCopy> send, Dictionary<string, string> names)
	{
		var copies = send.ToList();
		if (copies[0].Mail.Addressed is { Count: > 0 } addressed)
		{
			var list = new List<string>(addressed.Count);
			foreach (var entry in addressed)
			{
				list.Add(entry.StartsWith(MailAliases.Token) ? entry : await NameAsync(mediator, entry, names));
			}

			return list;
		}

		var mailboxes = new List<string>();
		foreach (var copy in copies)
		{
			var name = copy.Mail.To is null
				? reader.Object.Name
				: (await copy.Mail.To.WithCancellation(CancellationToken.None)).Object()?.Name ?? "!Purged!";
			if (!mailboxes.Contains(name))
			{
				mailboxes.Add(name);
			}
		}

		return mailboxes;
	}

	/// <summary>A player addressed by objid, by their name now; one gone since is <c>!Purged!</c>, as a sender would be.</summary>
	private static async ValueTask<string> NameAsync(IMediator mediator, string entry, Dictionary<string, string> names)
	{
		if (names.TryGetValue(entry, out var known))
		{
			return known;
		}

		var name = DBRef.TryParse(entry, out var parsed) && parsed is { } reference
			&& await mediator.Send(new GetObjectNodeQuery(reference)) is AnySharpObject found
			&& found.Object().DBRef.SameObjectAs(reference)
				? found.Object().Name
				: "!Purged!";
		names[entry] = name;
		return name;
	}
}
