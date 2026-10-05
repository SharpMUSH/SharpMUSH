using Mediator;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands.MailCommand;

[Union]
public sealed class ErrorOrMailList : IUnion
{
	public ErrorOrMailList(Error<string> value) => Value = value;
	public ErrorOrMailList(IAsyncEnumerable<SharpMail> value) => Value = value;

	public object? Value { get; }

	public override bool Equals(object? obj) => obj is ErrorOrMailList other && Equals(Value, other.Value);

	public override int GetHashCode() => Value?.GetHashCode() ?? 0;

	public bool IsError => Value is Error<string>;

	public static ErrorOrMailList FromAsyncEnumerable(IAsyncEnumerable<SharpMail> x) => new(x);
}

/// <summary>A message and where <c>@mail</c> finds it: its folder's number (or name) and its 1-based position there.</summary>
public readonly record struct MailPosition(SharpMail Mail, string Folder, int Index)
{
	public override string ToString() => $"{Folder}:{Index}";
}

public static class MessageListHelper
{
	public static async ValueTask<string> CurrentMailFolder(IMUSHCodeParser parser, IExpandedObjectDataService objectDataService, AnySharpObject executor)
	{
		var mailData = await objectDataService.GetExpandedDataAsync<ExpandedMailData>(executor.Object());

		if (mailData?.ActiveFolder != null)
		{
			return mailData.ActiveFolder!;
		}

		mailData = (mailData ?? new ExpandedMailData()) with
		{
			Folders = mailData?.Folders ?? [ExpandedMailData.Inbox],
			ActiveFolder = ExpandedMailData.Inbox
		};
		await objectDataService.SetExpandedDataAsync(mailData, executor.Object(), ignoreNull: true);

		return mailData.ActiveFolder!;
	}

	/// <summary>
	/// Each message with where <c>@mail</c> finds it: <c>&lt;folder number&gt;:&lt;position&gt;</c>, the position
	/// counted from 1 within its folder, as PennMUSH's <c>fun_maillist</c> and <c>do_mail_file</c> write it. A
	/// folder with no number is written by name.
	/// </summary>
	public static async ValueTask<List<MailPosition>> WithPositionsAsync(IMediator mediator,
		SharpPlayer mailbox, ExpandedMailData folders, IAsyncEnumerable<SharpMail> mailList)
	{
		var messages = await mailList.ToListAsync();
		var result = new List<MailPosition>(messages.Count);
		if (messages.Count == 0)
		{
			return result;
		}

		// Every folder's positions from one read of the whole mailbox, rather than one read per folder the
		// selection touches. The mailbox lists each folder's mail in the same (id) order its folder does.
		var positions = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
		await foreach (var held in mediator.CreateStream(new GetAllMailListQuery(mailbox)))
		{
			if (!positions.TryGetValue(held.Folder, out var folderPositions))
			{
				positions[held.Folder] = folderPositions = [];
			}

			folderPositions.TryAdd(held.Id ?? string.Empty, folderPositions.Count + 1);
		}

		foreach (var mail in messages)
		{
			if (mail.Id is { } id && positions.TryGetValue(mail.Folder, out var inFolder)
					&& inFolder.TryGetValue(id, out var position))
			{
				var folder = folders.NumberOf(mail.Folder) is int number ? number.ToString() : mail.Folder;
				result.Add(new MailPosition(mail, folder, position));
			}
		}

		return result;
	}

	/// <summary>Tells the executor why a message list could not be read, and answers with the same words.</summary>
	public static async ValueTask<MString> RefuseAsync(INotifyService notifyService, AnySharpObject executor,
		string error)
	{
		await notifyService.Notify(executor, error);
		return MarkupText.Plain(error);
	}

	public static async ValueTask<ErrorOrMailList> Handle(IMUSHCodeParser parser, IExpandedObjectDataService objectDataService, IMediator? mediator, INotifyService? notifyService, MString? arg0, AnySharpObject executor)
	{
		if (executor is not SharpPlayer player)
		{
			throw new InvalidOperationException("Only a player has a mail list.");
		}

		var msgList = arg0?.ToPlainText().Trim() ?? "folder";
		var folderSplit = msgList.Split(':');
		IAsyncEnumerable<SharpMail> mailList;

		if (folderSplit.Length == 2 && !string.IsNullOrWhiteSpace(folderSplit[0]))
		{
			// parse_message_spec (extmail.c:3153): <folder>:<messages>, the folder a number or, here, a name.
			// parse_msglist takes "all" with strcasecmp (extmail.c:3000), as it does every keyword below.
			if (MailFolders.Resolve(await MailFolders.LoadAsync(objectDataService, player), folderSplit[0]) is not MailFolder folder)
			{
				return new Error<string>("MAIL: Invalid message specification");
			}

			mailList = mediator!.CreateStream(new GetMailListQuery(player, folder.Name));
			msgList = folderSplit[1];
		}
		else if (msgList.Equals("all", StringComparison.OrdinalIgnoreCase))
		{
			mailList = mediator!.CreateStream(new GetAllMailListQuery(player));
		}
		else
		{
			var currentFolder = await CurrentMailFolder(parser, objectDataService, executor);
			mailList = mediator!.CreateStream(new GetMailListQuery(player, currentFolder));
		}

		msgList = msgList.ToLower();
		var rangeSplit = msgList.Split('-');
		ErrorOrMailList filteredList = msgList switch
		{
			_ when msgList.Contains(' ')
				=> new Error<string>("MAIL: Invalid message specification"),
			['*', .. var person] => await FilterMailByPerson(parser, executor, mailList, person),
			['~', .. var days] when int.TryParse(days, out var exactDay)
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => x.DateSent >= DateTimeOffset.UtcNow.AddDays(-exactDay - 1)
																																	 && x.DateSent <= DateTimeOffset.UtcNow.AddDays(-exactDay))),
			['>', .. var days] when int.TryParse(days, out var afterDay)
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => x.DateSent >= DateTimeOffset.UtcNow.AddDays(-afterDay))),
			['<', .. var days] when int.TryParse(days, out var beforeDay)
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => x.DateSent <= DateTimeOffset.UtcNow.AddDays(-beforeDay))),
			"read"
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => x.Read)),
			"unread"
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => !x.Read)),
			"cleared"
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => x.Cleared)),
			"tagged"
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => x.Tagged)),
			"urgent"
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => x.Urgent)),
			"folder" or "all"
				=> ErrorOrMailList.FromAsyncEnumerable(mailList),
			_ when rangeSplit.Length == 2
						 && int.TryParse(rangeSplit[0], out var left) && int.TryParse(rangeSplit[1], out var right)
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Skip(left - 1).Take(right - left + 1)),
			_ when rangeSplit.Length == 2
						 && int.TryParse(rangeSplit[0], out var left) && !int.TryParse(rangeSplit[1], out _)
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Skip(left - 1)),
			_ when rangeSplit.Length == 2
						 && !int.TryParse(rangeSplit[0], out _) && int.TryParse(rangeSplit[1], out var right)
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Take(right)),
			_ when int.TryParse(msgList, out var specificMessage)
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Skip(Math.Max(0, specificMessage - 1)).Take(1)),
			[] when msgList.Length == 0
				=> ErrorOrMailList.FromAsyncEnumerable(mailList),
			_ => new Error<string>("MAIL: Invalid message specification")
		};

		return filteredList;
	}

	/// <summary>
	/// The mail <paramref name="executor"/> has sent, filtered by <paramref name="arg0"/>: to
	/// <paramref name="recipient"/>, or to anyone when it is null. Sent mail has no folders, so a
	/// <c>folder:</c> prefix is ignored.
	/// </summary>
	public static ErrorOrMailList HandleSent(IMediator mediator, MString? arg0, AnySharpObject executor, SharpPlayer? recipient)
	{
		var msgList = arg0?.ToPlainText().Trim().ToLower() ?? "folder";
		var folderSplit = msgList.Split(':');
		if (folderSplit.Length == 2 && !string.IsNullOrWhiteSpace(folderSplit[0]))
		{
			msgList = folderSplit[1];
		}

		var rangeSplit = msgList.Split('-');
		var mailList = recipient is null
			? mediator.CreateStream(new GetAllSentMailListQuery(executor.Object()))
			: mediator.CreateStream(new GetSentMailListQuery(executor.Object(), recipient));

		ErrorOrMailList filteredList = msgList switch
		{
			_ when msgList.Contains(' ')
				=> new Error<string>("MAIL: Invalid message specification"),
			['~', .. var days] when int.TryParse(days, out var exactDay)
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => x.DateSent >= DateTimeOffset.UtcNow.AddDays(-exactDay - 1)
																																	 && x.DateSent <= DateTimeOffset.UtcNow.AddDays(-exactDay))),
			['>', .. var days] when int.TryParse(days, out var afterDay)
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => x.DateSent >= DateTimeOffset.UtcNow.AddDays(-afterDay))),
			['<', .. var days] when int.TryParse(days, out var beforeDay)
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => x.DateSent <= DateTimeOffset.UtcNow.AddDays(-beforeDay))),
			"read"
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => x.Read)),
			"unread"
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => !x.Read)),
			"cleared"
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => x.Cleared)),
			"tagged"
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => x.Tagged)),
			"urgent"
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Where(x => x.Urgent)),
			"folder" or "all"
				=> ErrorOrMailList.FromAsyncEnumerable(mailList),
			_ when rangeSplit.Length == 2
						 && int.TryParse(rangeSplit[0], out var left) && int.TryParse(rangeSplit[1], out var right)
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Skip(left - 1).Take(right - left + 1)),
			_ when rangeSplit.Length == 2
						 && int.TryParse(rangeSplit[0], out var left) && !int.TryParse(rangeSplit[1], out _)
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Skip(left - 1)),
			_ when rangeSplit.Length == 2
						 && !int.TryParse(rangeSplit[0], out _) && int.TryParse(rangeSplit[1], out var right)
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Take(right)),
			_ when int.TryParse(msgList, out var specificMessage)
				=> ErrorOrMailList.FromAsyncEnumerable(mailList.Skip(Math.Max(0, specificMessage - 1)).Take(1)),
			[] when msgList.Length == 0
				=> ErrorOrMailList.FromAsyncEnumerable(mailList),
			_ => new Error<string>("MAIL: Invalid message specification")
		};

		return filteredList;
	}

	private static async ValueTask<ErrorOrMailList> FilterMailByPerson(
		IMUSHCodeParser parser,
		AnySharpObject executor,
		IAsyncEnumerable<SharpMail> mailList,
		string personName)
	{
		var locateService = parser.ServiceProvider.GetRequiredService<ILocateService>();
		var locateResult = await locateService.Locate(parser, executor, executor, personName, LocateFlags.PlayersPreference);

		if (locateResult is not (AnySharpObject and SharpPlayer targetPlayer))
		{
			return ErrorOrMailList.FromAsyncEnumerable(mailList
				.Where(async (x, _) =>
				{
					var from = await x.From.WithCancellation(CancellationToken.None);
					return from.Object()?.Name.StartsWith(personName, StringComparison.OrdinalIgnoreCase) ?? false;
				}));
		}

		var targetPlayerDbref = targetPlayer.Object.DBRef;
		return ErrorOrMailList.FromAsyncEnumerable(mailList
			.Where(async (x, _) =>
			{
				var fromPlayer = await x.From.WithCancellation(CancellationToken.None);
				return fromPlayer.Object()?.DBRef == targetPlayerDbref;
			}));
	}
}