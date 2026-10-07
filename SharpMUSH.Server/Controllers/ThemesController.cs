using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// The portal's themes. Reading is anonymous, since a guest's portal is themed too; a viewer with
/// <see cref="PortalPermission.LayoutAdmin"/> also sees the unpublished ones. Editing takes the same scope, as
/// layouts and the rest of the portal's presentation do.
/// </summary>
[ApiController]
[Route("api/themes")]
public class ThemesController(
	IPortalThemeService themes,
	IAuthorizationService authorization,
	IAuditLog audit) : ControllerBase
{
	[HttpGet]
	[AllowAnonymous]
	public async Task<ActionResult<PortalThemesResponse>> Get()
	{
		var editor = User.Identity?.IsAuthenticated == true
			&& (await authorization.AuthorizeAsync(User, PortalPermission.LayoutAdmin)).Succeeded;
		return Ok(await themes.GetThemesAsync(includeUnpublished: editor));
	}

	[HttpPost]
	[Authorize(Policy = PortalPermission.LayoutAdmin)]
	public async Task<ActionResult<PortalTheme>> Create([FromBody] PortalThemeRequest request)
		=> await themes.CreateAsync(request) switch
		{
			PortalTheme theme => await SavedAsync(theme, "created"),
			Error<string> error => BadRequest(new { error = error.Value }),
		};

	[HttpPut("{id}")]
	[Authorize(Policy = PortalPermission.LayoutAdmin)]
	public async Task<ActionResult<PortalTheme>> Update(string id, [FromBody] PortalThemeRequest request)
		=> await themes.UpdateAsync(id, request) switch
		{
			PortalTheme theme => await SavedAsync(theme, "saved"),
			Library.DiscriminatedUnions.NotFound => NotFound(),
			Error<string> error => BadRequest(new { error = error.Value }),
		};

	[HttpDelete("{id}")]
	[Authorize(Policy = PortalPermission.LayoutAdmin)]
	public async Task<ActionResult<PortalThemesResponse>> Delete(string id)
		=> await themes.DeleteAsync(id) switch
		{
			PortalThemesResponse left => await ChangedAsync(id, "deleted", left),
			Library.DiscriminatedUnions.NotFound => NotFound(),
			Error<string> error => BadRequest(new { error = error.Value }),
		};

	[HttpPut("default")]
	[Authorize(Policy = PortalPermission.LayoutAdmin)]
	public async Task<ActionResult<PortalThemesResponse>> SetDefault([FromBody] DefaultThemeRequest request)
		=> await themes.SetDefaultAsync(request.ThemeId) switch
		{
			PortalThemesResponse all => await ChangedAsync(request.ThemeId, "made default", all),
			Library.DiscriminatedUnions.NotFound => NotFound(),
			Error<string> error => BadRequest(new { error = error.Value }),
		};

	private async Task<ActionResult<PortalTheme>> SavedAsync(PortalTheme theme, string what)
	{
		await RecordAsync(theme.Id, what);
		return Ok(theme);
	}

	private async Task<ActionResult<PortalThemesResponse>> ChangedAsync(string id, string what, PortalThemesResponse all)
	{
		await RecordAsync(id, what);
		return Ok(all);
	}

	private ValueTask RecordAsync(string id, string what)
		=> audit.RecordPortalAsync(User, AuditActions.ConfigSet, AuditTargets.Of(AuditTargetKinds.Setting, $"theme:{id}"), what);
}
