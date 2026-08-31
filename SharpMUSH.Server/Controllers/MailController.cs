using DotNext.Threading;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Services;
using SharpMUSH.Library.ParserInterfaces;

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
///   POST   /api/mail                     — send mail { to, subject, body, urgent }
///   DELETE /api/mail/{folder}/{number}   — delete a message
///
/// <c>{number}</c> is the number the list reports, which is the number <c>@mail</c> prints: folders
/// are numbered from 1 for the reader and indexed from 0 in the database, so every route that takes
/// one converts it with <see cref="FolderIndex"/> — the same <c>n - 1</c> the <c>@mail</c> command
/// applies before it queries.
/// </summary>
[ApiController]
[Route("api/mail")]
[Authorize]
public class MailController(IMediator mediator, IEngineCommandInvoker commandInvoker, ILogger<MailController> logger) : ControllerBase
{
	private const string DefaultFolder = "INBOX";

	/// <summary>extmail.h:71 — <c>SUBJECT_LEN</c>. Past this the command would treat the whole
	/// argument as the body, so the endpoint refuses rather than silently losing the subject.</summary>
	private const int SubjectLength = 60;

	public record MailSummaryDto(int Number, string From, string Subject, DateTimeOffset DateSent, bool Read, bool Urgent, string Folder);
	public record MailMessageDto(int Number, string From, string Subject, string Body, DateTimeOffset DateSent, bool Urgent, bool Read, string Folder);
	public record SendMailRequest(string To, string Subject, string Body, bool Urgent);

	[HttpGet]
	public async Task<ActionResult<IReadOnlyList<MailSummaryDto>>> List([FromQuery] string folder, CancellationToken ct)
	{
		var player = await ResolvePlayerAsync(ct);
		if (player is null) return Unauthorized();

		folder = string.IsNullOrWhiteSpace(folder) ? DefaultFolder : folder;
		var list = new List<MailSummaryDto>();
		var number = 1;
		await foreach (var mail in mediator.CreateStream(new GetMailListQuery(player, folder)).WithCancellation(ct))
		{
			list.Add(new MailSummaryDto(
				number++,
				await FromNameAsync(mail),
				mail.Subject.ToPlainText(),
				mail.DateSent,
				mail.Read,
				mail.Urgent,
				mail.Folder));
		}
		return list;
	}

	[HttpGet("folders")]
	public async Task<ActionResult<IReadOnlyList<string>>> Folders(CancellationToken ct)
	{
		var player = await ResolvePlayerAsync(ct);
		if (player is null) return Unauthorized();

		var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DefaultFolder };
		await foreach (var mail in mediator.CreateStream(new GetAllMailListQuery(player)).WithCancellation(ct))
		{
			folders.Add(mail.Folder);
		}
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

		return new MailMessageDto(
			number,
			await FromNameAsync(mail),
			mail.Subject.ToPlainText(),
			mail.Content.ToPlainText(),
			mail.DateSent,
			mail.Urgent,
			true,
			mail.Folder);
	}

	/// <summary>
	/// Sends by running the engine's own <c>@MAIL</c>, so recipient resolution, the mail lock,
	/// <c>MAILSIGNATURE</c>, the <c>AMAIL</c> trigger and the delivery notice all happen exactly as
	/// they do in-game. This endpoint used to resolve the recipient itself with a player-name lookup
	/// — a second mail implementation that drifted from the command it was meant to mirror, and that
	/// could not address <c>me</c>, a dbref or <c>*name</c>.
	///
	/// Arguments go over pre-split, never spliced into a command line, so a <c>;</c> in a body cannot
	/// start a second command; <c>NOEVAL</c> then keeps the caller's text as text rather than
	/// softcode.
	/// </summary>
	[HttpPost]
	public async Task<IActionResult> Send([FromBody] SendMailRequest request, CancellationToken ct)
	{
		if (User.GetActingCharacter() is not { } character) return Unauthorized();

		if (string.IsNullOrWhiteSpace(request.To))
		{
			return BadRequest(new { error = "Recipient is required." });
		}

		var subject = request.Subject ?? string.Empty;
		if (subject.Length > SubjectLength)
		{
			return BadRequest(new { error = $"Subject may be at most {SubjectLength} characters." });
		}

		// @mail takes "[subject/]message" as one argument and ends the subject at the first single
		// '/', a doubled one being a literal (extmail.c:1337) — so a slash the caller typed is doubled
		// on the way in and arrives as the character they meant.
		var subjectAndBody = $"{subject.Replace("/", "//")}/{request.Body ?? string.Empty}";

		var arguments = new Dictionary<string, CallState>
		{
			["0"] = new CallState(request.To),
			["1"] = new CallState(subjectAndBody)
		};

		// The send arm is selected by the *last* switch, so NOEVAL cannot be the one that ends the
		// list; SEND is named explicitly to close that over any future reordering.
		string[] switches = request.Urgent ? ["NOEVAL", "URGENT", "SEND"] : ["NOEVAL", "SEND"];

		var result = await commandInvoker.InvokeAsync("@MAIL", character, arguments, switches);
		var message = result?.Message?.ToPlainText() ?? string.Empty;

		if (message.StartsWith("#-1", StringComparison.Ordinal))
		{
			return StatusCode(StatusCodes.Status403Forbidden, new { error = message });
		}

		// @mail returns the dbrefs it actually delivered to. Nothing means every name was refused, and
		// the reason went to the character as a notification rather than into this response.
		if (string.IsNullOrWhiteSpace(message))
		{
			return NotFound(new { error = $"No such character: {request.To}" });
		}

		logger.LogInformation("Web mail sent from {From} to {To}.", character, request.To);
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

	/// <summary>
	/// The 1-based message number a caller quotes, as the 0-based index the mail query takes; null
	/// for a number below 1, which names no message.
	/// </summary>
	private static int? FolderIndex(int number) => number >= 1 ? number - 1 : null;

	private static async Task<string> FromNameAsync(SharpMail mail)
		=> (await mail.From.WithCancellation(CancellationToken.None)).Object()?.Name ?? "(unknown)";

	/// <summary>Resolves the character this request acts as (the primary character's dbref) to a player, or null.</summary>
	private async Task<SharpPlayer?> ResolvePlayerAsync(CancellationToken ct)
	{
		if (User.GetActingCharacter() is not { } character) return null;

		var result = await mediator.Send(new GetObjectNodeQuery(character), ct);
		return result.IsPlayer ? result.AsPlayer : null;
	}
}
