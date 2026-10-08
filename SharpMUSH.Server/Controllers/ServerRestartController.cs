using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Restarts the game engine, as <c>@shutdown/reboot</c> does: behind <c>server.operate</c>, the permission that
/// command needs. Connections stay open; the process's supervisor starts it again.
///
/// Routes:
///   POST api/server/restart
/// </summary>
[ApiController]
[Route("api/server/restart")]
[Authorize(Policy = PortalPermission.ServerOperate)]
public class ServerRestartController(IServerRestart restart, IAuditLog audit) : ControllerBase
{
	[HttpPost]
	public async Task<IActionResult> Restart(CancellationToken cancellationToken)
	{
		if (restart.Pending)
		{
			return Conflict("A restart is already under way.");
		}

		// Recorded first: the process stops a moment after the restart starts.
		if (User.FindFirstValue(ClaimTypes.NameIdentifier) is { } accountId)
		{
			await audit.RecordPortalAsync(accountId, AuditActions.ServerRestart,
				new AuditTarget(AuditTargetKinds.Server, "engine", "Game engine"), null, cancellationToken);
		}

		if (!await restart.RestartAsync(User.FindFirstValue(ClaimTypes.Name) ?? "the portal"))
		{
			return Conflict("A restart is already under way.");
		}

		return StatusCode(StatusCodes.Status202Accepted);
	}
}
