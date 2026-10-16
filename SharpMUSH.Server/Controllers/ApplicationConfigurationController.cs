using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Services.Interfaces;

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
		foreach (var source in catalog.ApplicationSources)
		{
			try { candidates.AddRange(source.GetConfigurationTiles().ToList()); }
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

	private async Task<bool> MayViewAsync(string permission)
	{
		try { return (await authorization.AuthorizeAsync(User, permission)).Succeeded; }
		catch (InvalidOperationException exception)
		{
			logger.LogWarning(exception, "An application configuration tile names an unavailable authorization policy.");
			return false;
		}
	}
}
