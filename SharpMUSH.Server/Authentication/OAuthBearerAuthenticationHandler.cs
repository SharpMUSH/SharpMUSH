using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.OAuth;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Hubs;

namespace SharpMUSH.Server.Authentication;

/// <summary>
/// Authenticates a request carrying an app's access token (<c>Authorization: Bearer smat_…</c>) as the character the
/// player allowed the app to act as. Only endpoints that name a <see cref="DelegatedPolicies"/> policy accept it; every
/// other endpoint authenticates with the default scheme alone, which leaves app tokens alone, so an app never reaches
/// the account's own settings, sessions or grants.
/// </summary>
/// <remarks>
/// The account, its activity and its link to the character are read again on every request, as the account-session
/// scheme reads them, so unlinking the character or disabling the account stops the app at once. Sitelock is checked
/// against the address the request comes from.
/// </remarks>
public sealed class OAuthBearerAuthenticationHandler(
	IOptionsMonitor<AuthenticationSchemeOptions> options,
	ILoggerFactory logger,
	UrlEncoder encoder,
	IOAuthService oauth,
	IAccountService accountService,
	SitelockGuard sitelockGuard)
	: AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
	public const string SchemeName = "OAuth";

	/// <summary>The app's client id.</summary>
	public const string ClientClaim = "oauth_client";

	/// <summary>The app's name.</summary>
	public const string AppNameClaim = "oauth_app";

	/// <summary>One per scope the token carries.</summary>
	public const string ScopeClaim = "oauth_scope";

	private bool _sitelocked;

	/// <summary>The app a request authenticated by this scheme acts for, or null for any other request.</summary>
	public static DelegatedApp? App(ClaimsPrincipal user)
		=> user.Identities.FirstOrDefault(identity => identity is { IsAuthenticated: true, AuthenticationType: SchemeName }) is { } identity
			&& identity.FindFirst(ClientClaim)?.Value is { } client
			&& DBRef.TryParse(identity.FindFirst(GameHub.CharacterDbrefClaim)?.Value ?? string.Empty, out var character)
			&& character is { } acting
				? new DelegatedApp(client, identity.FindFirst(AppNameClaim)?.Value ?? client, acting,
					[.. identity.FindAll(ScopeClaim).Select(claim => claim.Value)])
				: null;

	/// <summary>The bearer token of <paramref name="request"/>, if it is an app's access token.</summary>
	public static string? AppToken(HttpRequest request)
		=> request.Headers.Authorization.FirstOrDefault() is { } header
			&& header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
			&& header["Bearer ".Length..].Trim() is var token
			&& OAuthService.IsAccessToken(token)
				? token
				: null;

	protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
	{
		if (AppToken(Request) is not { } token) return AuthenticateResult.NoResult();

		var clientIp = Context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
		if (sitelockGuard.IsBlocked(clientIp, host: string.Empty, SitelockGuard.Connect))
		{
			_sitelocked = true;
			return AuthenticateResult.Fail("Sitelocked address.");
		}

		if (await oauth.ValidateAccessTokenAsync(token, Context.RequestAborted) is not DelegatedAccess access)
			return AuthenticateResult.Fail("Invalid, expired or revoked app token.");

		var stored = access.Token;
		var account = await accountService.GetByIdAsync(stored.AccountId);
		if (account is null || !account.IsActive || account.MustChangePassword)
			return AuthenticateResult.Fail("The account behind this app token is not active.");

		// The character must still be the account's: an unlinked character takes its apps' access with it.
		var characters = await accountService.GetCharactersAsync(stored.AccountId);
		if (characters.FirstOrDefault(c => c.Object.DBRef.Matches(stored.Character)) is not SharpPlayer character)
			return AuthenticateResult.Fail("The character behind this app token is no longer on the account.");

		List<Claim> claims =
		[
			new(ClaimTypes.NameIdentifier, stored.AccountId),
			new(ClaimTypes.Name, account.Username),
			new(GameHub.CharacterDbrefClaim, character.Object.DBRef.ToString()),
			new("character_name", character.Object.Name),
			new(ClientClaim, stored.ClientId),
			new(AppNameClaim, access.AppName),
			.. stored.Scopes.Select(scope => new Claim(ScopeClaim, scope)),
		];

		var identity = new ClaimsIdentity(claims, SchemeName);
		return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
	}

	protected override Task HandleChallengeAsync(AuthenticationProperties properties)
	{
		if (_sitelocked)
		{
			Response.StatusCode = StatusCodes.Status403Forbidden;
			return Task.CompletedTask;
		}

		// RFC 6750 §3: say which scheme, and why, so an app knows to refresh or ask again.
		Response.StatusCode = StatusCodes.Status401Unauthorized;
		Response.Headers.WWWAuthenticate = AppToken(Request) is null
			? "Bearer"
			: "Bearer error=\"invalid_token\", error_description=\"The access token is invalid, expired or revoked.\"";
		return Task.CompletedTask;
	}

	protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
	{
		Response.StatusCode = StatusCodes.Status403Forbidden;
		Response.Headers.WWWAuthenticate = "Bearer error=\"insufficient_scope\"";
		return Task.CompletedTask;
	}
}
