using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Applications;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Controllers;

/// <summary>The reusable host's public code assets and authenticated instance bootstrap.</summary>
[ApiController]
public sealed class HostedApplicationsController(
	UiPackageStore packages, IApplicationRegistryService registry, IOptionsWrapper<SharpMUSHOptions> options) : ControllerBase
{
	[AllowAnonymous]
	[HttpGet("api/ui/{id}/assets/{file}")]
	public async Task<IActionResult> Asset(string id, string file, CancellationToken ct)
	{
		if (!options.CurrentValue.Database.AllowBrowserCode || file == UiDefinition.FileName) return NotFound();
		if (await packages.ReadAsync(id, file, ct) is not { } bytes || UiDefinition.ContentType(file) is not { } contentType)
			return NotFound();
		Response.Headers.CacheControl = "no-cache";
		return File(bytes, contentType);
	}

	[Authorize]
	[HttpGet("api/applications/{slug}/host")]
	public async Task<IActionResult> Bootstrap(string slug, CancellationToken ct)
	{
		if (!options.CurrentValue.Database.AllowBrowserCode
			|| await registry.GetApplicationAsync(slug) is not RegisteredApplication app
			|| app.RenderKind != ApplicationRenderKind.Hosted || app.DefinitionId is null) return NotFound();
		var role = User.FindAll(ClaimTypes.Role)
			.Select(c => Enum.TryParse<PortalRole>(c.Value, true, out var r) ? r : PortalRole.Guest)
			.DefaultIfEmpty(PortalRole.Guest).Max();
		if (role < app.MinimumRole || (app.Permission is { Length: > 0 } scope
			&& !User.FindAll(PortalPermission.ClaimType).Any(c => string.Equals(c.Value, scope, StringComparison.OrdinalIgnoreCase))))
			return Forbid();
		if (await packages.GetAsync(app.DefinitionId, ct) is not { } definition) return NotFound();
		if (app.Config is not { } config || definition.ValidateConfig(app.ConfigVersion, config) is { })
			return Conflict(new { error = "Instance configuration does not match the installed definition; update it explicitly." });
		Response.Headers.CacheControl = "no-store";
		return Ok(new
		{
			instanceId = app.Slug,
			definitionId = app.DefinitionId,
			configVersion = app.ConfigVersion,
			config,
			entryUrl = $"api/ui/{Uri.EscapeDataString(app.DefinitionId)}/assets/{Uri.EscapeDataString(definition.Entry)}"
		});
	}
}
