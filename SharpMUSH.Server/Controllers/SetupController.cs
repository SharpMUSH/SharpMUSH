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
/// <para>The claim is the wizard's first step. The rest — importing a PennMUSH database and its
/// <c>mush.cnf</c>, setting the HTTP and event handlers, choosing the bundled packages — is the
/// administrator's (<c>api/setup/wizard</c>), and stays pending until they finish it, so closing the tab
/// after the claim does not lose it.</para>
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
	HandlerSetupService handlers,
	StarterWikiService starterWiki,
	IConfigOptionWriter config,
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

	/// <summary>Whether the wizard is unfinished, the game's handlers, and the packages it offers.</summary>
	[HttpGet("wizard")]
	[Authorize(Policy = PortalPermission.ServerAdmin)]
	public async Task<ActionResult<SetupWizardResponse>> GetWizard(CancellationToken cancellationToken)
		=> Ok(await WizardAsync(cancellationToken));

	/// <summary>
	/// Sets the <c>http</c> or <c>event</c> handler: an object the game has, a new one, or none. The bundled
	/// packages built on the old handler move to the new one. Answers with the wizard's state afterwards; 409
	/// with the reason when the change was refused or only partly made.
	/// </summary>
	[HttpPut("wizard/handlers/{kind}")]
	[Authorize(Policy = PortalPermission.ServerAdmin)]
	public async Task<IActionResult> SetHandler(string kind, [FromBody] SetHandlerRequest request,
		CancellationToken cancellationToken)
		=> await handlers.SetAsync(kind, request, cancellationToken) switch
		{
			Success => Ok(await WizardAsync(cancellationToken)),
			Error<string> error => Conflict(error.Value),
		};

	/// <summary>
	/// What building the <c>http</c> or <c>event</c> handler's packages onto object <paramref name="dbref"/> would
	/// leave not running: the attributes they write that the object already has from elsewhere, which the install
	/// keeps. Read-only.
	/// </summary>
	[HttpGet("wizard/handlers/{kind}/clashes")]
	[Authorize(Policy = PortalPermission.ServerAdmin)]
	public async Task<IActionResult> HandlerClashes(string kind, [FromQuery] int dbref, CancellationToken cancellationToken)
		=> await handlers.ClashesAsync(kind, dbref, cancellationToken) switch
		{
			IReadOnlyList<HandlerClash> clashes => Ok(clashes),
			Error<string> error => NotFound(error.Value),
		};

	/// <summary>
	/// Installs the listed bundled packages, with what they depend on, and removes the other bundled packages.
	/// 409 with the reason when some could not be changed; the others still were.
	/// </summary>
	[HttpPut("wizard/packages")]
	[Authorize(Policy = PortalPermission.ServerAdmin)]
	public async Task<IActionResult> SetPackages([FromBody] SetupPackagesRequest request,
		CancellationToken cancellationToken)
		=> await features.ApplyPackagesAsync(request.Installed ?? [], cancellationToken) switch
		{
			Success => Ok(await WizardAsync(cancellationToken)),
			Error<string> error => Conflict(error.Value),
		};

	/// <summary>
	/// Writes the starter wiki pages the game does not have yet: Getting Started, Theme, Setting, Policies and the
	/// categories that file them. Pages the game already has are left as they are. 409 naming the pages that could
	/// not be written; the others still were.
	/// </summary>
	[HttpPost("wizard/starter-wiki")]
	[Authorize(Policy = PortalPermission.ServerAdmin)]
	public async Task<IActionResult> ApplyStarterWiki(CancellationToken cancellationToken)
		=> await starterWiki.ApplyAsync() switch
		{
			Success => Ok(await WizardAsync(cancellationToken)),
			Error<string> error => Conflict(error.Value),
		};

	/// <summary>Closes the wizard. What it set up stays as it is.</summary>
	[HttpPost("wizard/finish")]
	[Authorize(Policy = PortalPermission.ServerAdmin)]
	public async Task<IActionResult> FinishWizard()
	{
		// After the wizard's mush.cnf import, which may have named mud_url or cleared it.
		await RememberWebsiteAsync();
		await features.SetWizardPendingAsync(false);
		return NoContent();
	}

	private async Task<SetupWizardResponse> WizardAsync(CancellationToken cancellationToken)
		=> new(await features.WizardPendingAsync(), await handlers.HandlersAsync(cancellationToken),
			await features.PackagesAsync(), await starterWiki.AppliedAsync());

	/// <summary>
	/// Sign the claimer in as the administrator they just became.
	/// </summary>
	private async Task<IActionResult> ClaimedAsync(SharpAccount account, string clientIp)
	{
		await RememberWebsiteAsync();

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
			var permissions = await accountClaims.ComputeGrantedScopesAsync(account.Id!);

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
	/// <summary>
	/// Sets <c>mud_url</c>, while it is unset, to the address the administrator reached the portal at: the
	/// browser's <c>Origin</c>, else the request's own scheme and host. It is the game's web address, which
	/// <c>@version</c> and MSSP report and an MXP or Pueblo client fetches the game's pictures from. A
	/// loopback address is left out: it is no one else's way in. Best-effort; setup never fails over it.
	/// </summary>
	private async Task RememberWebsiteAsync()
	{
		try
		{
			if (config.PropertyFor("mud_url") is not { } property
				|| !string.IsNullOrWhiteSpace((await config.CurrentAsync()).Net.MudUrl))
				return;

			var origin = Uri.TryCreate(Request.Headers.Origin.ToString(), UriKind.Absolute, out var sent)
				&& sent.Scheme is "http" or "https"
				? sent
				: Uri.TryCreate($"{Request.Scheme}://{Request.Host}", UriKind.Absolute, out var served) ? served : null;
			if (origin is null || origin.IsLoopback) return;

			await config.SetAsync(property, origin.GetLeftPart(UriPartial.Authority));
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logger.LogWarning(ex, "Could not set mud_url from the setup request");
		}
	}
}
