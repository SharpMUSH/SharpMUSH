using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// The audit log: every staff action, newest first.
///
/// Routes:
///   GET api/admin/audit          — ?action=&amp;actor=&amp;from=&amp;to=&amp;text=&amp;before=&amp;limit=
///   GET api/admin/audit/actions  — the action names, for the viewer's filter
/// </summary>
[ApiController]
[Route("api/admin/audit")]
[Authorize(Policy = PortalPermission.PlayersModerate)]
public class AdminAuditController(IMediator mediator) : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<AuditPage>> List(
		[FromQuery] string? action = null,
		[FromQuery] string? actor = null,
		[FromQuery] DateTimeOffset? from = null,
		[FromQuery] DateTimeOffset? to = null,
		[FromQuery] string? text = null,
		[FromQuery] string? before = null,
		[FromQuery] int limit = 50,
		CancellationToken ct = default)
		=> Ok(await mediator.Send(new GetAuditEntriesQuery(new AuditFilter(action, actor, from, to, text, before,
			Math.Clamp(limit, 1, AuditFilter.MaxLimit))), ct));

	[HttpGet("actions")]
	public ActionResult<AuditActionsResponse> Actions() => Ok(new AuditActionsResponse(AuditActions.All));
}
