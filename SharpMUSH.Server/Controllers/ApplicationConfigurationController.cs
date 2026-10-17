using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Authentication.External;

namespace SharpMUSH.Server.Controllers;

/// <summary>Private, permission-filtered configuration discovery for installed applications.</summary>
[ApiController]
[Route("api/application-configuration")]
[Authorize(Policy = PortalPermission.ConfigAdmin)]
public sealed class ApplicationConfigurationController(
	IApplicationRegistryService registry,
	PluginCatalog catalog,
	IAuthorizationService authorization,
	ILogger<ApplicationConfigurationController> logger) : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<IReadOnlyList<ApplicationConfigurationTile>>> List()
	{
		var candidates = (await registry.GetApplicationsAsync()).Select(app => app.ConfigurationTile)
			.OfType<ApplicationConfigurationTile>().ToList();
		candidates.AddRange(ExternalProviderDefinition.All.Select(provider => new ApplicationConfigurationTile(
			"signin-" + provider.Id, provider.DisplayName + " sign-in", "Configure account sign-in with " + provider.DisplayName + ".", "Login",
			"/admin/config/signin/" + provider.Id, PortalPermission.ConfigAdmin)));
		foreach (var source in catalog.ApplicationSources)
		{
			try { candidates.AddRange(source.GetConfigurationTiles().OfType<ApplicationConfigurationTile>().ToList()); }
			catch (Exception exception)
			{
				logger.LogWarning(exception, "A plugin could not describe its configuration tiles.");
			}
		}

		var visible = new List<ApplicationConfigurationTile>();
		foreach (var tile in candidates)
		{
			if (tile.HasSafeRoute() && !string.IsNullOrWhiteSpace(tile.Id) && !string.IsNullOrWhiteSpace(tile.Title)
				&& !string.IsNullOrWhiteSpace(tile.Permission)
				&& await MayViewAsync(tile.Permission))
				visible.Add(tile);
		}
		return Ok(visible.DistinctBy(tile => tile.Id, StringComparer.OrdinalIgnoreCase)
			.OrderBy(tile => tile.Title, StringComparer.OrdinalIgnoreCase).ToList());
	}

	private async Task<bool> MayViewAsync(string permission) =>
		(await authorization.AuthorizeAsync(User, null, [new PermissionRequirement(permission)])).Succeeded;
}
