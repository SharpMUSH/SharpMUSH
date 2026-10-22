using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Server.Authentication.Passkeys;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication.External;
using SharpMUSH.Library.Authorization;
namespace SharpMUSH.Server.Authentication;

public sealed class AccountSecurityService(AccountRecentProofService proofs, IAccountService accounts,
 IAccountSessionStore sessions, IBanEnforcer enforcer, IMediator mediator, ExternalProviderCatalog providers, AccountLoginCompletion? login = null, IAccountProofAuthorityValidator? authorityValidator = null, PasskeyService? passkeys = null)
{
	/// <summary>Existing password-based settings forms can prove the dedicated password directly.
	/// Character passwords never satisfy this proof.</summary>
	public async Task<Result<T>> WithProofAsync<T>(HttpContext context, string accountId, AccountSecurityOperation operation,
	 string? grant, string? currentPassword, Func<SharpAccount, AccountProofAuthority, Task<Result<T>>> action, CancellationToken ct = default)
	{
		if (ExternalLoginJourney.Bearer(context) is not { } token) return new Error<string>("Sign in again.");
		if (string.IsNullOrEmpty(grant) && currentPassword is not null)
		{
			if (await proofs.PasswordAsync(accountId, token, currentPassword, operation, ct) is not RecentAccountProof proof)
				return new Error<string>("Current account password is incorrect.");
			grant = proof.Grant;
		}
		return await proofs.WithProofAsync(accountId, token, operation, grant, action, ct);
	}
	public async Task RevokeAsync(string accountId, CancellationToken ct = default)
	{
		await sessions.RevokeAllForAccountAsync(accountId, ct);
		await enforcer.EnforceAccountBanAsync(accountId, ct);
	}
	public async Task RevokeAndReplaceAsync(HttpContext context, string accountId, AccountProofAuthority authority, CancellationToken ct = default)
	{
		var token = ExternalLoginJourney.Bearer(context);
		var source = token is null ? new Found<IAccountSessionStore.SessionIdentity>(new NotFound()) : await sessions.InspectAsync(token, ct);
		var remembered = RememberedLoginCookie.Read(context.Request) is { } cookie && await sessions.RedeemRememberedLoginAsync(cookie, ct) == accountId;
		context.Response.Headers["X-SharpMUSH-Signed-Out"] = "true";
		await RevokeAsync(accountId, ct);
		RememberedLoginCookie.Delete(context.Response);
		if (login is null || source is not IAccountSessionStore.SessionIdentity binding || await accounts.GetByIdAsync(accountId, ct) is not { IsActive: true } account) return;
		var survives = authority.Method switch
		{
			"password" => !string.IsNullOrEmpty(account.PasswordHash),
			"passkey" => passkeys is not null && (await passkeys.ListAsync(accountId, ct)).Any(key => PasskeyService.IdOf(key) == authority.IdentityId),
			"external" => authorityValidator is not null && await authorityValidator.IsCurrentAsync(accountId, authority, ct),
			_ => false
		};
		if (!survives) return;
		if (await login.CompleteAsync(context, account, remembered, binding.CharacterKey, binding.CharacterCreationTime, ct) is OkObjectResult { Value: AuthController.AccountLoginResponse replacement })
		{
			context.Response.Headers.Remove("X-SharpMUSH-Signed-Out");
			context.Response.Headers["X-SharpMUSH-Replacement-Session"] = replacement.AccountSessionToken;
		}
	}
	public Task<Result<Success>> PasswordAsync(HttpContext context, string accountId, string newPassword, string? grant, string? oldPassword, CancellationToken ct)
	 => WithProofAsync(context, accountId, AccountSecurityOperation.AccountPassword, grant, oldPassword, async (_, authority) =>
	 {
		 if (!AccountPasswordRule.Accepts(newPassword)) return new Error<string>(AccountPasswordRule.Requirement);
		 var result = await accounts.SetPasswordAsync(accountId, newPassword, false, ct);
		 if (result is Success) await RevokeAndReplaceAsync(context, accountId, authority, ct);
		 return result;
	 }, ct);
	public Task<Result<Success>> EmailAsync(HttpContext context, string accountId, string? email, string? grant, string? password, CancellationToken ct)
	 => WithProofAsync(context, accountId, AccountSecurityOperation.Email, grant, password, async (_, authority) =>
	 {
		 var result = await mediator.Send(new SetAccountEmailCommand(accountId, email), ct);
		 if (result is Success) await RevokeAndReplaceAsync(context, accountId, authority, ct);
		 return result;
	 }, ct);
	public async Task<IReadOnlyCollection<string>> EnabledAsync(CancellationToken ct) => await providers.EnabledRegistrationsAsync(ct);
}
