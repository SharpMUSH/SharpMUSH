using System.Security.Claims;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Every character in the game, for staff: the list with search and filters, one character's detail,
/// and booting or warning one.
///
/// Routes:
///   GET  api/admin/characters                 — list  ?search=&amp;online=&amp;flag=&amp;account=&amp;page=&amp;pageSize=
///   GET  api/admin/characters/{dbref}         — detail
///   POST api/admin/characters/{dbref}/boot    — boot  ?created=&amp;reason=
///   POST api/admin/characters/{dbref}/warn    — warn  ?created=, the reason in the body
///   POST api/admin/characters/{dbref}/link    — link to an account ?created=, the account's username in the body
///
/// Unlinking a character from its account is <c>DELETE api/admin/accounts/{key}/characters/{dbref}</c>.
/// </summary>
[ApiController]
[Route("api/admin/characters")]
[Authorize(Policy = PortalPermission.PlayersView)]
public class AdminCharactersController(
	IMediator mediator,
	IAccountService accounts,
	IConnectionService connections,
	IAuthorizationService authorization,
	IVisibleWorldProjection projection,
	IEngineCommandInvoker commandInvoker,
	IEventService events,
	IAuditLog audit) : ControllerBase
{
	/// <summary>The most rows one page holds.</summary>
	public const int MaxPageSize = 200;

	[HttpGet]
	public async Task<IActionResult> List(
		[FromQuery] string? search = null,
		[FromQuery] bool? online = null,
		[FromQuery] string? flag = null,
		[FromQuery] string? account = null,
		[FromQuery] int page = 1,
		[FromQuery] int pageSize = 50,
		CancellationToken ct = default)
	{
		pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
		page = Math.Max(page, 1);

		var named = await mediator.CreateStream(new GetAllPlayersQuery())
			.Where(player => search is not { Length: > 0 } || player.Object.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
			.ToListAsync(ct);

		named.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Object.Name, b.Object.Name));
		var skip = (page - 1) * pageSize;

		// Without the filters a row answers, only the page's rows are built: each costs an account
		// lookup, the flags and an attribute read.
		if (online is null && flag is not { Length: > 0 } && account is not { Length: > 0 })
		{
			var rows = new List<AdminCharacterRow>();
			foreach (var player in named.Skip(skip).Take(pageSize))
				rows.Add(await RowAsync(player, ct));
			return Ok(new AdminCharacterPage(rows, named.Count));
		}

		var matched = new List<AdminCharacterRow>();
		foreach (var player in named)
		{
			var row = await RowAsync(player, ct);
			if (online is { } wanted && row.Online != wanted) continue;
			if (flag is { Length: > 0 }
				&& !row.Flags.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(flag, StringComparer.OrdinalIgnoreCase))
				continue;
			if (account is { Length: > 0 }
				&& !(row.AccountName?.Contains(account, StringComparison.OrdinalIgnoreCase) ?? false))
				continue;
			matched.Add(row);
		}

		return Ok(new AdminCharacterPage(matched.Skip(skip).Take(pageSize).ToList(), matched.Count));
	}

	[HttpGet("{dbref:int}")]
	public async Task<IActionResult> Get(int dbref, CancellationToken ct)
	{
		if (await mediator.Send(new GetObjectNodeQuery(new DBRef(dbref)), ct) is not (AnySharpObject and SharpPlayer player))
			return NotFound();

		var playerRef = player.Object.DBRef;
		var mail = await mediator.CreateStream(new GetAllMailListQuery(player)).ToListAsync(ct);
		var attributes = await mediator.CreateStream(new GetLazyAttributesQuery(playerRef, ".*", false,
			IAttributeService.AttributePatternMode.Regex)).CountAsync(ct);
		var mayReadSite = (await authorization.AuthorizeAsync(User, PortalPermission.ServerAdmin)).Succeeded;
		var grants = await player.Object.Grants.WithCancellation(ct);

		return Ok(new AdminCharacterDetail(
			await RowAsync(player, ct),
			DateTimeOffset.FromUnixTimeMilliseconds(player.Object.CreationTime),
			attributes,
			mail.Count,
			mail.Count(message => !message.Read),
			await AttributeAsync(playerRef, "LASTLOGOUT", ct),
			mayReadSite ? await AttributeAsync(playerRef, "LASTSITE", ct) : null,
			mayReadSite ? await AttributeAsync(playerRef, "LASTIP", ct) : null,
			ObjectGrantsDisplay.Roles(grants).Split(' ', StringSplitOptions.RemoveEmptyEntries),
			await connections.Get(playerRef)
				.Where(connection => connection.State == IConnectionService.ConnectionState.LoggedIn)
				.Select(connection => connection.Handle)
				.ToListAsync(ct)));
	}

	/// <param name="created">
	/// The character's creation time, as the list reported it. A dbref number alone does not say which
	/// character a page that has stayed open meant: numbers are reused after a nuke.
	/// </param>
	[HttpPost("{dbref:int}/boot")]
	[Authorize(Policy = PortalPermission.PlayersModerate)]
	/// <param name="reason">Why, when the staff member gave a reason; kept in the audit log.</param>
	public async Task<IActionResult> Boot(int dbref, [FromQuery] long? created, [FromQuery] string? reason,
		CancellationToken ct)
	{
		if (reason is { Length: > AdminBansController.MaxReasonLength })
			return BadRequest(new ApiErrorDto($"Keep the reason to {AdminBansController.MaxReasonLength} characters."));
		if (await User.ResolveExecutorAsync(projection, ct) is not { } executor)
			return Conflict(new ApiErrorDto("Choose a character to act as before booting anyone."));
		if (await mediator.Send(new GetObjectNodeQuery(new DBRef(dbref)), ct) is not (AnySharpObject and SharpPlayer player))
			return NotFound();
		if (created is { } stamp && player.Object.CreationTime != stamp)
			return Conflict(new ApiErrorDto($"#{dbref} is now a different character. Reload and try again."));
		if (!await connections.Get(player.Object.DBRef).AnyAsync(c => c.State == IConnectionService.ConnectionState.LoggedIn, ct))
			return Conflict(new ApiErrorDto($"{player.Object.Name} is not connected."));

		// @BOOT does the work, checks the permission and records itself in the audit log, as the portal's.
		using var portal = audit.BeginPortal(User, reason);
		var result = await commandInvoker.InvokeAsync("@BOOT", executor.Object().DBRef,
			new Dictionary<string, CallState> { ["0"] = new(player.Object.DBRef.ToString()) });
		return result?.Message?.ToPlainText() is { } message && message.StartsWith("#-1", StringComparison.Ordinal)
			? StatusCode(StatusCodes.Status403Forbidden, new ApiErrorDto(message))
			: NoContent();
	}

	/// <summary>
	/// Fires the game's <c>PLAYER`WARN</c> event: the staff member's character is the enactor, <c>%0</c> the
	/// warned character's objid, <c>%1</c> the reason and <c>%2</c> the staff account's username. What a
	/// warning does is the handler's business; nothing else is stored apart from the audit entry.
	/// </summary>
	[HttpPost("{dbref:int}/warn")]
	[Authorize(Policy = PortalPermission.PlayersModerate)]
	public async Task<IActionResult> Warn(int dbref, [FromQuery] long? created, [FromBody] AdminWarnRequest request,
		CancellationToken ct)
	{
		if (string.IsNullOrWhiteSpace(request.Reason))
			return BadRequest(new ApiErrorDto("Give a reason for the warning."));
		if (request.Reason.Length > AdminBansController.MaxReasonLength)
			return BadRequest(new ApiErrorDto($"Keep the reason to {AdminBansController.MaxReasonLength} characters."));
		if (await User.ResolveExecutorAsync(projection, ct) is not { } executor)
			return Conflict(new ApiErrorDto("Choose a character to act as before warning anyone."));
		if (await mediator.Send(new GetObjectNodeQuery(new DBRef(dbref)), ct) is not (AnySharpObject and SharpPlayer player))
			return NotFound();
		if (created is { } stamp && player.Object.CreationTime != stamp)
			return Conflict(new ApiErrorDto($"#{dbref} is now a different character. Reload and try again."));

		var staff = User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } accountId
			? (await accounts.GetByIdAsync(accountId, ct))?.Username ?? ""
			: "";
		var reason = request.Reason.Trim();
		await events.TriggerEventAsync("PLAYER`WARN", executor.Object().DBRef,
			player.Object.DBRef.ToString(), reason, staff);
		await audit.RecordPortalAsync(User, AuditActions.PlayerWarn, AuditTargets.Of(player),
			AuditLog.WithReason(null, reason), ct);
		return NoContent();
	}

	/// <summary>
	/// Links a character to an account without its password: one made with <c>@pcreate</c> or imported, or one
	/// whose holder cannot prove it. The account takes on the character's roles, so as with
	/// <c>@newpassword</c> only God links God and only a wizard links a wizard. A character another account
	/// holds is refused; unlink it from that account first.
	/// </summary>
	[HttpPost("{dbref:int}/link")]
	[Authorize(Policy = PortalPermission.PlayersModerate)]
	public async Task<IActionResult> Link(int dbref, [FromQuery] long? created, [FromBody] AdminLinkCharacterRequest request,
		CancellationToken ct)
	{
		if (string.IsNullOrWhiteSpace(request.Account))
			return BadRequest(new ApiErrorDto("Name the account to link the character to."));
		if (await User.ResolveExecutorAsync(projection, ct) is not { } executor)
			return Conflict(new ApiErrorDto("Choose a character to act as before linking anyone."));
		if (await mediator.Send(new GetObjectNodeQuery(new DBRef(dbref)), ct) is not (AnySharpObject and SharpPlayer player))
			return NotFound();
		if (created is { } stamp && player.Object.CreationTime != stamp)
			return Conflict(new ApiErrorDto($"#{dbref} is now a different character. Reload and try again."));
		if (await accounts.GetByUsernameAsync(request.Account.Trim(), ct) is not { } account)
			return NotFound(new ApiErrorDto($"No account named '{request.Account.Trim()}'."));

		AnySharpObject target = player;
		if (target.IsGod() ? !executor.IsGod() : await target.IsWizard() && !await executor.IsWizard())
			return StatusCode(StatusCodes.Status403Forbidden, new ApiErrorDto($"You may not link {player.Object.Name}."));

		return await accounts.AttachCharacterAsync(account.Id!, player, ct) switch
		{
			SharpPlayer => await LinkedAsync(player, account, ct),
			LinkedElsewhere elsewhere => Conflict(new ApiErrorDto(
				$"{player.Object.Name} is linked to account '{elsewhere.Account.Username}'. Unlink it there first.")),
		};
	}

	private async Task<IActionResult> LinkedAsync(SharpPlayer player, SharpAccount account, CancellationToken ct)
	{
		await audit.RecordPortalAsync(User, AuditActions.CharacterLink, AuditTargets.Of(player), account.Username, ct);
		return NoContent();
	}

	private async Task<AdminCharacterRow> RowAsync(SharpPlayer player, CancellationToken ct)
	{
		var playerRef = player.Object.DBRef;
		var owner = await accounts.GetAccountForCharacterAsync(playerRef, ct);
		var flags = (await player.Object.ReadFlagsAsync(ct)).Flags.Select(f => f.Name);
		return new AdminCharacterRow(
			player.Object.Key,
			player.Object.CreationTime,
			player.Object.Name,
			owner?.Id?.Split('/')[^1],
			owner?.Username,
			await connections.Get(playerRef).AnyAsync(c => c.State == IConnectionService.ConnectionState.LoggedIn, ct),
			string.Join(' ', flags),
			await AttributeAsync(playerRef, "LAST", ct),
			owner is not null && await accounts.IsGodsAccountAsync(owner.Id!, ct));
	}

	private async Task<string?> AttributeAsync(DBRef dbref, string name, CancellationToken ct)
		=> await mediator.CreateStream(new GetAttributeQuery(dbref, [name])).LastOrDefaultAsync(ct) is { } attribute
			? attribute.Value.ToPlainText()
			: null;
}
