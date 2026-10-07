using SharpMUSH.Library.Markup;
using MarkupString.Layout;
using MarkupString;
using System.Globalization;
using SharpMUSH.Library.Authorization;
using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class StatsMail
{
	public static async ValueTask<MString> Handle(IMUSHCodeParser parser,
		IExpandedObjectDataService objectDataService,
		ILocateService locateService,
		IMediator mediator,
		INotifyService notifyService,
		MString? arg0, string[] switches)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		if (executor is not SharpPlayer target)
		{
			throw new InvalidOperationException("@mail reads a player's own mail, and its dispatcher routes only players here.");
		}

		if (!string.IsNullOrEmpty(arg0?.ToPlainText()))
		{
			if (!await executor.Can(PortalPermission.ChatAdmin))
			{
				var errorResult = await notifyService.NotifyAndReturn(
					executor.Object().DBRef,
					errorReturn: ErrorMessages.Returns.PermissionDenied,
					notifyMessage: ErrorMessages.Notifications.PermissionDenied,
					shouldNotify: true);
				return errorResult.Message!;
			}

			switch (await locateService.LocateAndNotifyIfInvalid(
				parser, executor, executor, arg0.ToPlainText(),
				LocateFlags.PlayersPreference | LocateFlags.OnlyMatchTypePreference))
			{
				case AnySharpObject and SharpPlayer located:
					target = located;
					break;
				case AnySharpObject:
					throw new InvalidOperationException("A lookup restricted to players found something that is not a player.");
				case None:
					var noTargetError = await notifyService.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.NoSuchObject,
						notifyMessage: ErrorMessages.Notifications.CantSeeThat,
						shouldNotify: true);
					return noTargetError.Message!;
				case Error<string> error:
					return MarkupText.Plain(error.Value);
			}
		}

		switch (switches)
		{
			case ["CSTATS"]:
				return await CStats(parser, objectDataService, mediator, notifyService, executor, target);
		}

		var allSentMail = mediator.CreateStream(new GetAllSentMailListQuery(target.Object));
		var allReceivedMail = mediator.CreateStream(new GetAllMailListQuery(target));
		var targetName = target.Object.Name;

		return switches switch
		{
			["STATS"] => await Stats(notifyService, executor, targetName, allSentMail, allReceivedMail),
			["DSTATS"] => await DStats(notifyService, executor, targetName, allSentMail, allReceivedMail),
			["FSTATS"] => await FStats(notifyService, executor, targetName, allSentMail, allReceivedMail),
			_ => MarkupText.Empty
		};
	}

	private static async Task<MString> CStats(IMUSHCodeParser parser,
		IExpandedObjectDataService objectDataService,
		IMediator mediator,
		INotifyService notifyService, AnySharpObject executor, SharpPlayer target)
	{
		var currentFolder = await MessageListHelper.CurrentMailFolder(parser, objectDataService, executor);
		var stats = await mediator.CreateStream(new GetMailListQuery(target, currentFolder)).ToArrayAsync();
		var unread = stats.Count(x => !x.Read);
		var cleared = stats.Count(x => x.Cleared);

		await notifyService.Notify(executor,
			$"MAIL: {stats.Length} messages in folder [{currentFolder}] ({unread} unread, {cleared} cleared).", executor);

		return MarkupText.Empty;
	}

	private static async Task<MString> FStats(
		INotifyService notifyService, AnySharpObject executor, string targetName,
		IAsyncEnumerable<SharpMail> allSentMailIe, IAsyncEnumerable<SharpMail> allReceivedMailIe)
		=> await StatsPanel(notifyService, executor, targetName, await allSentMailIe.ToArrayAsync(), await allReceivedMailIe.ToArrayAsync(), withSize: true);

	private static async Task<MString> DStats(
		INotifyService notifyService, AnySharpObject executor, string targetName,
		IAsyncEnumerable<SharpMail> allSentMailIe, IAsyncEnumerable<SharpMail> allReceivedMailIe)
		=> await StatsPanel(notifyService, executor, targetName, await allSentMailIe.ToArrayAsync(), await allReceivedMailIe.ToArrayAsync(), withSize: false);

	/// <summary><c>@mail/dstats</c> and <c>/fstats</c>: what was sent and received, and with <paramref name="withSize"/> how much text it holds.</summary>
	private static async Task<MString> StatsPanel(INotifyService notifyService, AnySharpObject executor, string targetName,
		SharpMail[] sent, SharpMail[] received, bool withSize)
	{
		string Summary(SharpMail[] mail) =>
			$"{mail.Length}, {mail.Count(x => !x.Read)} unread, {mail.Count(x => x.Cleared)} cleared"
			+ (withSize ? $", {mail.Sum(x => x.Content.Length)} characters" : "");

		(string, MString)[] fields =
		[
			("Sent", MarkupText.Plain(Summary(sent))),
			("Received", MarkupText.Plain(Summary(received))),
			.. received.Length == 0 ? [] : new[] { ("Last received", MarkupText.Plain(received.Max(x => x.DateSent).ToString("ddd MMM dd HH:mm yyyy", CultureInfo.InvariantCulture))) },
		];
		await notifyService.Notify(executor,
			ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain($"Mail statistics for {targetName}"), ServerLayout.KeyValues(fields)), 78), executor);
		return MarkupText.Empty;
	}

	private static async Task<MString> Stats(
		INotifyService notifyService, AnySharpObject executor, string targetName,
		IAsyncEnumerable<SharpMail> allSentMailIe, IAsyncEnumerable<SharpMail> allReceivedMailIe)
	{
		await notifyService.Notify(executor, $"{targetName} sent {await allSentMailIe.CountAsync()} messages.", executor);
		await notifyService.Notify(executor, $"{targetName} received {await allReceivedMailIe.CountAsync()} messages.", executor);

		return MarkupText.Empty;
	}
}