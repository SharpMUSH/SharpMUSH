using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class RetractMail
{
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser, IExpandedObjectDataService objectDataService, ILocateService locateService, IMediator mediator, INotifyService notifyService, string target, string msgList)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var maybeLocate = await locateService.LocateAndNotifyIfInvalid(parser,
			executor, executor, target,
			LocateFlags.PlayersPreference | LocateFlags.OnlyMatchTypePreference);

		if (maybeLocate is not (AnySharpObject and SharpPlayer targetPlayer))
		{
			return MarkupText.Plain(ErrorMessages.Returns.NoSuchPlayer);
		}

		return await MessageListHelper.Handle(parser, objectDataService, mediator, notifyService, MarkupText.Plain(msgList), targetPlayer) switch
		{
			IAsyncEnumerable<SharpMail> foundMailList => await RetractAsync(mediator, notifyService, executor, foundMailList),
			Error<string> error => MarkupText.Plain(error.Value)
		};
	}

	/// <summary>Deletes each message in the list that nobody has read yet.</summary>
	private static async ValueTask<MString> RetractAsync(IMediator mediator, INotifyService notifyService,
		AnySharpObject executor, IAsyncEnumerable<SharpMail> foundMailList)
	{
		var length = 0;
		await foreach (var mail in foundMailList)
		{
			if (!mail.Fresh)
			{
				await notifyService.Notify(executor, "MAIL: Mail already read.", executor);
				continue;
			}

			length++;
			await mediator.Send(new DeleteMailCommand(mail));
		}

		return MarkupText.Plain(length.ToString());
	}
}