using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// The Packages page's Plugins tab: which plugins run, turning one on or off, and uploading a plugin package to
/// review and apply like any other.
///
/// Routes:
///   GET  api/packages/plugins
///   POST api/packages/plugins/{id}/enable
///   POST api/packages/plugins/{id}/disable
///   POST api/packages/plugins/upload
/// </summary>
[ApiController]
[Route("api/packages/plugins")]
[Authorize(Policy = PortalPermission.PackagesAdmin)]
public class PackagePluginsController(
	IPluginAdministration plugins,
	PluginUploadStore uploads,
	PluginInstallOptions installOptions,
	IAuditLog audit) : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<PluginsResponse>> List(CancellationToken cancellationToken) =>
		Ok(await plugins.ListAsync(cancellationToken));

	[HttpPost("{id}/enable")]
	[Authorize(Policy = PortalPermission.ServerOperate)]
	public Task<ActionResult<PluginChangeResponse>> Enable(string id, CancellationToken cancellationToken) =>
		SetEnabledAsync(id, true, cancellationToken);

	[HttpPost("{id}/disable")]
	[Authorize(Policy = PortalPermission.ServerOperate)]
	public Task<ActionResult<PluginChangeResponse>> Disable(string id, CancellationToken cancellationToken) =>
		SetEnabledAsync(id, false, cancellationToken);

	private async Task<ActionResult<PluginChangeResponse>> SetEnabledAsync(string id, bool enabled, CancellationToken cancellationToken)
	{
		if (!await KnownPluginAsync(id, cancellationToken)) return BadRequest($"There is no plugin '{id}'.");
		var blockers = await plugins.GetBlockersAsync(id, enabled, cancellationToken);
		if (blockers.Packages.Count > 0 || blockers.Plugins.Count > 0) return Conflict(blockers);

		switch (await plugins.SetEnabledAsync(id, enabled, cancellationToken))
		{
			case PluginChangeResponse change:
				var removed = change.RemovedPackages.Count > 0 ? $"; uninstalled {string.Join(", ", change.RemovedPackages)}" : string.Empty;
				await RecordAsync(enabled ? AuditActions.PluginEnable : AuditActions.PluginDisable,
					new AuditTarget(AuditTargetKinds.Plugin, change.Plugin.Id, change.Plugin.Name),
					$"{(enabled ? "Turned on" : "Turned off")}{removed}", cancellationToken);
				return Ok(change);
			case Error<string> error:
				// A dependency can appear while the service waits for the package gate. A target that disappeared
				// meanwhile is still an ordinary validation error, even when its package row has dependents.
				if (!await KnownPluginAsync(id, cancellationToken)) return BadRequest(error.Value);
				blockers = await plugins.GetBlockersAsync(id, enabled, cancellationToken);
				return blockers.Packages.Count > 0 || blockers.Plugins.Count > 0
					? Conflict(blockers) : BadRequest(error.Value);
			default:
				return StatusCode(StatusCodes.Status500InternalServerError);
		}
	}

	private async Task<bool> KnownPluginAsync(string id, CancellationToken cancellationToken) =>
		(await plugins.ListAsync(cancellationToken)).Plugins.Any(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// Stages an uploaded plugin package (a <c>.zip</c> of <c>package.yaml</c> and its files, multipart field
	/// "file"). The answer names the remote and path to plan and apply it with; nothing is installed yet.
	/// </summary>
	[HttpPost("upload")]
	[RequestSizeLimit(PluginUploadStore.MaxUploadBytes + 64 * 1024)]
	public async Task<ActionResult<PluginUploadResponse>> Upload(IFormFile file, CancellationToken cancellationToken)
	{
		if (!installOptions.Allowed)
		{
			return StatusCode(StatusCodes.Status403Forbidden,
				$"This server does not install plugin packages ({PluginDirectories.InstallVariable}=false).");
		}

		if (file is null || file.Length == 0)
		{
			return BadRequest("No file uploaded.");
		}

		if (file.Length > PluginUploadStore.MaxUploadBytes)
		{
			return StatusCode(StatusCodes.Status413PayloadTooLarge,
				$"The upload is larger than {PluginUploadStore.MaxUploadBytes / (1024 * 1024)} MB.");
		}

		await using var content = file.OpenReadStream();
		switch (await uploads.StageAsync(content, cancellationToken))
		{
			case PluginUploadResponse staged:
				await RecordAsync(AuditActions.PluginUpload,
					new AuditTarget(AuditTargetKinds.Plugin, staged.PackageId, staged.PackageId),
					$"v{staged.Version}, {file.FileName}", cancellationToken);
				return Ok(staged);
			case Error<string> error:
				return BadRequest(error.Value);
			default:
				return StatusCode(StatusCodes.Status500InternalServerError);
		}
	}

	private async ValueTask RecordAsync(string action, AuditTarget target, string details, CancellationToken cancellationToken)
	{
		if (User.FindFirstValue(ClaimTypes.NameIdentifier) is { } accountId)
		{
			await audit.RecordPortalAsync(accountId, action, target, details, cancellationToken);
		}
	}
}
