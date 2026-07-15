using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Authorization;
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
	AccountClaimsService accountClaims,
	ILogger<SetupController> logger) : ControllerBase
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

		// The claim itself already succeeded (CompleteAsync flipped SetupCompleted) — everything
		// below is best-effort auto-login enrichment. If any of it throws, the claimer must not
		// be handed a bare 500: that would strand them mid-wizard with no way to re-run it (setup
		// is already marked complete) and no way to tell whether their account was created. Degrade
		// instead: report success with an empty session so the client falls back to a normal
		// sign-in prompt.
		try
		{
			var characters = await accountService.GetCharactersAsync(account.Id!);
			var charSummaries = await CharacterSummaryMapper.BuildSummariesAsync(characters);

			var role = await accountClaims.ComputeAccountRoleAsync(account.Id!);
			var permissions = await accountClaims.ComputeGrantedScopesAsync(account.Id!, role);

			// Net.Logins intentionally is NOT checked here (unlike AccountLogin): this is the
			// first-run bootstrap flow, and the claimer IS the staff account being created.
			// Net.Logins gates AccountLogin to protect an already-running game; it has no
			// meaningful role to play while the game is still unclaimed.
			var originIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
			var sessionToken = await accountSessionStore.CreateTokenAsync(account.Id!, TimeSpan.FromMinutes(15), originIp);

			return Ok(new AuthController.AccountLoginResponse(account.Id!, account.Username, charSummaries,
				sessionToken, MustChangePassword: false, role.ToString(), permissions.ToList()));
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex,
				"Setup claim succeeded for account {AccountId} but post-claim auto-login enrichment failed; " +
				"returning a degraded response so the claimer can sign in manually.", account.Id);

			return Ok(new AuthController.AccountLoginResponse(account.Id!, account.Username, [],
				string.Empty, MustChangePassword: false, PortalRole.Guest.ToString(), []));
		}
	}
}
