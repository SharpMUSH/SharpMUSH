using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Authentication.External;
namespace SharpMUSH.Server.Controllers;

[ApiController]
[Route("api/admin/signin-providers")]
[Authorize(Policy = PortalPermission.ConfigAdmin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ExternalProviderConfigurationController(ExternalProviderCatalog catalog, IAuditLog audit) : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<IReadOnlyList<ExternalProviderStatus>>> List() => Ok(await catalog.StatusesAsync(HttpContext.RequestAborted));
	[HttpGet("{provider}")]
	public async Task<ActionResult<ExternalProviderStatus>> Get(string provider)
	{
		var status = (await catalog.StatusesAsync(HttpContext.RequestAborted)).SingleOrDefault(status => status.Id == provider);
		return status is null ? NotFound() : Ok(status);
	}
	[HttpPut("{provider}")]
	public async Task<ActionResult<ExternalProviderStatus>> Save(string provider, ExternalProviderSaveRequest request)
	{
		if (request.Settings is null) return BadRequest(new { error = "Provider settings are required." });
		return await catalog.SaveAsync(provider, request.Settings, request.AcknowledgeAffectedAccounts, HttpContext.RequestAborted) switch
		{
			ExternalProviderStatus status => await SavedAsync(status),
			Error<string> error => BadRequest(new { error = error.Value })
		};
	}
	private async Task<ActionResult<ExternalProviderStatus>> SavedAsync(ExternalProviderStatus status)
	{
		await audit.RecordPortalAsync(User, "external-provider.configure", AuditTargets.Of("signin-provider", status.Id),
		 "enabled=" + status.Settings.Enabled + "; affected accounts=" + status.AffectedAccounts);
		return Ok(status);
	}
}
