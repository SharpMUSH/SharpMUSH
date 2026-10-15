using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
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
/// The server's secrets, as <c>@secret</c> manages them: list what is set and where from, set one, clear one, name
/// the hosts <c>@http</c> may send one to.
/// No request ever answers with a value. <see cref="PortalPermission.ConfigAdmin"/> manages every secret;
/// the permission a secret's declaration names manages that one.
/// </summary>
[ApiController]
[Route("api/secrets")]
[Authorize]
public class SecretsController(
	ISecretStore secrets,
	IAuthorizationService authorization,
	IAuditLog audit) : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<IReadOnlyList<SecretStatus>>> List()
	{
		List<SecretStatus> managed = [];
		foreach (var status in await secrets.ListAsync(HttpContext.RequestAborted))
		{
			if (await MayManageAsync(status.Name))
			{
				managed.Add(status);
			}
		}

		return managed.Count == 0 && !await MayAsync(PortalPermission.ConfigAdmin) ? Forbid() : Ok(managed);
	}

	[HttpPut("{name}")]
	public async Task<ActionResult<SecretStatus>> Set(string name, [FromBody] SecretSetRequest request)
	{
		if (!await MayManageAsync(name))
		{
			return Forbid();
		}

		return await secrets.SetAsync(name, request.Value ?? string.Empty, PortalSecretAuthor.Of(User), HttpContext.RequestAborted) switch
		{
			SecretStatus status => await AuditedAsync(AuditActions.SecretSet, status),
			Error<string> error => BadRequest(new { error = error.Value }),
		};
	}

	[HttpPut("{name}/hosts")]
	public async Task<ActionResult<SecretStatus>> SetHosts(string name, [FromBody] SecretHostsRequest request)
	{
		if (!await MayManageAsync(name))
		{
			return Forbid();
		}

		return await secrets.SetHostsAsync(name, request.Hosts ?? [], HttpContext.RequestAborted) switch
		{
			SecretStatus status => await AuditedAsync(AuditActions.SecretHosts, status, status.Hosts),
			Error<string> error => BadRequest(new { error = error.Value }),
		};
	}

	[HttpDelete("{name}")]
	public async Task<ActionResult<SecretStatus>> Clear(string name)
	{
		if (!await MayManageAsync(name))
		{
			return Forbid();
		}

		return await secrets.ClearAsync(name, HttpContext.RequestAborted) switch
		{
			SecretStatus status => await AuditedAsync(AuditActions.SecretClear, status),
			Library.DiscriminatedUnions.NotFound => NotFound(new { error = $"{name} has no stored value." }),
		};
	}

	private async Task<ActionResult<SecretStatus>> AuditedAsync(string action, SecretStatus status, string? details = null)
	{
		await audit.RecordPortalAsync(User, action, AuditTargets.Of(AuditTargetKinds.Secret, status.Name), details);
		return Ok(status);
	}

	private async ValueTask<bool> MayManageAsync(string name)
		=> await MayAsync(PortalPermission.ConfigAdmin)
			|| (secrets.Definitions.FirstOrDefault(definition => definition.Name == name)?.ManagedBy is { } scope
				&& await MayAsync(scope));

	/// <remarks>
	/// The requirement rather than a policy name: a plugin's permission is not one the policy provider knows.
	/// </remarks>
	private async ValueTask<bool> MayAsync(string scope)
		=> (await authorization.AuthorizeAsync(User, null, [new PermissionRequirement(scope)])).Succeeded;
}
