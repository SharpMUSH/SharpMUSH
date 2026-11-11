using System.Globalization;
using SharpMUSH.Implementation.Commands.MailCommand;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// TinyMUX's mail functions that PennMUSH lacks: <c>mailcount()</c>, <c>mailflags()</c>, <c>mailinfo()</c>,
/// <c>mailreview()</c> and <c>mailsize()</c>. Each reads the caller's own mailbox, or another player's that the
/// caller controls.
/// </summary>
public partial class Functions
{
	/// <summary>
	/// The mailbox <paramref name="name"/> names, matched quietly: the caller's own when <paramref name="name"/> is
	/// empty, <c>#-1 NO SUCH PLAYER</c> when it names no player, and the refusal when the caller does not control it.
	/// </summary>
	private async ValueTask<CallState> WithMailbox(IMUSHCodeParser parser, string name,
		Func<SharpPlayer, ValueTask<CallState>> answer)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (string.IsNullOrWhiteSpace(name))
		{
			return executor is SharpPlayer own ? await answer(own) : new CallState(ErrorMessages.Returns.NoSuchPlayer);
		}

		if (await LocateService.Locate(parser, executor, executor, name.Trim(), LocateFlags.PlayersPreference)
				is not (AnySharpObject and SharpPlayer mailbox))
		{
			return new CallState(ErrorMessages.Returns.NoSuchPlayer);
		}

		return await CanReadMailOf(executor, mailbox)
			? await answer(mailbox)
			: PermissionRefusal.NotControlled(mailbox).Return;
	}

	/// <summary>The argument at <paramref name="key"/>, as plain text, or nothing when it was not given.</summary>
	private static string OptionalArgument(IMUSHCodeParser parser, string key)
		=> parser.CurrentState.Arguments.TryGetValue(key, out var value) ? value.Message.ToPlainText() : string.Empty;

	/// <summary>Message <paramref name="number"/> (from 1) in <paramref name="mailbox"/>'s current folder, if there is one.</summary>
	private async ValueTask<SharpMail?> CurrentFolderMessage(IMUSHCodeParser parser, SharpPlayer mailbox, string number)
	{
		if (!int.TryParse(number.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) || index < 1)
		{
			return null;
		}

		var folder = await MessageListHelper.CurrentMailFolder(parser, ObjectDataService, mailbox);
		return await GetMailMessage(mailbox, folder, index - 1);
	}

	/// <summary>A message's flags as TinyMUX writes them: <c>R</c>ead, <c>U</c>rgent, <c>C</c>leared, <c>T</c>agged, <c>F</c>orwarded.</summary>
	private static string MuxMailFlags(SharpMail mail)
		=> string.Concat(
			mail.Read ? "R" : string.Empty,
			mail.Urgent ? "U" : string.Empty,
			mail.Cleared ? "C" : string.Empty,
			mail.Tagged ? "T" : string.Empty,
			mail.Forwarded ? "F" : string.Empty);

	/// <summary>
	/// <c>mailcount([&lt;player&gt;])</c>: how many messages are in the player's inbox, folder 0, cleared ones included.
	/// </summary>
	[SharpFunction(Name = "mailcount", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["player"])]
	public ValueTask<CallState> MailCount(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> WithMailbox(parser, OptionalArgument(parser, "0"), async mailbox => new CallState(
			(await Mediator.CreateStream(new GetMailListQuery(mailbox, ExpandedMailData.Inbox)).CountAsync())
			.ToString(CultureInfo.InvariantCulture)));

	/// <summary><c>mailflags(&lt;message&gt;[, &lt;player&gt;])</c>: the flags of a message in the current folder.</summary>
	[SharpFunction(Name = "mailflags", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["message", "player"])]
	public ValueTask<CallState> MailFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> WithMailbox(parser, OptionalArgument(parser, "1"), async mailbox =>
			await CurrentFolderMessage(parser, mailbox, OptionalArgument(parser, "0")) is { } mail
				? new CallState(MuxMailFlags(mail))
				: new CallState(ErrorMessages.Returns.NoSuchMessage));

	/// <summary>
	/// <c>mailinfo(&lt;message&gt;, &lt;field&gt;[, &lt;player&gt;])</c>: one field of a message in the current folder:
	/// <c>from</c>, <c>to</c>, <c>tolist</c>, <c>subject</c>, <c>time</c>, <c>body</c>, <c>size</c>, <c>flags</c> or
	/// <c>read</c>.
	/// </summary>
	[SharpFunction(Name = "mailinfo", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["message", "field", "player"])]
	public ValueTask<CallState> MailInfo(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> WithMailbox(parser, OptionalArgument(parser, "2"), async mailbox =>
			await CurrentFolderMessage(parser, mailbox, OptionalArgument(parser, "0")) is { } mail
				? await MailField(mailbox, mail, OptionalArgument(parser, "1").Trim().ToLowerInvariant())
				: new CallState(ErrorMessages.Returns.NoSuchMessage));

	private static async ValueTask<CallState> MailField(SharpPlayer mailbox, SharpMail mail, string field)
		=> field switch
		{
			"from" => new CallState(await MailSenderDbref(mail)),
			"to" => new CallState($"#{mailbox.Object.DBRef.Number}"),
			// TinyMUX keeps every recipient a message went to; a message here is one recipient's copy, so the list
			// is the one player whose mailbox holds it, written as a bare number as TinyMUX writes its list.
			"tolist" => new CallState(mailbox.Object.DBRef.Number.ToString(CultureInfo.InvariantCulture)),
			"subject" => new CallState(mail.Subject),
			"time" => new CallState(mail.DateSent.ToLocalTime().ToString(PennTimeFormat, CultureInfo.InvariantCulture)),
			"body" => new CallState(mail.Content),
			"size" => new CallState(mail.Content.Length.ToString(CultureInfo.InvariantCulture)),
			"flags" => new CallState(MuxMailFlags(mail)),
			"read" => new CallState(mail.Read ? "1" : "0"),
			_ => new CallState(ErrorMessages.Returns.InvalidField)
		};

	/// <summary>The sender's dbref, or <c>#-1</c> when the message is anonymous or its sender is gone.</summary>
	private static async ValueTask<string> MailSenderDbref(SharpMail mail)
		=> !mail.Anonymous && (await mail.From.WithCancellation(CancellationToken.None)).Object() is { } sender
			? $"#{sender.DBRef.Number}"
			: ErrorMessages.Returns.Nothing;

	/// <summary>
	/// <c>mailreview(&lt;player&gt;[, &lt;message&gt;])</c>: how many messages the caller has sent that are in
	/// <c>&lt;player&gt;</c>'s mailbox, or the text of the <c>&lt;message&gt;</c>th of them. Anyone may ask about
	/// their own mail to anyone.
	/// </summary>
	[SharpFunction(Name = "mailreview", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["player", "message"])]
	public async ValueTask<CallState> MailReview(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (await LocateService.Locate(parser, executor, executor, OptionalArgument(parser, "0").Trim(),
				LocateFlags.PlayersPreference) is not (AnySharpObject and SharpPlayer recipient))
		{
			return new CallState(ErrorMessages.Returns.NoSuchPlayer);
		}

		var sent = Mediator.CreateStream(new GetSentMailListQuery(executor.Object(), recipient));
		if (!parser.CurrentState.Arguments.ContainsKey("1"))
		{
			return new CallState((await sent.CountAsync()).ToString(CultureInfo.InvariantCulture));
		}

		if (!int.TryParse(OptionalArgument(parser, "1").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
				out var number) || number < 1)
		{
			return new CallState(ErrorMessages.Returns.NoSuchMessage);
		}

		return await sent.Skip(number - 1).FirstOrDefaultAsync() is { } mail
			? new CallState(mail.Content)
			: new CallState(ErrorMessages.Returns.NoSuchMessage);
	}

	/// <summary>
	/// <c>mailsize(&lt;player&gt;)</c>: the size of everything in the player's mailbox, in characters, counting one
	/// more for each message as TinyMUX does.
	/// </summary>
	[SharpFunction(Name = "mailsize", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["player"])]
	public async ValueTask<CallState> MailSize(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var name = OptionalArgument(parser, "0");
		if (string.IsNullOrWhiteSpace(name))
		{
			return new CallState(ErrorMessages.Returns.NoSuchPlayer);
		}

		return await WithMailbox(parser, name, async mailbox => new CallState(
			(await Mediator.CreateStream(new GetAllMailListQuery(mailbox)).AggregateAsync(0L, (size, mail) => size + mail.Content.Length + 1))
			.ToString(CultureInfo.InvariantCulture)));
	}
}
