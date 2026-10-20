using SharpMUSH.Server.RateLimiting;
using System.Text.Json;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Authentication.External;
using SharpMUSH.Server.Authentication.Passkeys;

namespace SharpMUSH.Server.Controllers;

[ApiController]
[Route("api/account/security")]
[EnableRateLimiting(PublicApiRateLimit.PolicyName)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountSecurityController(IMediator mediator, IAccountService accounts, IAccountSessionStore sessions,
 BearerAccountResolver bearer, AccountRecentProofService proofs, AccountSecurityService security,
 AccountSecurityCeremonyStore ceremonies, PasskeyService passkeys, AccountCredentialGate gate,
 IPasswordService passwords, ExternalProviderCatalog catalog, IAuditLog? audit = null) : ControllerBase
{
	public record PasswordProofRequest(string Password, AccountSecurityOperation Operation);
	public record CeremonyRequest(AccountSecurityOperation Operation);
	public record AssertionRequest(string CeremonyId, JsonElement Credential);
	public record MutationRequest(string? ProofGrant);
	public record PasswordRequest(string NewPassword, string Confirmation, string? ProofGrant);
	public record FallbackRequest(bool Enabled, string? ProofGrant);
	public record LegacyUpgradeRequest(int CharacterKey, long CharacterCreationTime, string CharacterPassword, string NewPassword, string Confirmation);
	private string Token => ExternalLoginJourney.Bearer(HttpContext) ?? "";
	private async Task<IActionResult> AsAccount(Func<string, Task<IActionResult>> action, bool allowForced = false)
	 => await bearer.ResolveAsync(this, allowForced) switch { SignedInAccount account => await action(account.Id), ActionResult refused => refused };
	private IActionResult Result<T>(Result<T> result) => result switch { T value => Ok(value), Error<string> error => Conflict(error.Value), _ => BadRequest("The security operation returned no result.") };

	[HttpGet]
	public Task<IActionResult> List() => AsAccount(async id =>
	{
		var account = await accounts.GetByIdAsync(id);
		var identities = await mediator.Send(new GetExternalIdentitiesQuery(id));
		var providers = await catalog.ProvidersByRegistrationAsync(HttpContext.RequestAborted);
		return Ok(new
		{
			HasPassword = !string.IsNullOrEmpty(account?.PasswordHash),
			AllowCharacterPasswordAccountLogin = account?.AllowCharacterPasswordAccountLogin ?? false,
			Identities = identities.Select(identity => new { identity.Id, identity.Key.Registration, Provider = providers.GetValueOrDefault(identity.Key.Registration), identity.Label, identity.CreatedAt, identity.LastUsedAt }),
			Passkeys = (await passkeys.ListAsync(id)).Select(key => new { Id = PasskeyService.IdOf(key), key.Name })
		});
	});
	[HttpPost("proof/password")]
	public Task<IActionResult> PasswordProof([FromBody] PasswordProofRequest request) => AsAccount(async id =>
	 Result(await proofs.PasswordAsync(id, Token, request.Password, request.Operation, HttpContext.RequestAborted)), allowForced: true);
	[HttpPost("proof/passkey/options")]
	public Task<IActionResult> PasskeyOptions([FromBody] CeremonyRequest request) => AsAccount(async id => await gate.RunAsync<IActionResult>(async () =>
	{
		if (!Enum.IsDefined(request.Operation) || await sessions.InspectAsync(Token) is not IAccountSessionStore.SessionIdentity session || session.AccountId != id
		|| await accounts.GetByIdAsync(id) is not { IsActive: true, MustChangePassword: false } account) return Unauthorized();
		if (passkeys.BeginSignIn(Request) is not PasskeyService.Challenge challenge) return BadRequest("Passkeys are unavailable.");
		if (!ceremonies.Begin(challenge.CeremonyId, id, Token, account.CredentialEpoch, request.Operation)) return Conflict("Too many pending security requests.");
		return Ok(challenge);
	}, HttpContext.RequestAborted));
	[HttpPost("proof/passkey")]
	public Task<IActionResult> PasskeyProof([FromBody] AssertionRequest request) => AsAccount(async id => await gate.RunAsync<IActionResult>(async () =>
	{
		var binding = ceremonies.Take(request.CeremonyId, id, Token);
		if (binding is null || await accounts.GetByIdAsync(id) is not { IsActive: true, MustChangePassword: false } current || current.CredentialEpoch != binding.Epoch
		|| await passkeys.CompleteSignInAsync(request.CeremonyId, request.Credential, HttpContext.RequestAborted) is not SharpAccount account || account.Id != id
		|| !request.Credential.TryGetProperty("rawId", out var raw) || raw.GetString() is not { } credentialId) return Unauthorized("The passkey proof expired or was not verified.");
		return Result(await proofs.VerifiedAsync(id, Token, binding.Operation, "passkey", credentialId,
		async ct => (await passkeys.ListAsync(id, ct)).Any(key => PasskeyService.IdOf(key) == credentialId), HttpContext.RequestAborted));
	}, HttpContext.RequestAborted));
	[HttpPut("password")]
	public Task<IActionResult> SetPassword([FromBody] PasswordRequest request) => AsAccount(async id =>
	 request.NewPassword != request.Confirmation ? BadRequest("The password confirmation does not match.")
		: Result(await security.PasswordAsync(HttpContext, id, request.NewPassword, request.ProofGrant, null, HttpContext.RequestAborted)), allowForced: true);
	[HttpDelete("password")]
	public Task<IActionResult> RemovePassword([FromBody] MutationRequest request) => AsAccount(async id =>
	 Result(await security.WithProofAsync<Success>(HttpContext, id, AccountSecurityOperation.AccountPassword, request.ProofGrant, null, async (_, authority) =>
	 {
		 var result = await mediator.Send(new RemoveAccountPasswordCommand(id, await security.EnabledAsync(HttpContext.RequestAborted)));
		 if (result is Success) await security.RevokeAndReplaceAsync(HttpContext, id, authority, HttpContext.RequestAborted);
		 return result;
	 }, HttpContext.RequestAborted)));
	[HttpDelete("identities/{identityId}")]
	public Task<IActionResult> Unlink(string identityId, [FromBody] MutationRequest request) => AsAccount(async id =>
	 Result(await security.WithProofAsync<Success>(HttpContext, id, AccountSecurityOperation.UnlinkIdentity, request.ProofGrant, null, async (_, authority) =>
	 {
		 var identity = (await mediator.Send(new GetExternalIdentitiesQuery(id))).FirstOrDefault(identity => identity.Id == identityId);
		 if (identity is null) return new Error<string>("Identity is not linked to this account.");
		 var result = await mediator.Send(new RemoveExternalIdentityCommand(id, identity.Key, await security.EnabledAsync(HttpContext.RequestAborted)));
		 if (result is Success) await security.RevokeAndReplaceAsync(HttpContext, id, authority, HttpContext.RequestAborted);
		 return result;
	 }, HttpContext.RequestAborted)));
	[HttpPut("legacy-fallback")]
	public Task<IActionResult> Fallback([FromBody] FallbackRequest request) => AsAccount(async id =>
	 Result(await security.WithProofAsync<Success>(HttpContext, id, AccountSecurityOperation.LegacyFallback, request.ProofGrant, null, async (_, authority) =>
	 {
		 await mediator.Send(new SetAccountCharacterPasswordLoginCommand(id, request.Enabled));
		 await security.RevokeAndReplaceAsync(HttpContext, id, authority, HttpContext.RequestAborted); return new Success();
	 }, HttpContext.RequestAborted)));
	/// <summary>A deliberately separate, one-time upgrade for an account with no explicit method.
	/// Its character proof can install a dedicated password, and can never mint a general security proof.</summary>
	[HttpPost("legacy-upgrade")]
	public Task<IActionResult> LegacyUpgrade([FromBody] LegacyUpgradeRequest request) => AsAccount(async id => await gate.RunAsync<IActionResult>(async () =>
	{
		if (await sessions.InspectAsync(Token) is not IAccountSessionStore.SessionIdentity session || session.AccountId != id
		|| await accounts.GetByIdAsync(id) is not { IsActive: true, MustChangePassword: false, AllowCharacterPasswordAccountLogin: true } account
		|| !string.IsNullOrEmpty(account.PasswordHash) || (await passkeys.ListAsync(id)).Count != 0
		|| (await mediator.Send(new GetExternalIdentitiesQuery(id))).Count != 0) return Conflict("This account already has a dedicated sign-in method.");
		if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length is < 8 or > 1024 || request.NewPassword != request.Confirmation) return BadRequest("A matching new password and confirmation are required.");
		var character = (await accounts.GetCharactersAsync(id)).FirstOrDefault(c => c.Object.Key == request.CharacterKey && c.Object.CreationTime == request.CharacterCreationTime);
		if (character is null || string.IsNullOrEmpty(character.PasswordHash) || !passwords.PasswordIsValid(request.CharacterPassword, character.PasswordHash)) return Unauthorized("Character credentials were not verified.");
		var result = await accounts.SetPasswordAsync(id, request.NewPassword, false);
		if (result is Success)
		{
			if (audit is not null) await audit.RecordPortalAsync(id, AuditActions.AccountPassword, AuditTargets.Of(account), "Explicit legacy account upgrade", HttpContext.RequestAborted);
			await security.RevokeAsync(id, HttpContext.RequestAborted);
		}
		return Result(result);
	}, HttpContext.RequestAborted));
}
