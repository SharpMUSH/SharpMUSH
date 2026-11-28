using System.Collections.Immutable;
using System.Globalization;
using Mediator;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.MailCommand;

/// <summary>
/// <c>@mail/thread &lt;msg&gt;|&lt;thread id&gt;</c>: one conversation as the reader can see it, a row for each message
/// in it the reader sent or holds, oldest first.
/// </summary>
public static class ThreadMail
{
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, IExpandedObjectDataService objectDataService,
		IMediator mediator, INotifyService notifyService, MString? arg0)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		if (executor is not SharpPlayer player)
		{
			throw new InvalidOperationException("@mail/thread reads a player's own mail, and its dispatcher routes only players here.");
		}

		return await ThreadOfAsync(parser, objectDataService, mediator, notifyService, executor, arg0) switch
		{
			Guid thread => await ShowAsync(parser.HouseLook(), objectDataService, mediator, notifyService, player, thread),
			Error<string> error => await MessageListHelper.RefuseAsync(notifyService, executor, error.Value)
		};
	}

	/// <summary>The conversation an argument names: a thread id as it is, or the thread of the one message a list names.</summary>
	private static async ValueTask<Result<Guid>> ThreadOfAsync(IMUSHCodeParser parser,
		IExpandedObjectDataService objectDataService, IMediator mediator, INotifyService notifyService,
		AnySharpObject executor, MString? arg0)
	{
		var text = arg0?.ToPlainText().Trim() ?? string.Empty;
		if (text.Length == 0)
		{
			return new Error<string>("MAIL: Which message's conversation do you want to see?");
		}

		if (Guid.TryParse(text, out var thread))
		{
			return thread;
		}

		return await MessageListHelper.Handle(parser, objectDataService, mediator, notifyService, arg0, executor) switch
		{
			IAsyncEnumerable<SharpMail> list => await list.Take(2).ToListAsync() switch
			{
				[{ Thread: { } found }] => found,
				[{ Message: { } own }] => own,
				[_] => new Error<string>("MAIL: That message is in no conversation."),
				[] => new Error<string>("MAIL: You don't have that many matching messages!"),
				_ => new Error<string>("MAIL: Name one message to see its conversation.")
			},
			Error<string> error => error
		};
	}

	private static async ValueTask<MString> ShowAsync(LayoutTheme look, IExpandedObjectDataService objectDataService,
		IMediator mediator, INotifyService notifyService, SharpPlayer player, Guid thread)
	{
		var executor = new AnySharpObject(player);
		var messages = await MailThreads.ReadAsync(objectDataService, mediator, player, thread);
		if (messages.Count == 0)
		{
			await notifyService.Notify(executor, "MAIL: You have no mail in that conversation.", executor);
			return MarkupText.Plain(ErrorMessages.Returns.NoSuchMail);
		}

		var topic = Topic(messages[0].First.Subject.ToPlainText());
		var count = messages.Count == 1 ? "1 message" : $"{messages.Count} messages";
		await notifyService.Notify(executor, ServerLayout.Build(ServerLayout.Section(
			MarkupText.Concat([MarkupText.Plain("Mail thread: "), messages[0].First.Subject]),
			new Tree(Items(messages, topic)),
			new Rule(),
			new TextBlock(MarkupText.Plain($"{count}. Thread {thread}\nEach reply is under the message it answers. A number is where you keep\nthat message; one without is mail you sent and do not hold."))),
			78, look), executor);

		return MarkupText.Plain(thread.ToString());
	}

	/// <summary>
	/// <paramref name="messages"/>, in reply order with their depths, as the items of a tree: each reply under the
	/// message it answers.
	/// </summary>
	private static ImmutableArray<TreeItem> Items(List<MailThreads.Message> messages, string topic)
	{
		var index = 0;
		return Level(0);

		ImmutableArray<TreeItem> Level(int depth)
		{
			var items = ImmutableArray.CreateBuilder<TreeItem>();
			while (index < messages.Count && messages[index].Depth == depth)
			{
				var message = messages[index++];
				items.Add(new TreeItem(Line(message, topic), Level(depth + 1)));
			}

			return items.ToImmutable();
		}
	}

	/// <summary>
	/// One message: where the reader keeps it, when they do, who wrote to whom and when, and its subject when it is not the
	/// conversation's own.
	/// </summary>
	private static TextBlock Line(MailThreads.Message message, string topic)
	{
		string[] where = message.Position is { } position ? [position.ToString()] : [];
		var head = MarkupText.Plain(string.Join(" ", [.. where, message.From, "to", string.Join(", ", message.To), "·",
			message.First.DateSent.ToString("ddd MMM dd HH:mm", CultureInfo.InvariantCulture)]));
		return new TextBlock(Topic(message.First.Subject.ToPlainText()) == topic
			? head
			: MarkupText.Concat([head, MarkupText.Plain("\n"), message.First.Subject]));
	}

	/// <summary>A subject without the <c>Re: </c> a reply adds, to tell whether a reply kept it.</summary>
	private static string Topic(string subject)
		=> subject.StartsWith(SendMail.ReplyPrefix, StringComparison.OrdinalIgnoreCase)
			? subject[SendMail.ReplyPrefix.Length..].Trim()
			: subject.Trim();
}
