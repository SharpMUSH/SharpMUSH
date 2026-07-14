using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// First-run setup endpoints, gated on the game-wide ServerState.SetupCompleted flag.
/// While setup is incomplete, the first visitor to complete the wizard claims the
/// pre-generated admin account (renames it and sets its password).
/// </summary>
[ApiController]
[Route("api/setup")]
public class SetupController(SetupService setupService) : ControllerBase
{
	public record SetupStatusResponse(bool NeedsSetup);
	public record SetupCompleteRequest(string Username, string Password);

	[HttpGet("status")]
	[EnableRateLimiting("public-api")]
	public async Task<IActionResult> GetStatus()
		=> Ok(new SetupStatusResponse(await setupService.NeedsSetupAsync()));

	[HttpPost("complete")]
	[EnableRateLimiting("public-api")]
	public async Task<IActionResult> Complete([FromBody] SetupCompleteRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
			return BadRequest("Username and Password are required.");
		if (request.Password.Length < 8)
			return BadRequest("Password must be at least 8 characters.");

		var result = await setupService.CompleteAsync(request.Username.Trim(), request.Password);
		return result.Match<IActionResult>(
			_ => Ok(new { Message = "Setup complete. You can now log in." }),
			err => Conflict(err.Value));
	}
}
