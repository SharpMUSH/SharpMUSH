using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.OAuth;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Hubs;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// The game's OAuth 2 authorization server, for apps that act as a player's character.
///
/// Routes:
///   GET  /.well-known/oauth-authorization-server — the server's metadata (RFC 8414)
///   GET  api/oauth/authorize       — what the consent page (<c>/oauth/authorize</c>) asks the signed-in player
///   POST api/oauth/authorize       — the player's answer, and where the browser goes next
///   GET  api/oauth/scopes          — every scope an app may ask for, a custom permission category's among them
///   POST api/oauth/token           — trades a code or refresh token for tokens (RFC 6749 §4.1.3, §6)
///   POST api/oauth/revoke          — forgets a token (RFC 7009)
///   GET  api/oauth/me              — who an app's token acts as
///   GET  api/oauth/grants          — the apps the account's characters allowed
///   DELETE api/oauth/grants/{id}   — takes one back
/// </summary>
/// <remarks>
/// The consent and grant routes take the player's own account session, never an app's token: an app cannot approve
/// itself or see what else the player allowed. The token and revocation routes take no session at all; the app proves
/// itself with its client id, its secret when it has one, and PKCE.
/// </remarks>
[ApiController]
public sealed class OAuthController(IOAuthService oauth, BearerAccountResolver bearer, IAccountService accounts) : ControllerBase
{
	/// <summary>The portal page an authorization request opens.</summary>
	public const string ConsentPage = "/oauth/authorize";

	[AllowAnonymous]
	[HttpGet("/.well-known/oauth-authorization-server")]
	public async Task<IActionResult> Metadata(CancellationToken ct)
	{
		var issuer = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
		string[] clientAuthentication = ["none", "client_secret_basic", "client_secret_post"];
		return Ok(new Dictionary<string, object>
		{
			["issuer"] = issuer,
			["authorization_endpoint"] = issuer + ConsentPage,
			["token_endpoint"] = issuer + "/api/oauth/token",
			["revocation_endpoint"] = issuer + "/api/oauth/revoke",
			["scopes_supported"] = (await oauth.GetScopesAsync(ct)).Select(scope => scope.Scope).ToArray(),
			["response_types_supported"] = new[] { "code" },
			["grant_types_supported"] = new[] { "authorization_code", "refresh_token" },
			["code_challenge_methods_supported"] = new[] { "S256" },
			["token_endpoint_auth_methods_supported"] = clientAuthentication,
			["revocation_endpoint_auth_methods_supported"] = clientAuthentication,
		});
	}

	[HttpGet("api/oauth/authorize")]
	public async Task<IActionResult> Check([FromQuery(Name = "response_type")] string? responseType,
		[FromQuery(Name = "client_id")] string? clientId, [FromQuery(Name = "redirect_uri")] string? redirectUri,
		[FromQuery] string? scope, [FromQuery] string? state,
		[FromQuery(Name = "code_challenge")] string? codeChallenge,
		[FromQuery(Name = "code_challenge_method")] string? codeChallengeMethod, CancellationToken ct)
	{
		var request = new OAuthAuthorizeRequest(responseType, clientId, redirectUri, scope, state, codeChallenge, codeChallengeMethod);
		return await bearer.ResolveCharacterAsync(this) switch
		{
			SignedInCharacter signedIn => Ok(await oauth.CheckAuthorizationAsync(request, signedIn.AccountId, signedIn.Character.Object.DBRef, ct) switch
			{
				AuthorizationPrompt prompt => new OAuthAuthorizeView(View(prompt.App), prompt.Scopes, prompt.AlreadyAllowed,
					signedIn.Character.Object.Name, null, null),
				AuthorizationRedirect redirect => new OAuthAuthorizeView(null, [], false, null, redirect.Location, null),
				AuthorizationRefused refused => new OAuthAuthorizeView(null, [], false, null, null, refused.Message),
			}),
			ActionResult refused => refused,
		};
	}

	[HttpPost("api/oauth/authorize")]
	public async Task<IActionResult> Decide([FromBody] OAuthDecisionRequest decision, CancellationToken ct)
		=> await bearer.ResolveCharacterAsync(this) switch
		{
			SignedInCharacter signedIn => Ok(await oauth.DecideAsync(decision.Request, signedIn.AccountId,
				signedIn.Character.Object.DBRef, decision.Approve, ct) switch
			{
				AuthorizationRedirect redirect => new OAuthDecisionView(redirect.Location, null),
				AuthorizationRefused refused => new OAuthDecisionView(null, refused.Message),
			}),
			ActionResult refused => refused,
		};

	[Authorize]
	[HttpGet("api/oauth/scopes")]
	public async Task<IReadOnlyList<OAuthScopeView>> Scopes(CancellationToken ct) => await oauth.GetScopesAsync(ct);

	[AllowAnonymous]
	[HttpPost("api/oauth/token")]
	[EnableRateLimiting("public-api")]
	public async Task<IActionResult> Token(CancellationToken ct)
	{
		NoStore();
		if (!Request.HasFormContentType)
			return TokenProblem(new TokenError(TokenError.InvalidRequest, "Send the request as application/x-www-form-urlencoded."));
		var form = await Request.ReadFormAsync(ct);
		if (ClientOf(form) is not { } client)
			return TokenProblem(new TokenError(TokenError.InvalidRequest, "Send the client's credentials one way: Basic or in the form."));

		var values = form.ToDictionary(field => field.Key, field => field.Value.ToString(), StringComparer.Ordinal);
		return await oauth.TokenAsync(values, client, ct) switch
		{
			IssuedTokens tokens => Ok(new Dictionary<string, object?>
			{
				["access_token"] = tokens.AccessToken,
				["token_type"] = "Bearer",
				["expires_in"] = tokens.ExpiresIn,
				["refresh_token"] = tokens.RefreshToken,
				["scope"] = DelegatedScopes.Format(tokens.Scopes),
			}),
			TokenError error => TokenProblem(error),
		};
	}

	[AllowAnonymous]
	[HttpPost("api/oauth/revoke")]
	[EnableRateLimiting("public-api")]
	public async Task<IActionResult> Revoke(CancellationToken ct)
	{
		NoStore();
		if (!Request.HasFormContentType)
			return TokenProblem(new TokenError(TokenError.InvalidRequest, "Send the request as application/x-www-form-urlencoded."));
		var form = await Request.ReadFormAsync(ct);
		if (ClientOf(form) is not { } client)
			return TokenProblem(new TokenError(TokenError.InvalidRequest, "Send the client's credentials one way: Basic or in the form."));
		if (form["token"].ToString() is not { Length: > 0 } token)
			return TokenProblem(new TokenError(TokenError.InvalidRequest, "token is required."));

		// RFC 7009 §2.2: an unknown or already revoked token is answered the same as a revoked one.
		await oauth.RevokeTokenAsync(token, client, ct);
		return Ok();
	}

	[HttpGet("api/oauth/me")]
	[Authorize(Policy = DelegatedPolicies.Profile)]
	public IActionResult Me()
	{
		if (OAuthBearerAuthenticationHandler.App(User) is not { } app)
			return Problem("Only an app's access token says who it acts as.", statusCode: StatusCodes.Status400BadRequest);
		return Ok(new OAuthMeView(
			User.FindFirst(GameHub.CharacterDbrefClaim)?.Value ?? string.Empty,
			User.FindFirst("character_name")?.Value ?? string.Empty,
			app.ClientId,
			[.. User.FindAll(OAuthBearerAuthenticationHandler.ScopeClaim).Select(claim => claim.Value)]));
	}

	[HttpGet("api/oauth/grants")]
	public async Task<IActionResult> Grants(CancellationToken ct)
		=> await bearer.ResolveAsync(this) switch
		{
			SignedInAccount account => Ok(await GrantViewsAsync(account.Id, ct)),
			ActionResult refused => refused,
		};

	[HttpDelete("api/oauth/grants/{id}")]
	public async Task<IActionResult> RevokeGrant(string id, CancellationToken ct)
		=> await bearer.ResolveAsync(this) switch
		{
			SignedInAccount account => await oauth.RevokeGrantAsync(id, account.Id, ct) ? NoContent() : NotFound(),
			ActionResult refused => refused,
		};

	private async Task<IReadOnlyList<OAuthGrantView>> GrantViewsAsync(string accountId, CancellationToken ct)
	{
		var characters = await accounts.GetCharactersAsync(accountId);
		return [.. (await oauth.GetGrantsAsync(accountId, ct)).Select(grant => new OAuthGrantView(
			grant.Id, grant.ClientId, grant.ClientName, grant.Character.ToString(),
			characters.FirstOrDefault(character => character.Object.DBRef.Matches(grant.Character))?.Object.Name
				?? $"#{grant.Character.Number}",
			grant.Scopes, grant.CreatedAt, grant.LastUsedAt))];
	}

	public static OAuthAppView View(OAuthApp app) => new(app.ClientId, app.Name, app.Description, app.Homepage, app.Hosted);

	/// <summary>
	/// The client's credentials (RFC 6749 §2.3.1): HTTP Basic, or <c>client_id</c> and <c>client_secret</c> in the form,
	/// never both; null when both are used or the Basic header is malformed.
	/// </summary>
	private ClientCredentials? ClientOf(IFormCollection form)
	{
		var formId = form["client_id"].ToString() is { Length: > 0 } id ? id : null;
		var formSecret = form["client_secret"].ToString() is { Length: > 0 } secret ? secret : null;
		if (Request.Headers.Authorization.FirstOrDefault() is not { } header
				|| !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
			return new ClientCredentials(formId, formSecret);
		if (formSecret is not null) return null;

		try
		{
			var pair = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..].Trim()));
			var colon = pair.IndexOf(':');
			if (colon < 0) return null;
			var basicId = Uri.UnescapeDataString(pair[..colon].Replace('+', ' '));
			if (formId is not null && formId != basicId) return null;
			return new ClientCredentials(basicId, Uri.UnescapeDataString(pair[(colon + 1)..].Replace('+', ' ')));
		}
		catch (FormatException)
		{
			return null;
		}
	}

	private ObjectResult TokenProblem(TokenError error)
	{
		var status = error.Code == TokenError.InvalidClient ? StatusCodes.Status401Unauthorized : StatusCodes.Status400BadRequest;
		if (status == StatusCodes.Status401Unauthorized) Response.Headers.WWWAuthenticate = "Basic realm=\"oauth\"";
		return StatusCode(status, new Dictionary<string, string> { ["error"] = error.Code, ["error_description"] = error.Description });
	}

	private void NoStore()
	{
		Response.Headers.CacheControl = "no-store";
		Response.Headers.Pragma = "no-cache";
	}
}
