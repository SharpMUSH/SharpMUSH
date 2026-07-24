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

		var accountId = await sessionStore.ValidateAsync(token);
		if (accountId is null)
			return AuthenticateResult.Fail("Invalid or expired account session.");

		var account = await accountService.GetByIdAsync(accountId);
		if (account is null || account.IsDisabled)
			return AuthenticateResult.Fail("Account not found or disabled.");

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
		var acting = ResolveActingCharacter(characters);
		if (acting is not null)
		{
			claims.Add(new Claim(GameHub.CharacterDbrefClaim, $"#{acting.Object.Key}"));
			claims.Add(new Claim("character_key", acting.Object.Key.ToString()));
			claims.Add(new Claim("character_creation_time", acting.Object.CreationTime.ToString()));
			claims.Add(new Claim("character_name", acting.Object.Name));
		}

		var identity = new ClaimsIdentity(claims, SchemeName);
		return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
	}

	/// <summary>
	/// The character this request acts as: the client-supplied hint (<c>X-Acting-Character</c> header
	/// for REST, <c>character</c> query for the SignalR connection) when it names a character the account
	/// owns, otherwise the primary. Only owned characters are honoured — an unknown or unowned hint
	/// silently falls back to the primary.
	/// </summary>
	private SharpPlayer? ResolveActingCharacter(IReadOnlyList<SharpPlayer> characters)
	{
		var hint = Request.Headers["X-Acting-Character"].FirstOrDefault()
			?? Request.Query["character"].FirstOrDefault();
		if (!string.IsNullOrWhiteSpace(hint))
		{
			var key = hint.TrimStart('#');
			var match = characters.FirstOrDefault(c => c.Object.Key.ToString() == key);
			if (match is not null)
				return match;
		}
		return characters.FirstOrDefault();
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
