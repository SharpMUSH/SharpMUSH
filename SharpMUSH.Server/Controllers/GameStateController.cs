using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/game/state")]
public sealed class GameStateController(IVisibleWorldProjection projection) : ControllerBase
{
	/// <summary>Current room and visible contents under the same rules as look.</summary>
	[HttpGet]
	public async Task<IActionResult> Get(CancellationToken ct)
	{
		if (User.GetCapabilityActor() is not { } actor) return Unauthorized();
		var state = await projection.GetStateAsync(actor, ct);
		return state is null ? NotFound() : Ok(state);
	}
}
