using DotNext.Threading;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using MarkupString;
using System.Security.Claims;

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
/// </summary>
[ApiController]
[Route("api/mail")]
[Authorize]
public class MailController(IMediator mediator, ILogger<MailController> logger) : ControllerBase
{
	private const string DefaultFolder = "INBOX";

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

		var mail = await mediator.Send(new GetMailQuery(player, number, folder), ct);
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

	[HttpPost]
	public async Task<IActionResult> Send([FromBody] SendMailRequest request, CancellationToken ct)
	{
		var sender = await ResolvePlayerAsync(ct);
		if (sender is null) return Unauthorized();

		if (string.IsNullOrWhiteSpace(request.To))
		{
			return BadRequest(new { error = "Recipient is required." });
		}

		var recipient = await ResolvePlayerByNameAsync(request.To, ct);
		if (recipient is null)
		{
			return NotFound(new { error = $"No such character: {request.To}" });
		}

		var mail = new SharpMail
		{
			DateSent = DateTimeOffset.UtcNow,
			Fresh = true,
			Read = false,
			Tagged = false,
			Urgent = request.Urgent,
			Cleared = false,
			Forwarded = false,
			Folder = DefaultFolder,
			Content = MModule.single(request.Body ?? string.Empty),
			Subject = MModule.single(request.Subject ?? string.Empty),
			From = new AsyncLazy<AnyOptionalSharpObject>(async _ =>
			{
				await ValueTask.CompletedTask;
				return sender;
			})
		};

		await mediator.Send(new SendMailCommand(sender.Object, recipient, mail), ct);
		logger.LogInformation("Web mail sent from #{From} to {To}.", sender.Object.Key, recipient.Object.Name);
		return Ok(new { sent = true });
	}

	[HttpDelete("{folder}/{number:int}")]
	public async Task<IActionResult> Delete(string folder, int number, CancellationToken ct)
	{
		var player = await ResolvePlayerAsync(ct);
		if (player is null) return Unauthorized();

		var mail = await mediator.Send(new GetMailQuery(player, number, folder), ct);
		if (mail is null) return NotFound();

		await mediator.Send(new DeleteMailCommand(mail), ct);
		return Ok(new { deleted = true });
	}

	private static async Task<string> FromNameAsync(SharpMail mail)
		=> (await mail.From.WithCancellation(CancellationToken.None)).Object()?.Name ?? "(unknown)";

	/// <summary>Resolves the authenticated character (JWT dbref) to a player, or null.</summary>
	private async Task<SharpPlayer?> ResolvePlayerAsync(CancellationToken ct)
	{
		var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (string.IsNullOrWhiteSpace(raw)) return null;

		var numberPart = raw.TrimStart('#').Split(':', 2)[0];
		if (!int.TryParse(numberPart, out var dbref)) return null;

		var result = await mediator.Send(new GetObjectNodeQuery(new DBRef(dbref, null)), ct);
		return result.IsPlayer ? result.AsPlayer : null;
	}

	private async Task<SharpPlayer?> ResolvePlayerByNameAsync(string name, CancellationToken ct)
	{
		await foreach (var player in mediator.CreateStream(new GetPlayerQuery(name)).WithCancellation(ct))
		{
			return player;
		}
		return null;
	}
}
