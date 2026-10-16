using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.OAuth;

/// <summary>
/// An app a player can be asked about: a registered <see cref="OAuthClient"/>, or a hosted UI package whose
/// <c>ui.json</c> asks for scopes, which the portal hosts and which has no address of its own.
/// </summary>
/// <param name="ClientId">The <c>client_id</c>: a registered app's id, or <c>package:&lt;id&gt;</c>.</param>
/// <param name="Name">What the player is shown.</param>
/// <param name="Description">What the app says it does, or null.</param>
/// <param name="Homepage">Where it lives, or null.</param>
/// <param name="RedirectUris">Where a code may be sent; none for a hosted package.</param>
/// <param name="Scopes">The most it may ask for.</param>
/// <param name="SecretHash">The SHA-256 of its secret when it has one.</param>
/// <param name="Hosted">A hosted UI package: it is asked for in the portal and gets its token from there.</param>
public sealed record OAuthApp(
	string ClientId,
	string Name,
	string? Description,
	string? Homepage,
	IReadOnlyList<string> RedirectUris,
	IReadOnlyList<string> Scopes,
	string? SecretHash,
	bool Hosted)
{
	/// <summary>The prefix of a hosted package's client id.</summary>
	public const string PackagePrefix = "package:";

	public static OAuthApp Of(OAuthClient client) => new(client.Id, client.Name, client.Description, client.Homepage,
		client.RedirectUris, client.Scopes, client.SecretHash, Hosted: false);
}

/// <summary>What the consent page asks the player.</summary>
/// <param name="App">Who is asking.</param>
/// <param name="Scopes">What it asks for, <see cref="DelegatedScopes.Profile"/> always among them.</param>
/// <param name="AlreadyAllowed">The character already allowed all of it.</param>
public sealed record AuthorizationPrompt(OAuthApp App, IReadOnlyList<string> Scopes, bool AlreadyAllowed);

/// <summary>
/// A request too broken to answer at its <c>redirect_uri</c> (an unknown app, or an address it did not register):
/// shown to the player, never redirected, so a forged link cannot bounce them anywhere.
/// </summary>
public readonly record struct AuthorizationRefused(string Message);

/// <summary>Where the player's browser goes next: back to the app, with a code or an error.</summary>
public readonly record struct AuthorizationRedirect(string Location);

/// <summary>What an authorization request comes to before the player answers it.</summary>
public union AuthorizationCheck(AuthorizationPrompt, AuthorizationRedirect, AuthorizationRefused);

/// <summary>What the player's answer comes to.</summary>
public union AuthorizationDecision(AuthorizationRedirect, AuthorizationRefused);

/// <summary>A token response (RFC 6749 §5.1). The tokens are shown here once and stored only as hashes.</summary>
/// <param name="AccessToken">The bearer for the API.</param>
/// <param name="ExpiresIn">Seconds until <paramref name="AccessToken"/> stops working.</param>
/// <param name="RefreshToken">Traded once for the next pair, or null for a hosted package's token.</param>
/// <param name="Scopes">What the access token carries.</param>
public sealed record IssuedTokens(string AccessToken, int ExpiresIn, string? RefreshToken, IReadOnlyList<string> Scopes);

/// <summary>An error response from the token endpoint (RFC 6749 §5.2).</summary>
/// <param name="Code">The <c>error</c> value, such as <c>invalid_grant</c>.</param>
/// <param name="Description">The <c>error_description</c>.</param>
public readonly record struct TokenError(string Code, string Description)
{
	public const string InvalidRequest = "invalid_request";
	public const string InvalidClient = "invalid_client";
	public const string InvalidGrant = "invalid_grant";
	public const string InvalidScope = "invalid_scope";
	public const string UnsupportedGrantType = "unsupported_grant_type";
}

/// <summary>What a token request comes to.</summary>
public union TokenOutcome(IssuedTokens, TokenError);

/// <summary>The character has not allowed the hosted package what it asks for; the portal asks the player.</summary>
public readonly record struct ConsentRequired(OAuthApp App, IReadOnlyList<string> Scopes);

/// <summary>What asking for a hosted package's token comes to: the token, a question for the player, or no such package.</summary>
public union HostedTokenOutcome(IssuedTokens, ConsentRequired, NotFound);

/// <summary>What staff enter to register or change an app.</summary>
public sealed record OAuthClientDraft(
	string Name,
	string? Description,
	string? Homepage,
	IReadOnlyList<string> RedirectUris,
	IReadOnlyList<string> Scopes);

/// <summary>A newly registered app, and its secret, shown this once, when it has one.</summary>
public sealed record RegisteredOAuthClient(OAuthClient Client, string? Secret);

/// <summary>How the caller of the token or revocation endpoint says which app it is (RFC 6749 §2.3.1).</summary>
public readonly record struct ClientCredentials(string? ClientId, string? Secret);

/// <summary>What a valid access token lets its bearer do.</summary>
/// <param name="Token">The stored token.</param>
/// <param name="AppName">The app's name, for logs and the audit trail.</param>
public sealed record DelegatedAccess(OAuthToken Token, string AppName);
