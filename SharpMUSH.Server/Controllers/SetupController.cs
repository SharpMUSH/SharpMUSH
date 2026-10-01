using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal.Setup;
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
///
/// <para>The claim is the wizard's first step. The rest — importing a PennMUSH database, choosing which
/// optional applications the game runs — is the administrator's (<c>api/setup/wizard</c>), and stays
/// pending until they finish it, so closing the tab after the claim does not lose it.</para>
/// </summary>
[ApiController]
[Route("api/setup")]
public class SetupController(
	SetupService setupService,
	IAccountService accountService,
	IAccountSessionStore accountSessionStore,
	AccountClaimsService accountClaims,
	SitelockGuard sitelockGuard,
	GameFeatureService features,
	ILogger<SetupController> logger) : ControllerBase
{
	public record SetupStatusResponse(bool NeedsSetup);
	public record SetupCompleteRequest(string Username, string Password);

	// Not rate-limited: every portal page load asks this before it renders, and it is one read of the
	// server state. The limiter belongs on Complete, which claims the admin account.
	[HttpGet("status")]
	public async Task<IActionResult> GetStatus()
		=> Ok(new SetupStatusResponse(await setupService.NeedsSetupAsync()));

	[HttpPost("complete")]
	[EnableRateLimiting("public-api")]
	public async Task<IActionResult> Complete([FromBody] SetupCompleteRequest request)
	{
		var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
		if (sitelockGuard.IsBlocked(clientIp, host: "", SitelockGuard.Create))
			return StatusCode(StatusCodes.Status403Forbidden, "Access from your location is restricted.");

		if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
			return BadRequest("Username and Password are required.");
		if (request.Password.Length < 8)
			return BadRequest("Password must be at least 8 characters.");

		return await setupService.CompleteAsync(request.Username.Trim(), request.Password) switch
		{
			SharpAccount account => await ClaimedAsync(account, clientIp),
			Error<string> error => Conflict(error.Value),
		};
	}

	/// <summary>Whether the wizard is unfinished, and the optional applications it offers.</summary>
	[HttpGet("wizard")]
	[Authorize(Policy = PortalPermission.ServerAdmin)]
	public async Task<ActionResult<SetupWizardResponse>> GetWizard()
		=> Ok(await WizardAsync());

	/// <summary>
	/// Turns on the listed optional applications and off the rest, then answers with what the game has
	/// afterwards. 409 with the reason when some could not be switched; the others still were.
	/// </summary>
	[HttpPut("wizard/applications")]
	[Authorize(Policy = PortalPermission.ServerAdmin)]
	public async Task<IActionResult> SetApplications([FromBody] SetupApplicationsRequest request,
		CancellationToken cancellationToken)
		=> await features.ApplyAsync(request.Enabled ?? [], cancellationToken) switch
		{
			Success => Ok(await WizardAsync()),
			Error<string> error => Conflict(error.Value),
		};

	/// <summary>Closes the wizard. What it set up stays as it is.</summary>
	[HttpPost("wizard/finish")]
	[Authorize(Policy = PortalPermission.ServerAdmin)]
	public async Task<IActionResult> FinishWizard()
	{
		await features.SetWizardPendingAsync(false);
		return NoContent();
	}

	private async Task<SetupWizardResponse> WizardAsync()
		=> new(await features.WizardPendingAsync(), await features.ApplicationsAsync());

	/// <summary>
	/// Sign the claimer in as the administrator they just became.
	/// </summary>
	private async Task<IActionResult> ClaimedAsync(SharpAccount account, string clientIp)
	{
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
			var sessionToken = await accountSessionStore.CreateTokenAsync(account.Id!, TimeSpan.FromMinutes(15), clientIp);

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
