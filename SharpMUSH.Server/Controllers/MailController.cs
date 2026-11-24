using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Services;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Time;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Character mailbox API, backed by the in-game <c>@mail</c> system (the same data the MUSH reads
/// and writes). All operations act on the authenticated character's mail; mail is per-character, so
/// these endpoints have no meaning at the account level.
///
/// Routes:
///   GET    /api/mail?folder=INBOX        — list messages in a folder (numbered as in @mail)
///   GET    /api/mail/folders             — the character's folder names
///   GET    /api/mail/{folder}/{number}   — read one message (marks it read)
///   GET    /api/mail/sent                — the mail the character sent that is still in a mailbox
///   GET    /api/mail/sent/{id}           — read one sent message (leaves it as the recipient has it)
///   POST   /api/mail                     — send mail { to, subject, body, urgent }
///   DELETE /api/mail/{folder}/{number}   — delete a message
///
/// <c>{number}</c> is the number the list reports, as <c>@mail</c> prints it. A folder is numbered
/// from 1 for the reader and indexed from 0 in the database, so every route taking one converts it
/// with <see cref="FolderIndex"/>.
/// <para>
/// Every time is in the reader's zone when their <c>TZ</c> attribute names one (<c>Zoned</c>), as boards and jobs
/// show it; otherwise it is UTC, for the portal to show in the browser's zone.
/// </para>
/// </summary>
[ApiController]
[Route("api/mail")]
[Authorize]
public class MailController(
	IMediator mediator,
	IEngineCommandInvoker commandInvoker,
	IVisibleWorldProjection projection,
	IAttributeService attributes,
	ILogger<MailController> logger) : ControllerBase
{
	private const string DefaultFolder = "INBOX";

	/// <summary>extmail.h:71 — <c>SUBJECT_LEN</c>. Past it the command reads the whole argument as
	/// the body, so refusing beats silently losing the subject.</summary>
	private const int SubjectLength = 60;

	public record MailSummaryDto(int Number, string From, string Subject, DateTimeOffset DateSent, bool Read, bool Urgent, string Folder, bool Zoned);
	public record MailMessageDto(int Number, string From, string Subject, string Body, DateTimeOffset DateSent, bool Urgent, bool Read, string Folder, bool Zoned);

	/// <summary>
	/// One message as its sender sent it. A message sent to several players is stored once for each of them; the
	/// copies are one row here, naming every recipient whose copy is still in their mailbox.
	/// </summary>
	/// <param name="Id">The id of the first copy, which <c>GET api/mail/sent/{id}</c> reads.</param>
	/// <param name="ReadBy">How many of the recipients have read their copy.</param>
	public record SentMailDto(long Id, IReadOnlyList<string> To, string Subject, DateTimeOffset DateSent, bool Urgent, int ReadBy, bool Zoned);
	public record SentMailMessageDto(long Id, IReadOnlyList<string> To, string Subject, string Body, DateTimeOffset DateSent, bool Urgent, int ReadBy, bool Zoned);
	public record SendMailRequest(string To, string Subject, string Body, bool Urgent);

	[HttpGet]
	public async Task<ActionResult<IReadOnlyList<MailSummaryDto>>> List([FromQuery] string folder, CancellationToken ct)
	{
		var player = await ResolvePlayerAsync(ct);
		if (player is null) return Unauthorized();

		folder = string.IsNullOrWhiteSpace(folder) ? DefaultFolder : folder;
		var zone = await ReaderZoneAsync(player);
		var list = new List<MailSummaryDto>();
		var number = 1;
		// The summaries leave the bodies undecoded and name the sender without hydrating it.
		await foreach (var mail in mediator.CreateStream(new GetMailSummaryListQuery(player, folder)).WithCancellation(ct))
		{
			list.Add(new MailSummaryDto(
				number++,
				mail.Anonymous ? AnonymousSender : mail.SenderName ?? UnknownSender,
				mail.Subject.ToPlainText(),
				InZone(mail.DateSent, zone),
				mail.Read,
				mail.Urgent,
				mail.Folder,
				zone is not null));
		}
		return list;
	}

	[HttpGet("folders")]
	public async Task<ActionResult<IReadOnlyList<string>>> Folders(CancellationToken ct)
	{
		var player = await ResolvePlayerAsync(ct);
		if (player is null) return Unauthorized();

		// Read from the folder index (one seek per folder) instead of decoding the whole mailbox. The index
		// lists only non-empty names, so a message filed under "" no longer adds an empty entry here.
		var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DefaultFolder };
		folders.UnionWith(await mediator.Send(new GetMailFoldersQuery(player), ct));
		return folders.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
	}

	[HttpGet("{folder}/{number:int}")]
	public async Task<ActionResult<MailMessageDto>> Read(string folder, int number, CancellationToken ct)
	{
		var player = await ResolvePlayerAsync(ct);
		if (player is null) return Unauthorized();

		if (FolderIndex(number) is not { } index) return NotFound();

		var mail = await mediator.Send(new GetMailQuery(player, index, folder), ct);
		if (mail is null) return NotFound();

		// Reading marks it read, mirroring @mail.
		if (!mail.Read)
		{
			await mediator.Send(new UpdateMailCommand(mail, MailUpdate.ReadEdit(true)), ct);
		}

		var zone = await ReaderZoneAsync(player);
		return new MailMessageDto(
			number,
			await FromNameAsync(mail),
			mail.Subject.ToPlainText(),
			mail.Content.ToPlainText(),
			InZone(mail.DateSent, zone),
			mail.Urgent,
			true,
			mail.Folder,
			zone is not null);
	}

	[HttpGet("sent")]
	public async Task<ActionResult<IReadOnlyList<SentMailDto>>> Sent(CancellationToken ct)
	{
		var player = await ResolvePlayerAsync(ct);
		if (player is null) return Unauthorized();

		var zone = await ReaderZoneAsync(player);
		var sent = await SentMessagesAsync(player, ct);
		// Newest first, as a sent list is read.
		return sent.AsEnumerable().Reverse()
			.Select(message => new SentMailDto(message.Id, message.To, message.First.Subject.ToPlainText(),
				InZone(message.First.DateSent, zone), message.First.Urgent, message.ReadBy, zone is not null))
			.ToList();
	}

	[HttpGet("sent/{id:long}")]
	public async Task<ActionResult<SentMailMessageDto>> ReadSent(long id, CancellationToken ct)
	{
		var player = await ResolvePlayerAsync(ct);
		if (player is null) return Unauthorized();

		// Only the sender's own list is searched, so an id names nothing someone else sent.
		var sent = await SentMessagesAsync(player, ct);
		if (sent.FirstOrDefault(message => message.Id == id) is not { } found) return NotFound();

		var zone = await ReaderZoneAsync(player);
		return new SentMailMessageDto(found.Id, found.To, found.First.Subject.ToPlainText(), found.First.Content.ToPlainText(),
			InZone(found.First.DateSent, zone), found.First.Urgent, found.ReadBy, zone is not null);
	}

	/// <summary>One message as sent: its first copy, every recipient's name, and how many have read theirs.</summary>
	private sealed class SentMessage(long id, SharpMail first)
	{
		public long Id { get; } = id;
		public SharpMail First { get; } = first;
		public List<string> To { get; } = [];
		public int ReadBy { get; private set; }
		private readonly HashSet<string> _recipients = [];

		/// <summary>Whether this send already has a copy for that recipient: a second one is another send.</summary>
		public bool Reached(string? recipient) => recipient is not null && _recipients.Contains(recipient);

		public void Add(string? recipient, string to, bool read)
		{
			if (recipient is not null) _recipients.Add(recipient);
			To.Add(to);
			if (read) ReadBy++;
		}
	}

	/// <summary>
	/// How far apart two copies of one message may be stamped. <c>@mail</c> delivers to each recipient in turn and
	/// stamps each copy as it is stored, so the copies of one send differ by the time the deliveries took.
	/// </summary>
	private static readonly TimeSpan SameSend = TimeSpan.FromSeconds(5);

	/// <summary>
	/// The character's sent mail in the order it was sent, the copies of one send folded together: consecutive
	/// copies (the store keeps them in id order) with the same subject, text and urgency, stamped within
	/// <see cref="SameSend"/> of the first, each to a recipient the send has not reached yet.
	/// </summary>
	private async Task<List<SentMessage>> SentMessagesAsync(SharpPlayer player, CancellationToken ct)
	{
		var names = new Dictionary<string, string>(StringComparer.Ordinal);
		var messages = new List<SentMessage>();
		await foreach (var mail in mediator.CreateStream(new GetAllSentMailListQuery(player.Object)).WithCancellation(ct))
		{
			if (mail.Id is null || MailNumber(mail.Id) is not { } id) continue;
			var (recipient, to) = await RecipientAsync(mail, names);
			if (messages.Count == 0 || !SameMessage(messages[^1].First, mail) || messages[^1].Reached(recipient))
			{
				messages.Add(new SentMessage(id, mail));
			}

			messages[^1].Add(recipient, to, mail.Read);
		}

		return messages;
	}

	private static bool SameMessage(SharpMail first, SharpMail next)
		=> next.DateSent - first.DateSent <= SameSend
			&& next.DateSent >= first.DateSent
			&& next.Urgent == first.Urgent
			&& next.Subject.ToPlainText() == first.Subject.ToPlainText()
			&& next.Content.ToPlainText() == first.Content.ToPlainText();

	/// <summary>The number of a <see cref="SharpMail.Id"/> (<c>Mail/12</c>).</summary>
	private static long? MailNumber(string id)
		=> long.TryParse(id.AsSpan(id.IndexOf('/') + 1), System.Globalization.NumberStyles.None,
			System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : null;

	/// <summary>The mailbox a copy is in, as its dbref and its name; no dbref once that mailbox is gone.</summary>
	private static async ValueTask<(string? Key, string Name)> RecipientAsync(SharpMail mail, Dictionary<string, string> names)
	{
		if (mail.To is null) return (null, UnknownSender);
		var recipient = await mail.To.WithCancellation(CancellationToken.None);
		if (recipient is not AnySharpObject found) return (null, UnknownSender);
		var key = found.Object().DBRef.ToString();
		if (!names.TryGetValue(key, out var name))
		{
			name = found.Object().Name;
			names[key] = name;
		}

		return (key, name);
	}

	/// <summary>
	/// The reader's zone: their <c>TZ</c> attribute when it names one (as <c>+timezone</c> sets it and boards, jobs
	/// and <c>time()</c> read it), else <see langword="null"/>.
	/// </summary>
	private async ValueTask<TimeZoneInfo?> ReaderZoneAsync(SharpPlayer player)
	{
		var reader = new AnySharpObject(player);
		return await attributes.GetAttributeAsync(reader, reader, "TZ", IAttributeService.AttributeMode.Read, parent: false)
				is SharpAttribute[] { Length: > 0 } tz
			&& tz.Last().Value.ToPlainText().Trim() is { Length: > 0 } name
			&& TimeZones.TryFind(name, out var zone)
				? zone
				: null;
	}

	private static DateTimeOffset InZone(DateTimeOffset when, TimeZoneInfo? zone)
		=> zone is null ? when.ToUniversalTime() : TimeZoneInfo.ConvertTime(when, zone);

	/// <summary>
	/// Sends by running the engine's own <c>@MAIL</c>, so recipient resolution, the mail lock,
	/// <c>MAILSIGNATURE</c>, the <c>AMAIL</c> trigger and the delivery notice happen as they do
	/// in-game. Arguments go over pre-split, never spliced into a command line, so a <c>;</c> in a
	/// body cannot start a second command; <c>NOEVAL</c> keeps the caller's text as text.
	/// </summary>
	[HttpPost]
	public async Task<IActionResult> Send([FromBody] SendMailRequest request, CancellationToken ct)
	{
		// The same resolution the read paths use. This one runs an engine command as the resolved
		// character, so it is the last place that should have been happy with a bare claim.
		if (await ResolvePlayerAsync(ct) is not { } sender) return Unauthorized();
		var character = sender.Object.DBRef;

		if (string.IsNullOrWhiteSpace(request.To))
		{
			return BadRequest(new { error = "Recipient is required." });
		}

		var subject = request.Subject ?? string.Empty;
		if (subject.Length > SubjectLength)
		{
			return BadRequest(new { error = $"Subject may be at most {SubjectLength} characters." });
		}

		// The subject ends at the first single '/', a doubled one being a literal (extmail.c:1337).
		var subjectAndBody = $"{subject.Replace("/", "//")}/{request.Body ?? string.Empty}";

		var arguments = new Dictionary<string, CallState>
		{
			["0"] = new CallState(request.To),
			["1"] = new CallState(subjectAndBody)
		};

		// The send arm is selected by the *last* switch, so NOEVAL must not end the list.
		string[] switches = request.Urgent ? ["NOEVAL", "URGENT", "SEND"] : ["NOEVAL", "SEND"];

		var result = await commandInvoker.InvokeAsync("@MAIL", character, arguments, switches);
		var message = result?.Message.ToPlainText() ?? string.Empty;

		// A recipient who refuses your mail exists, so that is a 403, not the 404 a bad name gets.
		if (message == ErrorMessages.Returns.NoSuchPlayer)
		{
			return NotFound(new { error = $"No such character: {request.To}" });
		}

		if (message == ErrorMessages.Returns.RecipientDoesNotAcceptMail)
		{
			return StatusCode(StatusCodes.Status403Forbidden, new { error = message });
		}

		// Only those two are the caller's doing. @mail can also return TooManySwitches or
		// BadArgumentsToMailCommand, which this endpoint builds and so can only get wrong itself —
		// blaming the caller with a 403 would send them looking in the wrong place.
		if (string.IsNullOrWhiteSpace(message) || message.StartsWith("#-1", StringComparison.Ordinal))
		{
			logger.LogError("@MAIL refused a web send with {Result}.", message);
			return StatusCode(StatusCodes.Status500InternalServerError,
				new { error = "@MAIL could not send the message." });
		}

		// The engine's list, not request.To: caller text with a newline in it would forge log lines.
		logger.LogInformation("Web mail sent from {From} to {Delivered}.", character, message);
		return Ok(new { sent = true });
	}

	[HttpDelete("{folder}/{number:int}")]
	public async Task<IActionResult> Delete(string folder, int number, CancellationToken ct)
	{
		var player = await ResolvePlayerAsync(ct);
		if (player is null) return Unauthorized();

		if (FolderIndex(number) is not { } index) return NotFound();

		var mail = await mediator.Send(new GetMailQuery(player, index, folder), ct);
		if (mail is null) return NotFound();

		await mediator.Send(new DeleteMailCommand(mail), ct);
		return Ok(new { deleted = true });
	}

	private static int? FolderIndex(int number) => number >= 1 ? number - 1 : null;

	private const string UnknownSender = "(unknown)";

	private const string AnonymousSender = "(anonymous)";

	private static async Task<string> FromNameAsync(SharpMail mail)
		=> mail.Anonymous
			? AnonymousSender
			: (await mail.From.WithCancellation(CancellationToken.None)).Object()?.Name ?? UnknownSender;

	/// <summary>
	/// Resolves the character this request acts as, re-checked against its current account link.
	/// Mail is per-character and private, so this must never be looser than the object API's rule.
	/// </summary>
	private ValueTask<SharpPlayer?> ResolvePlayerAsync(CancellationToken ct) =>
		User.ResolvePlayerAsync(projection, ct);
}
