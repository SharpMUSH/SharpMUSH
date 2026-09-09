using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Snapshots;
using SharpMUSH.Library.Services.Snapshots;
using SharpMUSH.Server.Hubs;

namespace SharpMUSH.Server.Controllers;

[ApiController, Route("api/object-snapshots"), Authorize]
public sealed class ObjectSnapshotsController(IObjectSnapshotService snapshots) : ControllerBase
{
	public sealed record CaptureRequest(string ObjectId, string Description, int Retain = 10);
	public sealed record PreviewRequest(string ObjectId, string SnapshotId, SnapshotSelection Selection);
	public sealed record ResolveRequest(string ObjectId, string RecoverySnapshotId);
	public sealed record RestoreRequest(string ObjectId, string SnapshotId, SnapshotSelection Selection, string PreviewToken);

	[HttpGet]
	public Task<IActionResult> List([FromQuery] string objectId, CancellationToken ct)
		=> Run(async () => await snapshots.ListAsync(Actor(), Target(objectId), ct));
	[HttpPost("capture")]
	public Task<IActionResult> Capture(CaptureRequest request, CancellationToken ct)
		=> Run(async () => await snapshots.CaptureAsync(Actor(), Target(request.ObjectId), request.Description, request.Retain, ct));
	[HttpPost("preview")]
	public Task<IActionResult> Preview(PreviewRequest request, CancellationToken ct)
		=> Run(async () => await snapshots.PreviewAsync(Actor(), Target(request.ObjectId), request.SnapshotId, request.Selection, ct));
	[HttpPost("restore")]
	public Task<IActionResult> Restore(RestoreRequest request, CancellationToken ct)
		=> Run(async () => await snapshots.RestoreAsync(Actor(), Target(request.ObjectId), request.SnapshotId, request.Selection, request.PreviewToken, ct));

	[HttpPost("resolve")]
	public Task<IActionResult> Resolve(ResolveRequest request, CancellationToken ct)
		=> Run(async () => { await snapshots.ResolveRecoveryAsync(Actor(), Target(request.ObjectId), request.RecoverySnapshotId, ct); return true; });

	private CapabilityActor Actor()
	{
		var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (id is null || !DBRef.TryParse(User.FindFirstValue(GameHub.CharacterDbrefClaim), out var active) || active is not { IsObjid: true })
			throw new SnapshotOperationException("denied", "Select a linked active character before using object snapshots.");
		return new(id, active, active);
	}
	private static DBRef Target(string value) => DBRef.TryParse(value, out var target) && target is { IsObjid: true } objid
		? objid : throw new SnapshotOperationException("invalid", "Use the full object identity (#number:creation).");
	private async Task<IActionResult> Run(Func<Task<object>> action)
	{
		try { return Ok(await action()); }
		catch (SnapshotOperationException ex)
		{
			return StatusCode(ex.Code switch { "denied" => 403, "missing" => 404, "stale-preview" or "recovery-required" => 409, _ => 400 }, new { error = ex.Message, code = ex.Code });
		}
	}
}
