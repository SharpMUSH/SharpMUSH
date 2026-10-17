using SharpMUSH.Server.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SharpMUSH.Library.Models;
using SharpMUSH.Server.Authentication.External;

namespace SharpMUSH.Server.Controllers;

[ApiController]
[Route("api/auth/external")]
[EnableRateLimiting(PublicApiRateLimit.PolicyName)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ExternalLoginController(ExternalLoginJourney journey, ExternalProviderCatalog providers,
 IAntiforgery antiforgery) : ControllerBase
{
	public record BeginRequest(string ReturnPath = "/account", bool RememberMe = false,
	 ExternalFlowPurpose Purpose = ExternalFlowPurpose.Login, string? ProofGrant = null,
	 AccountSecurityOperation? Operation = null, string? IdentityId = null);
	public record SignupRequest(string Username);
	[HttpGet("providers")]
	public async Task<IActionResult> Providers(CancellationToken ct)
	 => Ok((await providers.StatusesAsync(ct)).Where(provider => provider.Ready).Select(provider => new { provider.Id, provider.DisplayName }));
	[HttpGet("csrf")]
	public IActionResult Csrf()
	{
		var token = antiforgery.GetAndStoreTokens(HttpContext);
		return Ok(new { Token = token.RequestToken, FieldName = token.FormFieldName });
	}
	[HttpPost("{provider}/start")]
	[ValidateAntiForgeryToken]
	public Task<IActionResult> Begin(string provider, [FromBody] BeginRequest request, CancellationToken ct)
	 => Enum.IsDefined(request.Purpose) ? journey.BeginAsync(HttpContext, provider, request.ReturnPath, request.RememberMe,
		request.Purpose, request.ProofGrant, request.Operation, request.IdentityId, ct) : Task.FromResult<IActionResult>(BadRequest());
	[HttpPost("challenge")]
	[ValidateAntiForgeryToken]
	public Task<IActionResult> Challenge([FromForm] string transactionId, CancellationToken ct)
	 => journey.ChallengeAsync(HttpContext, transactionId, ct);
	[HttpPost("complete")]
	[ValidateAntiForgeryToken]
	public Task<IActionResult> Complete(CancellationToken ct) => journey.FinishAsync(HttpContext, ct);
	[HttpPost("signup")]
	[ValidateAntiForgeryToken]
	public Task<IActionResult> Signup([FromBody] SignupRequest request, CancellationToken ct)
	 => journey.SignupAsync(HttpContext, request.Username, ct);
	[HttpPost("cancel")]
	[ValidateAntiForgeryToken]
	public IActionResult Cancel() { journey.Cancel(HttpContext); return NoContent(); }
}
