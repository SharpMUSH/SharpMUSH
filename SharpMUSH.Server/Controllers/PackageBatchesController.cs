using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

[ApiController]
[Route("api/packages/batches")]
[Authorize(Policy = PortalPermission.PackagesAdmin)]
public sealed class PackageBatchesController(PackageBatchService batches) : ControllerBase
{
	[HttpPost("plan")]
	public async Task<ActionResult<PackageBatchPlanResponse>> Plan(PackageBatchPlanRequest request, CancellationToken cancellationToken)
	{
		if (Actor() is not { } actor) return Unauthorized();
		return await batches.PlanAsync(request, actor, cancellationToken) switch
		{
			PackageBatchPlanResponse plan => Ok(plan),
			Error<string> => Forbid()
		};
	}

	[HttpPost("apply")]
	public async Task<ActionResult<PackageBatchRunResponse>> Apply(PackageBatchApplyRequest request, CancellationToken cancellationToken)
	{
		if (Actor() is not { } actor) return Unauthorized();
		return await batches.ApplyAsync(request, actor, cancellationToken) switch
		{
			PackageBatchRunResponse run => AcceptedAtAction(nameof(Get), new { id = run.Id }, run),
			Error<string> error => Conflict(new { Message = error.Value })
		};
	}

	[HttpGet("{id}")]
	public async Task<ActionResult<PackageBatchRunResponse>> Get(string id, CancellationToken cancellationToken)
	{
		if (Actor() is not { } actor) return Unauthorized();
		return await batches.GetRunAsync(id, actor, cancellationToken) switch
		{
			PackageBatchRunResponse run => Ok(run),
			SharpMUSH.Library.DiscriminatedUnions.NotFound => StatusCode(410, new { Message = "This run is unknown or expired. Refresh actual state and request a fresh review." }),
			Error<string> => Forbid()
		};
	}

	private CapabilityActor? Actor()
	{
		if (User.GetCapabilityActor() is { } active) return active;
		if (User.HasClaim(c => c.Type == GameHub.CharacterDbrefClaim)) return null;
		return User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } account ? new(account) : null;
	}
}
