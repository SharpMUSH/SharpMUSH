using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Hubs;

namespace SharpMUSH.Server.Authentication;

/// <summary>
/// Authenticates REST and SignalR requests bearing an account-session token, resolving
/// role/permission claims server-side (so bans/role changes take effect on the next request)
/// and emitting the <see cref="GameHub.CharacterDbrefClaim"/> the hub authorizes on.
/// </summary>
public class AccountSessionAuthenticationHandler(
	IOptionsMonitor<AuthenticationSchemeOptions> options,
	ILoggerFactory logger,
	UrlEncoder encoder,
	IAccountSessionStore sessionStore,
	IAccountService accountService,
	AccountClaimsService accountClaims)
	: AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
	public const string SchemeName = "AccountSession";

	protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
	{
		var token = ExtractToken();
		if (string.IsNullOrWhiteSpace(token))
			return AuthenticateResult.NoResult();

		var session = await sessionStore.ValidateAsync(token);
		if (session is null)
			return AuthenticateResult.Fail("Invalid or expired account session.");

		var accountId = session.Value.AccountId;

		var account = await accountService.GetByIdAsync(accountId);
		if (account is null || !account.IsActive)
			return AuthenticateResult.Fail("Account not found or not active.");

		var role = await accountClaims.ComputeAccountRoleAsync(accountId);
		var scopes = await accountClaims.ComputeGrantedScopesAsync(accountId, role);

		var claims = new List<Claim>
		{
			new(ClaimTypes.NameIdentifier, accountId),
			new(ClaimTypes.Name, account.Username),
			new(ClaimTypes.Role, role.ToString()),
		};
		claims.AddRange(scopes.Select(s => new Claim(PortalPermission.ClaimType, s)));

		var characters = await accountService.GetCharactersAsync(accountId);
		var acting = ResolveActingCharacter(session.Value, characters);
		if (acting is not null)
		{
			claims.Add(new Claim(GameHub.CharacterDbrefClaim, acting.Object.DBRef.ToString()));
			claims.Add(new Claim("character_key", acting.Object.Key.ToString()));
			claims.Add(new Claim("character_creation_time", acting.Object.CreationTime.ToString()));
			claims.Add(new Claim("character_name", acting.Object.Name));
		}

		var identity = new ClaimsIdentity(claims, SchemeName);
		return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
	}

	/// <summary>
	/// The character this request acts as, taken from the session the token names. The binding is
	/// established when the token is minted (login binds the primary; switch-character mints a new
	/// token bound to the target), so nothing the client sends participates in the decision — there is
	/// no header or query hint to spoof, and no silent fallback to the primary when one doesn't match.
	/// </summary>
	/// <remarks>
	/// Membership is still re-checked against the live roster on every request: a character unlinked
	/// from the account after the token was minted must stop being actable immediately, without
	/// waiting for the session to expire.
	/// </remarks>
	private static SharpPlayer? ResolveActingCharacter(
		IAccountSessionStore.SessionIdentity session, IReadOnlyList<SharpPlayer> characters)
	{
		if (session.CharacterKey is not { } key || session.CharacterCreationTime is not { } created)
			return null;

		return characters.FirstOrDefault(c => c.Object.Key == key && c.Object.CreationTime == created);
	}

	private string? ExtractToken()
	{
		var header = Request.Headers.Authorization.FirstOrDefault();
		if (header is not null && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
			return header["Bearer ".Length..].Trim();
		// SignalR WebSocket/SSE transports pass the token as a query parameter.
		return Request.Query["access_token"].FirstOrDefault();
	}
}
