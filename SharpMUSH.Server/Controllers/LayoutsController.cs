using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Portal.Widgets;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Admin-customized portal layouts. A <em>scope</em> is a named layout — <c>"global"</c> (the chrome
/// zones rendered by the shell), <c>"home"</c>, <c>"wiki-index"</c>, <c>"profile"</c>, … — and each
/// scope stores one <see cref="LayoutConfiguration"/>.
///
/// Routes:
///   GET    /api/layouts            — list scopes that have a stored (customized) layout
///   GET    /api/layouts/{scope}    — fetch one scope's layout (404 when never customized → client uses its default)
///   PUT    /api/layouts/{scope}    — create or replace one scope's layout (layout.admin)
///   DELETE /api/layouts/{scope}    — reset one scope to its code default (layout.admin)
///
/// Reads are anonymous: a layout is presentation metadata, rendered for guests on public pages
/// (the front page, the wiki). The widgets placed in it authorize their own data. Writes are gated on
/// <see cref="PortalPermission.LayoutAdmin"/>, matching application/theme editing.
/// </summary>
[ApiController]
[Route("api/layouts")]
public class LayoutsController(
	ILayoutRegistryService layouts,
	ILogger<LayoutsController> logger) : ControllerBase
{
	[HttpGet]
	[Authorize]
	public async Task<ActionResult<IReadOnlyList<string>>> List()
		=> Ok(await layouts.GetCustomizedScopesAsync());

	[HttpGet("{scope}")]
	[AllowAnonymous]
	public async Task<ActionResult<LayoutConfiguration>> Get(string scope)
	{
		var result = await layouts.GetLayoutAsync(scope);
		return result.Match<ActionResult<LayoutConfiguration>>(
			layout => Ok(layout),
			_ => NotFound());
	}

	[HttpPut("{scope}")]
	[Authorize(Policy = PortalPermission.LayoutAdmin)]
	public async Task<IActionResult> Upsert(string scope, [FromBody] LayoutConfiguration layout)
	{
		if (string.IsNullOrWhiteSpace(scope))
		{
			return BadRequest(new { error = "Scope is required." });
		}

		if (layout?.Zones is null || layout.Settings is null)
		{
			return BadRequest(new { error = "Layout must include zones and settings." });
		}

		await layouts.UpsertLayoutAsync(scope.Trim(), layout);
		logger.LogInformation("Saved layout for scope '{Scope}'.", scope);
		return Ok(layout);
	}

	[HttpDelete("{scope}")]
	[Authorize(Policy = PortalPermission.LayoutAdmin)]
	public async Task<IActionResult> Delete(string scope)
	{
		await layouts.RemoveLayoutAsync(scope.Trim());
		logger.LogInformation("Reset layout for scope '{Scope}' to default.", scope);
		return Ok(new { reset = true });
	}
}
