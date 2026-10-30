using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal.Navigation;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// The game's portal navigation layout: how staff grouped, ordered and hid the destinations in the rail, the drawer
/// and the phone tab bar.
///
/// Routes:
///   GET    /api/navigation — the stored layout (204 while staff never saved one: the portal uses its default)
///   PUT    /api/navigation — save a layout over the version it was read at (layout.admin; 409 when another save landed first)
///   DELETE /api/navigation — back to the default (layout.admin)
///
/// The read is anonymous, like <c>api/layouts</c>: it is presentation, and each destination in it still shows only
/// to whoever its own gate admits.
/// </summary>
[ApiController]
[Route("api/navigation")]
[RequestSizeLimit(256 * 1024)]
public sealed class NavigationController(IMediator mediator, IAuditLog audit) : ControllerBase
{
	[HttpGet]
	[AllowAnonymous]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public async Task<IActionResult> Get(CancellationToken cancellationToken)
		=> await mediator.Send(new GetNavLayoutQuery(), cancellationToken) is NavLayout layout ? Ok(layout) : NoContent();

	[HttpPut]
	[Authorize(Policy = PortalPermission.LayoutAdmin)]
	public async Task<IActionResult> Save([FromBody] SaveNavLayoutRequest? request, CancellationToken cancellationToken)
	{
		if (request is null) return BadRequest(new { error = "A layout is required." });
		if (NavLayoutRules.Problem(request.Layout) is { } problem) return BadRequest(new { error = problem });
		return await mediator.Send(new SaveNavLayoutCommand(request.Layout, request.ExpectedVersion), cancellationToken) switch
		{
			NavLayout saved => await SavedAsync(saved),
			NavLayoutConflict conflict => Conflict(conflict),
			Error<string> error => BadRequest(new { error = error.Value }),
		};
	}

	[HttpDelete]
	[Authorize(Policy = PortalPermission.LayoutAdmin)]
	public async Task<IActionResult> Reset(CancellationToken cancellationToken)
	{
		await mediator.Send(new ResetNavLayoutCommand(), cancellationToken);
		await audit.RecordPortalAsync(User, AuditActions.NavigationReset, AuditTargets.Of(AuditTargetKinds.Setting, "navigation"));
		return NoContent();
	}

	private async Task<IActionResult> SavedAsync(NavLayout saved)
	{
		await audit.RecordPortalAsync(User, AuditActions.NavigationSave, AuditTargets.Of(AuditTargetKinds.Setting, "navigation"),
			$"version {saved.Version}");
		return Ok(saved);
	}
}
