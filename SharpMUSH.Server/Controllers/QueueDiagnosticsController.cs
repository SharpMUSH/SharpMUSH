using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Diagnostics;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>The selected full character identity is validated by the shared queue authorization service.</summary>
[ApiController]
[Route("api/diagnostics")]
[Authorize]
[EnableRateLimiting("public-api")]
public sealed class QueueDiagnosticsController(IQueueDiagnosticsService diagnostics) : ControllerBase
{
	public sealed record ProfileRequest(string Character, int Seconds = 60);

	[HttpGet]
	public async Task<IActionResult> Inspect([FromQuery] string character, [FromQuery] int limit = 50,
		[FromQuery] Guid? beforeCursor = null, CancellationToken ct = default)
	{
		if (Actor(character) is not { } actor) return StatusCode(403);
		var result = await diagnostics.InspectAsync(actor, limit, beforeCursor, ct);
		return result.Match<IActionResult>(Ok, Error);
	}

	[HttpPost("profile")]
	public async Task<IActionResult> Start([FromBody] ProfileRequest request, CancellationToken ct = default)
	{
		if (Actor(request.Character) is not { } actor) return StatusCode(403);
		var result = await diagnostics.StartProfileAsync(actor, request.Seconds, ct);
		return result.Match<IActionResult>(id => Ok(new { id }), Error);
	}

	[HttpDelete("profile")]
	public async Task<IActionResult> Stop([FromQuery] string character, CancellationToken ct = default)
	{
		if (Actor(character) is not { } actor) return StatusCode(403);
		var result = await diagnostics.StopProfileAsync(actor, ct);
		return result.Match<IActionResult>(_ => NoContent(), Error);
	}

	private CapabilityActor? Actor(string character)
	{
		var account = User.FindFirstValue(ClaimTypes.NameIdentifier);
		return !string.IsNullOrWhiteSpace(account) && DBRef.TryParse(character, out var parsed)
			&& parsed is { IsObjid: true } full ? new CapabilityActor(account, full, full) : null;
	}

	private IActionResult Error(DiagnosticsError error) => error switch
	{
		DiagnosticsError.PermissionDenied => StatusCode(403),
		DiagnosticsError.NotFound => NotFound(),
		DiagnosticsError.Unsupported => StatusCode(501, new { error = error.ToString() }),
		DiagnosticsError.CapacityExceeded => StatusCode(429, new { error = error.ToString() }),
		_ => BadRequest(new { error = error.ToString() })
	};
}
