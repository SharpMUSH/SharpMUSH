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

	/// <summary>The columns of a conversation; a narrow client loses the date, then the recipients.</summary>
	private static readonly ImmutableArray<TableColumn> Columns =
	[
		new(MarkupText.Plain("#")) { Alignment = Alignment.Right, Wrap = false },
		new(MarkupText.Plain("From")) { Min = 8, Max = 20 },
		new(MarkupText.Plain("To")) { Min = 8, Max = 24, Priority = 2 },
		new(MarkupText.Plain("Subject")) { Min = 10 },
		new(MarkupText.Plain("Sent")) { Wrap = false, Priority = 3 },
	];

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

		var rows = messages.Select(message => (ImmutableArray<Block>)
		[
			MarkupText.Plain(message.Position?.ToString() ?? "-"),
			MarkupText.Plain(message.From),
			MarkupText.Plain(string.Join(", ", message.To)),
			message.First.Subject,
			MarkupText.Plain(message.First.DateSent.ToString("ddd MMM dd HH:mm", CultureInfo.InvariantCulture)),
		]).ToArray();

		var count = messages.Count == 1 ? "1 message" : $"{messages.Count} messages";
		await notifyService.Notify(executor, ServerLayout.Build(ServerLayout.Section(
			MarkupText.Concat([MarkupText.Plain("Mail thread: "), messages[0].First.Subject]),
			ServerLayout.Listing(Columns, rows),
			new Rule(),
			new TextBlock(MarkupText.Plain($"{count}. Thread {thread}\n# is where you keep it; - is mail you sent and do not hold."))),
			78, look), executor);

		return MarkupText.Plain(thread.ToString());
	}
}
