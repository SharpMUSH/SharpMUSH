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
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
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
			"/admin/config/signin/" + provider.Id, PortalPermission.ConfigAdmin)
		{
			Options = provider.RequiresClientSecret
				? [.. SignInConfigurationFields.ForProvider(provider.Id), SignInConfigurationFields.ClientSecret(provider.SecretName, provider.DisplayName)]
				: SignInConfigurationFields.ForProvider(provider.Id)
		}));
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
				&& tile.HasValidOptions() && await MayViewAsync(tile.Permission))
			{
				var options = new List<ConfigurationOption>();
				foreach (var option in tile.Options ?? [])
					if (option.Permission is null || await MayViewAsync(option.Permission)) options.Add(option);
				visible.Add(tile with { Options = tile.Options is null ? null : options });
			}
		}
		return Ok(visible.DistinctBy(tile => tile.Id, StringComparer.OrdinalIgnoreCase)
			.OrderBy(tile => tile.Title, StringComparer.OrdinalIgnoreCase).ToList());
	}

	/// <summary>Only authorized descriptions and destinations, never configuration/default values.</summary>
	[HttpGet("options")]
	public async Task<ActionResult<IReadOnlyList<ConfigurationSearchEntry>>> Options()
	{
		if (!await MayViewAsync(PortalPermission.ConfigAdmin)) return Forbid();
		var rows = SharpMUSH.Library.API.SchemaBuilder.BuildSearchMetadata().ToList();
		var tiles = (IReadOnlyList<ApplicationConfigurationTile>)((OkObjectResult)(await List()).Result!).Value!;
		foreach (var tile in tiles)
			foreach (var option in tile.Options ?? [])
				rows.Add(new("app:" + tile.Id + ":" + option.Key, tile.Title, tile.Title, option.Route ?? tile.Route, option));
		return Ok(rows);
	}

	private async Task<bool> MayViewAsync(string permission) =>
		(await authorization.AuthorizeAsync(User, null, [new PermissionRequirement(permission)])).Succeeded;
}
