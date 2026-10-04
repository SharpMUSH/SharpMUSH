using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Hubs;

namespace SharpMUSH.Server.Authentication;

/// <summary>
/// Authenticates REST and SignalR requests bearing an account-session token, resolving
/// the role claim server-side (permission scopes come from <see cref="FreshPermissionClaimsTransformation"/>) (so bans/role changes take effect on the next request)
/// and emitting the <see cref="GameHub.CharacterDbrefClaim"/> the hub authorizes on.
/// </summary>
/// <remarks>
/// <para>Nothing the client sends participates in choosing the acting character — there is no header or
/// query hint to spoof. The choice is <see cref="ActingCharacterResolver"/>'s alone, made from the
/// session the token names plus the account's live character roster.</para>
///
/// <para>Sitelock is re-checked here against the address the request is actually coming from, not the
/// one the session was created at. A session records one origin IP; sitelock rules are patterns
/// evaluated against the current request, and a session that changes address otherwise walks straight
/// through a ban on the address it is being used from. Revoking at ban time (see
/// <c>BanEnforcementService.EnforceHostRuleAsync</c>) cannot cover that case, because the address in
/// question did not exist anywhere when the ban landed.</para>
///
/// <para>The check runs before the session store is read, so a banned address costs strictly less than
/// it did — the sitelock decision is a scan of the in-memory rule set behind an
/// <c>IOptionsMonitor</c> snapshot, with no I/O of any kind. That is deliberately unlike the
/// per-request database write PR #754 removed from this path: the concern there was a write per
/// request contending on one document, and nothing here writes or reads anything.</para>
/// </remarks>
public class AccountSessionAuthenticationHandler(
	IOptionsMonitor<AuthenticationSchemeOptions> options,
	ILoggerFactory logger,
	UrlEncoder encoder,
	IAccountSessionStore sessionStore,
	IAccountService accountService,
	AccountClaimsService accountClaims,
	SitelockGuard sitelockGuard)
	: AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
	public const string SchemeName = "AccountSession";

	/// <summary>Present (value <c>true</c>) when the account must change its password before anything else.</summary>
	public const string MustChangePasswordClaim = "must_change_password";

	/// <summary>
	/// The account this scheme authenticated <paramref name="user"/> as: the session was validated, the
	/// account found and active, on this request. False for any other scheme (DebugAuth in Development),
	/// whose callers fall back to validating the bearer themselves.
	/// </summary>
	public static bool TryGetAccount(ClaimsPrincipal user, out string accountId, out bool mustChangePassword)
	{
		accountId = string.Empty;
		mustChangePassword = false;
		if (user.Identity is not { IsAuthenticated: true, AuthenticationType: SchemeName }
			|| user.FindFirstValue(ClaimTypes.NameIdentifier) is not { Length: > 0 } id) return false;
		accountId = id;
		mustChangePassword = user.HasClaim(MustChangePasswordClaim, "true");
		return true;
	}

	/// <summary>The acting character this scheme resolved for <paramref name="user"/>, as its dbref claim.</summary>
	public static DBRef? ActingCharacter(ClaimsPrincipal user)
		=> DBRef.TryParse(user.FindFirstValue(GameHub.CharacterDbrefClaim) ?? string.Empty, out var dbref) ? dbref : null;

	/// <summary>
	/// Set when this request was refused for sitelock rather than for a bad credential, so the
	/// challenge answers 403 instead of 401. Handler instances are per-request, so this holds no state
	/// across requests.
	/// </summary>
	private bool _sitelocked;

	protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
	{
		var token = ExtractToken();
		if (string.IsNullOrWhiteSpace(token))
			return AuthenticateResult.NoResult();

		// Same "unknown" fallback the session-minting surfaces use (AuthController.ClientIp), so a rule
		// written against one is understood by the other.
		var clientIp = Context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
		if (sitelockGuard.IsBlocked(clientIp, host: string.Empty, SitelockGuard.Connect))
		{
			_sitelocked = true;
			return AuthenticateResult.Fail("Sitelocked address.");
		}

		var session = await sessionStore.ValidateAsync(token);
		if (session is null)
			return AuthenticateResult.Fail("Invalid or expired account session.");

		var accountId = session.Value.AccountId;

		var account = await accountService.GetByIdAsync(accountId);
		if (account is null || !account.IsActive)
			return AuthenticateResult.Fail("Account not found or not active.");

		// Permission-scope claims are not added here: FreshPermissionClaimsTransformation replaces them
		// from the account's current authority after every successful authenticate.
		var role = await accountClaims.ComputeAccountRoleAsync(accountId);

		var claims = new List<Claim>
		{
			new(ClaimTypes.NameIdentifier, accountId),
			new(ClaimTypes.Name, account.Username),
			new(ClaimTypes.Role, role.ToString()),
		};
		if (account.MustChangePassword) claims.Add(new Claim(MustChangePasswordClaim, "true"));

		var characters = await accountService.GetCharactersAsync(accountId);
		var acting = ActingCharacterResolver.Resolve(session.Value, characters);
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
	/// A banned address is not a bad credential. Without this the sitelock refusal above would reach the
	/// client as 401, telling a browser its session had expired and sending it round the login loop,
	/// and it would silently replace the 403 the sitelocked auth surfaces have always answered with
	/// (<c>AuthController.IsSitelocked</c>, pinned by <c>SitelockCheckTests</c> and
	/// <c>SwitchCharacterTests</c>) on every <c>[Authorize]</c>-gated endpoint.
	/// </summary>
	protected override Task HandleChallengeAsync(AuthenticationProperties properties)
	{
		if (!_sitelocked)
		{
			return base.HandleChallengeAsync(properties);
		}

		Response.StatusCode = StatusCodes.Status403Forbidden;
		return Task.CompletedTask;
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
