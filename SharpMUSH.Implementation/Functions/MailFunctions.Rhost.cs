using System.Globalization;
using SharpMUSH.Implementation.Commands.MailCommand;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// RhostMUSH's mail functions that PennMUSH lacks: <c>mailquick()</c>, <c>mailread()</c>, <c>mailquota()</c>,
/// <c>foldercurrent()</c>, <c>folderlist()</c> and <c>msizetot()</c>. They are wizard-only, as on RhostMUSH.
/// </summary>
/// <remarks>
/// RhostMUSH calls the inbox <c>Incoming</c>, and these functions do too. Its message states map onto SharpMUSH's
/// as the import maps them: an unread message is new, a read one old, a cleared one marked. SharpMUSH has no saved
/// state (the import reads saved messages as read), so no message counts as saved.
/// </remarks>
public partial class Functions
{
	/// <summary>RhostMUSH's name for folder 0.</summary>
	private const string RhostInbox = "Incoming";

	/// <summary>A folder as RhostMUSH names it: <c>Incoming</c> for the inbox, else the name it is stored under.</summary>
	private static string RhostFolderName(string folder)
		=> folder.Equals(ExpandedMailData.Inbox, StringComparison.OrdinalIgnoreCase) ? RhostInbox : folder;

	/// <summary>
	/// The player <paramref name="name"/> names, matched quietly, handed to <paramref name="answer"/> when the caller
	/// controls them; <paramref name="notFound"/> or <paramref name="refused"/> otherwise (the refusal with its reason
	/// when <paramref name="refused"/> is null).
	/// </summary>
	private async ValueTask<CallState> WithRhostMailbox(IMUSHCodeParser parser, string name, string notFound,
		string? refused, Func<SharpPlayer, ValueTask<CallState>> answer)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (string.IsNullOrWhiteSpace(name)
				|| await LocateService.Locate(parser, executor, executor, name.Trim(), LocateFlags.PlayersPreference)
				is not (AnySharpObject and SharpPlayer mailbox))
		{
			return new CallState(notFound);
		}

		if (!await CanReadMailOf(executor, mailbox))
		{
			return refused is null ? PermissionRefusal.NotControlled(mailbox).Return : new CallState(refused);
		}

		return await answer(mailbox);
	}

	/// <summary>The stored folder <paramref name="spec"/> names: the current one when empty, <c>Incoming</c> the inbox.</summary>
	private async ValueTask<string?> RhostFolder(IMUSHCodeParser parser, SharpPlayer mailbox, string spec)
	{
		spec = spec.Trim();
		if (spec.Length == 0)
		{
			return await MessageListHelper.CurrentMailFolder(parser, ObjectDataService, mailbox);
		}

		if (spec.Equals(RhostInbox, StringComparison.OrdinalIgnoreCase))
		{
			return ExpandedMailData.Inbox;
		}

		return MailFolders.Resolve(await MailFolders.LoadAsync(ObjectDataService, mailbox), spec) is MailFolder folder
			? folder.Name
			: null;
	}

	private const string NoQuota = "-1 -1 -1 -1 -1 -1";

	/// <summary>
	/// <c>mailquick(&lt;player&gt;[, &lt;folder&gt;[, &lt;type&gt;]])</c>: counts of the messages in a folder, the current one
	/// by default. Type 0 gives total, new, unread, old, marked and saved; 1 old, new and marked, the old count
	/// including the marked; 2 the total alone; 3 old, new and marked, the old count without the marked.
	/// </summary>
	[SharpFunction(Name = "mailquick", MinArgs = 1, MaxArgs = 3,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.WizardOnly,
		ParameterNames = ["player", "folder", "type"])]
	public ValueTask<CallState> MailQuick(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> WithRhostMailbox(parser, OptionalArgument(parser, "0"), ErrorMessages.Returns.NotFound, NoQuota, async mailbox =>
		{
			var type = int.TryParse(OptionalArgument(parser, "2").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
				out var parsed) && parsed is >= 0 and <= 3
				? parsed
				: 0;

			var folder = await RhostFolder(parser, mailbox, OptionalArgument(parser, "1"));
			var tally = folder is null
				? new MailTally()
				: await TallyMail(Mediator.CreateStream(new GetMailListQuery(mailbox, folder)));
			var (fresh, old, marked) = (tally.Unread, tally.Read, tally.Cleared);

			return new CallState(type switch
			{
				1 => $"{old + marked} {fresh} {marked}",
				2 => $"{tally.Total}",
				3 => $"{old} {fresh} {marked}",
				_ => $"{tally.Total} {fresh} 0 {old} {marked} 0"
			});
		});

	/// <summary>
	/// <c>mailquota(&lt;player&gt;[, &lt;type&gt;])</c>: messages held and the most the player may hold, messages saved
	/// and the most they may save, messages sent and the most they may send. Type 1, 2 and 3 give one pair each.
	/// A player who cannot be read gives <c>-1</c> throughout.
	/// </summary>
	[SharpFunction(Name = "mailquota", MinArgs = 1, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.WizardOnly,
		ParameterNames = ["player", "type"])]
	public ValueTask<CallState> MailQuota(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> WithRhostMailbox(parser, OptionalArgument(parser, "0"), NoQuota, NoQuota, async mailbox =>
		{
			var typeArg = OptionalArgument(parser, "1").Trim();
			var type = typeArg.Length == 0
				? 0
				: int.TryParse(typeArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : -1;

			var held = await Mediator.CreateStream(new GetAllMailListQuery(mailbox)).CountAsync();
			var limit = await MailDelivery.MailLimitAsync(AttributeService, Configuration, mailbox);
			var sent = await Mediator.CreateStream(new GetAllSentMailListQuery(mailbox.Object)).CountAsync();

			// No message is saved, nor can be; nothing limits how many a player sends.
			return new CallState(type switch
			{
				0 => $"{held} {limit} 0 0 {sent} -1",
				1 => $"{held} {limit}",
				2 => "0 0",
				3 => $"{sent} -1",
				_ => NoQuota
			});
		});

	/// <summary>
	/// <c>foldercurrent(&lt;player&gt;[, &lt;shared&gt;])</c>: the player's current mail folder. RhostMUSH's shared
	/// folders have no counterpart, so asking for the current shared folder gives nothing.
	/// </summary>
	[SharpFunction(Name = "foldercurrent", MinArgs = 1, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.WizardOnly,
		ParameterNames = ["player", "shared"])]
	public ValueTask<CallState> FolderCurrent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> WithRhostMailbox(parser, OptionalArgument(parser, "0"), ErrorMessages.Returns.NotFound, null, async mailbox =>
			int.TryParse(OptionalArgument(parser, "1").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
				out var shared) && shared > 0
				? CallState.Empty
				: new CallState(RhostFolderName(await MessageListHelper.CurrentMailFolder(parser, ObjectDataService, mailbox))));

	/// <summary><c>folderlist(&lt;player&gt;)</c>: the player's mail folders, <c>Incoming</c> first, then by number.</summary>
	[SharpFunction(Name = "folderlist", MinArgs = 1, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.WizardOnly,
		ParameterNames = ["player"])]
	public ValueTask<CallState> FolderList(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> WithRhostMailbox(parser, OptionalArgument(parser, "0"), ErrorMessages.Returns.NotFound, null, async mailbox =>
		{
			var data = await MailFolders.LoadAsync(ObjectDataService, mailbox);
			var holding = await Mediator.Send(new GetMailFoldersQuery(mailbox));
			var names = (data.FolderNumbers ?? []).Keys
				.Concat(holding)
				.Where(name => !name.Equals(ExpandedMailData.Inbox, StringComparison.OrdinalIgnoreCase))
				.DistinctBy(name => name.ToUpperInvariant())
				.OrderBy(name => data.NumberOf(name) is int number ? number : int.MaxValue)
				.ThenBy(name => name, StringComparer.OrdinalIgnoreCase);

			return new CallState(string.Join(" ", names.Prepend(RhostInbox)));
		});

	/// <summary><c>msizetot()</c>: how much every message on the game takes, in characters of subject and text.</summary>
	[SharpFunction(Name = "msizetot", MinArgs = 0, MaxArgs = 0,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.WizardOnly)]
	public async ValueTask<CallState> MailSizeTotal(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> new((await Mediator.CreateStream(new GetAllSystemMailQuery())
			.AggregateAsync(0L, (size, mail) => size + mail.Subject.Length + mail.Content.Length))
			.ToString(CultureInfo.InvariantCulture));

	/// <summary>
	/// <c>mailread(&lt;player&gt;, &lt;message&gt;, &lt;type&gt;[, &lt;mark&gt;])</c>: one part of a message in the player's
	/// current folder. <c>&lt;message&gt;</c> is its number, or <c>new</c>, <c>unread</c> or <c>both</c> for the first
	/// unread one. With <c>&lt;mark&gt;</c> 1 the message is marked read and nothing is returned.
	/// </summary>
	[SharpFunction(Name = "mailread", MinArgs = 3, MaxArgs = 4,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.WizardOnly,
		ParameterNames = ["player", "message", "type", "mark"])]
	public ValueTask<CallState> MailRead(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var spec = OptionalArgument(parser, "1").Trim();
		var type = OptionalArgument(parser, "2").Trim();
		if (spec.Length == 0 || type.Length == 0)
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.InvalidArguments));
		}

		return WithRhostMailbox(parser, OptionalArgument(parser, "0"), ErrorMessages.Returns.NoSuchPlayer, null, async mailbox =>
		{
			var folder = await MessageListHelper.CurrentMailFolder(parser, ObjectDataService, mailbox);
			if (await RhostMessage(mailbox, folder, spec) is not var (number, mail))
			{
				return new CallState(ErrorMessages.Returns.NoSuchMessage);
			}

			if (int.TryParse(OptionalArgument(parser, "3").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
					out var mark) && mark != 0)
			{
				await Mediator.Send(new UpdateMailCommand(mail, MailUpdate.ReadEdit(true)));
				return CallState.Empty;
			}

			return await MailPart(mailbox, number, mail, char.ToUpperInvariant(type[0]));
		});
	}

	/// <summary>The message <paramref name="spec"/> names in a folder, with its number there.</summary>
	private async ValueTask<(int Number, SharpMail Mail)?> RhostMessage(SharpPlayer mailbox, string folder, string spec)
	{
		if (spec.ToLowerInvariant() is "new" or "unread" or "both")
		{
			var number = 0;
			await foreach (var mail in Mediator.CreateStream(new GetMailListQuery(mailbox, folder)))
			{
				number++;
				if (!mail.Read && !mail.Cleared)
				{
					return (number, mail);
				}
			}

			return null;
		}

		return int.TryParse(spec, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index > 0
			&& await GetMailMessage(mailbox, folder, index - 1) is { } found
				? (index, found)
				: null;
	}

	/// <summary>The part of a message RhostMUSH's <c>mailread()</c> type letter asks for.</summary>
	private async ValueTask<CallState> MailPart(SharpPlayer mailbox, int number, SharpMail mail, char type)
		=> type switch
		{
			'B' => new CallState(mail.Content),
			'I' => new CallState(number.ToString(CultureInfo.InvariantCulture)),
			'S' => new CallState(mail.Subject),
			'F' => new CallState(await MailLayout.SenderName(mail)),
			'T' => new CallState(mailbox.Object.Name),
			'D' => new CallState(mail.DateSent.ToLocalTime().ToString(PennTimeFormat, CultureInfo.InvariantCulture)),
			'C' => new CallState(await SenderOnline(mail) ? "1" : "0"),
			'G' => new CallState(mail.Cleared ? "MARKED" : mail.Read ? "OLD" : "NEW"),
			'?' => new CallState(mail.Forwarded ? "FORWARD" : string.Empty),
			'K' => new CallState(mail.Content.Length.ToString(CultureInfo.InvariantCulture)),
			'U' => CallState.Empty,
			_ => new CallState("B C D F G I K S T U ?")
		};

	/// <summary>Whether the sender is connected and not hidden; an anonymous message's never is.</summary>
	private async ValueTask<bool> SenderOnline(SharpMail mail)
		=> !mail.Anonymous
			&& await mail.From.WithCancellation(CancellationToken.None) is AnySharpObject sender
			&& await ConnectionService.IsConnected(sender)
			&& !await ConnectionService.IsPlayerHiddenAsync(sender.Object().DBRef);
}
