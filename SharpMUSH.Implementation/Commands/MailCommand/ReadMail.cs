using Mediator;
using MarkupString;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class ReadMail
{
	/// <summary>
	/// <c>do_mail_read</c> (<c>extmail.c:670</c>): every message <paramref name="msgList"/> names, in the current
	/// folder or the one a <c>&lt;folder&gt;:</c> prefix names, each headed with its folder number and position.
	/// </summary>
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser,
		IExpandedObjectDataService objectDataService,
		IMediator mediator,
		INotifyService notifyService,
		MString? msgList, string[] switches)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		if (executor is not SharpPlayer player)
		{
			throw new InvalidOperationException("@mail reads a player's own mail, and its dispatcher routes only players here.");
		}

		return await MessageListHelper.Handle(parser, objectDataService, mediator, notifyService, msgList, executor) switch
		{
			IAsyncEnumerable<SharpMail> list => await ReadAsync(objectDataService, mediator, notifyService, player, list),
			Error<string> error => await MessageListHelper.RefuseAsync(notifyService, executor, error.Value)
		};
	}

	private static async ValueTask<MString> ReadAsync(IExpandedObjectDataService objectDataService, IMediator mediator,
		INotifyService notifyService, SharpPlayer player, IAsyncEnumerable<SharpMail> list)
	{
		var executor = new AnySharpObject(player);
		var folders = await MailFolders.LoadAsync(objectDataService, player);
		var messages = await MessageListHelper.WithPositionsAsync(mediator, player, folders, list);
		if (messages.Count == 0)
		{
			await notifyService.Notify(executor, "MAIL: You don't have that many matching messages!", executor);
			return MarkupText.Plain(ErrorMessages.Returns.NoSuchMail);
		}

		var outputs = new List<MString>();
		foreach (var (actualMail, folder, number) in messages)
		{
			var output = await MailLayout.Message(actualMail, $"Message {folder}:{number}");
			await notifyService.Notify(executor, output, executor);
			outputs.Add(output);

			await mediator.Send(new UpdateMailCommand(actualMail, MailUpdate.ReadEdit(true)));
		}

		return MarkupText.Join(MarkupText.NewLine, outputs);
	}
}
