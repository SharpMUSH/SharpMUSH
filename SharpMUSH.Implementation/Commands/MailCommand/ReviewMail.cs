using Mediator;
using MarkupString;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class ReviewMail
{
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, ILocateService locateService, IMediator mediator, INotifyService notifyService, MString? arg0, MString? msgListArg, string[] switches)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var name = arg0?.ToPlainText() ?? string.Empty;

		// Review is of the mail the executor SENT (PennMUSH do_mail_review selects on the sender), so a
		// named player narrows it to what was sent to them — it never opens that player's own mailbox.
		if (string.IsNullOrWhiteSpace(name))
		{
			if (!string.IsNullOrWhiteSpace(msgListArg?.ToPlainText()))
			{
				await notifyService.Notify(executor, "MAIL: You must specify a player.", executor);
				return MarkupText.Empty;
			}

			return await ReviewSentAsync(notifyService, mediator, executor, msgListArg, recipient: null);
		}

		// extmail.c's lookup_player resolves "#1" as readily as a name, so a dbref must match too.
		var actualPlayer = await locateService.LocateAndNotifyIfInvalid(parser,
			executor, executor, name,
			LocateFlags.PlayersPreference |
			LocateFlags.MatchWildCardForPlayerName |
			LocateFlags.MatchOptionalWildCardForPlayerName |
			LocateFlags.OnlyMatchTypePreference |
			LocateFlags.AbsoluteMatch);

		if (actualPlayer is not (AnySharpObject and SharpPlayer recipient))
		{
			await notifyService.Notify(executor, $"MAIL: {name} not found.", executor);
			return MarkupText.Plain(ErrorMessages.Returns.NoSuchPlayer);
		}

		return await ReviewSentAsync(notifyService, mediator, executor, msgListArg, recipient);
	}

	private static async ValueTask<MString> ReviewSentAsync(INotifyService notifyService, IMediator mediator,
		AnySharpObject executor, MString? msgListArg, SharpPlayer? recipient)
		=> MessageListHelper.HandleSent(mediator, msgListArg, executor, recipient) switch
		{
			IAsyncEnumerable<SharpMail> mailList => await ReviewAsync(notifyService, executor, mailList),
			Error<string> error => MarkupText.Plain(error.Value)
		};

	private static async ValueTask<MString> ReviewAsync(INotifyService notifyService, AnySharpObject executor,
		IAsyncEnumerable<SharpMail> mailList)
	{
		var i = 0;

		await foreach (var actualMail in mailList)
		{
			i++;
			var output = await MailLayout.Message(actualMail, $"Sent message {i} ({actualMail.Folder})");
			await notifyService.Notify(executor, output, executor);
		}

		return MarkupText.Empty;
	}
}