using SharpMUSH.Server.Authentication;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

[ApiController]
[Route("api/admin/backups/remote")]
[Authorize(Policy = PortalPermission.ServerAdmin)]
public sealed class RemoteBackupsController(RemoteBackupCoordinator coordinator, IAuditLog audit) : ControllerBase
{
	[HttpGet]
	public async Task<ActionResult<RemoteBackupStatus>> Status() => Ok(await coordinator.StatusAsync());

	[HttpPut]
	public async Task<IActionResult> Configure(RemoteBackupConfigure request)
	{
		var error = await coordinator.ConfigureAsync(request, new(User.Identity?.Name ?? "administrator", User.Identity?.Name ?? "administrator"));
		if (error is Error<string> failure) return BadRequest(new { error = failure.Value });
		await audit.RecordPortalAsync(User, "remote-backup.configure", AuditTargets.Of("remote-backup", "configuration"), null);
		return Ok(await coordinator.StatusAsync());
	}

	[HttpPost("run")]
	public async Task<IActionResult> Run()
	{
		var result = await coordinator.QueueAsync("manual");
		if (result is not RemoteBackupRun run) return Conflict(new { error = result switch { Error<string> failure => failure.Value, _ => "Remote backup could not be queued." } });
		await audit.RecordPortalAsync(User, "remote-backup.queue", AuditTargets.Of("remote-backup", run.Id.ToString()), "manual");
		return Accepted(run);
	}
	[HttpPost("{id:guid}/abandon")]
	public async Task<IActionResult> Abandon(Guid id)
	{
		if (!await coordinator.AbandonAsync(id)) return Conflict(new { error = "Only an unknown job can be abandoned." });
		await audit.RecordPortalAsync(User, "remote-backup.abandon", AuditTargets.Of("remote-backup", id.ToString()), "copy retained");
		return Ok(await coordinator.StatusAsync());
	}

}

/// <summary>Deployment-scoped authentication, independent of portal accounts and cookies.</summary>
[ApiController]
[Route("api/backup-agent")]
[RequestSizeLimit(65536)]
[AllowAnonymous]
public sealed class RemoteBackupAgentController(RemoteBackupCoordinator coordinator) : ControllerBase
{
	private bool Authenticated()
	{
		var expected = Environment.GetEnvironmentVariable("SHARPMUSH_BACKUP_AGENT_TOKEN");
		var actual = Request.Headers["X-Backup-Agent-Token"].ToString();
		return expected is { Length: >= 32 } && actual.Length <= 512
			&& CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(actual)));
	}

	[HttpPost("claim")]
	public async Task<IActionResult> Claim()
	{
		if (!Authenticated()) return Unauthorized();
		Response.Headers.CacheControl = "no-store";
		var workerId = Request.Headers["X-Backup-Agent-Id"].ToString();
		if (!Guid.TryParseExact(workerId, "N", out _)) return BadRequest();
		return await coordinator.ClaimAsync(workerId) is RemoteBackupClaim claim ? Ok(claim) : NoContent();
	}

	[HttpPost("heartbeat")]
	public async Task<IActionResult> Heartbeat()
	{
		if (!Authenticated()) return Unauthorized();
		await coordinator.HeartbeatAsync();
		return NoContent();
	}

	[HttpPost("receipt")]
	public async Task<IActionResult> Receipt(RemoteBackupReceipt receipt)
	{
		if (!Authenticated()) return Unauthorized();
		return await coordinator.ReceiptAsync(receipt) ? NoContent() : Conflict();
	}
}
