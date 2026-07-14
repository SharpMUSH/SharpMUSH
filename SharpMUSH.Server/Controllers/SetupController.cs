using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// First-run setup endpoints, gated on the game-wide ServerState.SetupCompleted flag.
/// While setup is incomplete, the first visitor to complete the wizard claims the
/// pre-generated admin account (renames it and sets its password). On success, the claimer
/// is minted an account session exactly like <see cref="AuthController.AccountLogin"/> does,
/// so they're auto-logged-in as the new administrator.
/// </summary>
[ApiController]
[Route("api/setup")]
public class SetupController(
	SetupService setupService,
	IAccountService accountService,
	IAccountSessionStore accountSessionStore,
	AccountClaimsService accountClaims) : ControllerBase
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
		if (result.IsT1)
			return Conflict(result.AsT1.Value);

		var account = result.AsT0;
		var characters = await accountService.GetCharactersAsync(account.Id!);
		var charSummaries = await CharacterSummaryMapper.BuildSummariesAsync(characters);

		var role = await accountClaims.ComputeAccountRoleAsync(account.Id!);
		var permissions = await accountClaims.ComputeGrantedScopesAsync(account.Id!, role);

		var sessionToken = await accountSessionStore.CreateTokenAsync(account.Id!, TimeSpan.FromMinutes(15));

		return Ok(new AuthController.AccountLoginResponse(account.Id!, account.Username, charSummaries,
			sessionToken, MustChangePassword: false, role.ToString(), permissions.ToList()));
	}
}
