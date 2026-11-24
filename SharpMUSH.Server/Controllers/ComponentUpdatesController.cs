using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// The server page's updates panel, behind <c>server.admin</c>. The update agent does the work; this
/// only queues it and reports what the agent last said.
///
/// Routes:
///   GET  api/admin/updates
///   POST api/admin/updates
/// </summary>
[ApiController]
[Route("api/admin/updates")]
[Authorize(Policy = PortalPermission.ServerAdmin)]
public sealed class ComponentUpdatesController(ComponentUpdateCoordinator coordinator, IConnectionService connections, IAuditLog audit)
	: ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<AdminUpdatesStatus>> Status(CancellationToken ct)
	{
		var (open, players) = await CountAsync(ct);
		return Ok(coordinator.Status(open, players));
	}

	[HttpPost]
	public async Task<IActionResult> Queue(AdminUpdateRequest request) =>
		await coordinator.QueueAsync(request, User.Identity?.Name ?? "administrator") switch
		{
			UpdateJobStatus job => await QueuedAsync(job),
			Error<string> error => Conflict(new { error = error.Value }),
		};

	private async Task<IActionResult> QueuedAsync(UpdateJobStatus job)
	{
		var details = string.Join(" ", job.Components) + (job.Tag is { } tag ? $" to {tag}" : "");
		await audit.RecordPortalAsync(User, AuditActions.ServerUpdate, new AuditTarget(AuditTargetKinds.Server, job.Id.ToString(), "Component update"), details);
		return Accepted(job);
	}

	private async Task<(int Open, int Players)> CountAsync(CancellationToken ct)
	{
		var open = await connections.GetAll().ToListAsync(ct);
		var players = open
			.Where(connection => connection.State == IConnectionService.ConnectionState.LoggedIn)
			.Select(connection => connection.Ref)
			.Distinct()
			.Count();
		return (open.Count, players);
	}
}

/// <summary>
/// The update agent's poll. It authenticates with the deployment's <c>SHARPMUSH_UPDATE_AGENT_TOKEN</c>,
/// not a portal account, and is answered with the update to start, if one waits.
///
/// Routes:
///   POST api/update-agent/poll
/// </summary>
[ApiController]
[Route("api/update-agent")]
[RequestSizeLimit(262144)]
[AllowAnonymous]
public sealed class UpdateAgentController(ComponentUpdateCoordinator coordinator) : ControllerBase
{
	public const string TokenHeader = "X-Update-Agent-Token";

	/// <summary>Headers a proxy in front of the server adds. The agent reaches the server on the Compose network, never through one.</summary>
	public static readonly IReadOnlyList<string> ProxyHeaders = ["X-Forwarded-For", "X-Original-For", "Forwarded", "CF-Connecting-IP"];

	[HttpPost("poll")]
	public IActionResult Poll(UpdateAgentReport report)
	{
		// From the internet, the endpoint does not exist: a leaked token is no use outside the game's own network.
		if (ProxyHeaders.Any(Request.Headers.ContainsKey)) return NotFound();
		if (!coordinator.Authenticates(Request.Headers[TokenHeader].ToString())) return Unauthorized();
		Response.Headers.CacheControl = "no-store";
		if (report.Protocol != ComponentUpdateCoordinator.Protocol)
			return StatusCode(StatusCodes.Status426UpgradeRequired, new { protocol = ComponentUpdateCoordinator.Protocol });
		return coordinator.Poll(report) switch
		{
			UpdateAgentJob job => (IActionResult)Ok(job),
			Library.DiscriminatedUnions.NotFound => NoContent(),
		};
	}
}
