namespace SharpMUSH.Library.Models;

/// <summary>
/// An app staff registered so it may ask players for access to their characters (<c>api/oauth</c>).
/// </summary>
/// <param name="Id">The <c>client_id</c> the app sends.</param>
/// <param name="Name">What the consent page and the app lists call it.</param>
/// <param name="Description">One or two sentences the consent page shows under the name, or null.</param>
/// <param name="Homepage">Where the app lives, shown as a link on the consent page, or null.</param>
/// <param name="RedirectUris">The only addresses an authorization may send its code to, compared exactly.</param>
/// <param name="Scopes">The most the app may ask for (<see cref="SharpMUSH.Library.OAuth.DelegatedScopes"/>).</param>
/// <param name="SecretHash">The SHA-256 of the app's secret, hex, when it has one; a public app (a browser or
/// desktop app, which cannot keep one) has none and proves itself with PKCE alone.</param>
/// <param name="CreatedAt">When it was registered.</param>
public sealed record OAuthClient(
	string Id,
	string Name,
	string? Description,
	string? Homepage,
	IReadOnlyList<string> RedirectUris,
	IReadOnlyList<string> Scopes,
	string? SecretHash,
	DateTimeOffset CreatedAt);

/// <summary>
/// A player's yes to an app: the app may act as <paramref name="Character"/> within <paramref name="Scopes"/> until the
/// player, or staff, takes it back. There is at most one per character and app; asking again for more widens it.
/// </summary>
/// <param name="Id">The grant's name in the API.</param>
/// <param name="AccountId">The account that owns <paramref name="Character"/> and approved it.</param>
/// <param name="Character">The character the app acts as, an objid.</param>
/// <param name="ClientId">The app: a registered <see cref="OAuthClient"/>'s id, or <c>package:&lt;id&gt;</c> for a hosted UI package.</param>
/// <param name="ClientName">The app's name when the grant was made, kept so a list still reads after the app is gone.</param>
/// <param name="Scopes">What the player allowed.</param>
/// <param name="CreatedAt">When the player first allowed it.</param>
/// <param name="LastUsedAt">When the app last used it, to the minute, or null.</param>
public sealed record OAuthGrant(
	string Id,
	string AccountId,
	DBRef Character,
	string ClientId,
	string ClientName,
	IReadOnlyList<string> Scopes,
	DateTimeOffset CreatedAt,
	DateTimeOffset? LastUsedAt);

/// <summary>What an <see cref="OAuthToken"/> is good for.</summary>
public enum OAuthTokenKind
{
	/// <summary>An authorization code: traded once, within a minute, for the other two.</summary>
	Code,

	/// <summary>A bearer for the API, for an hour.</summary>
	Access,

	/// <summary>Traded once for a new access token and a new refresh token.</summary>
	Refresh
}

/// <summary>
/// One issued code or token. Only its SHA-256 is stored; the token itself is shown once, to the app.
/// </summary>
/// <param name="Hash">The SHA-256 of the token.</param>
/// <param name="Kind">What it is good for.</param>
/// <param name="GrantId">The grant it was issued under; revoking the grant deletes it.</param>
/// <param name="ClientId">The app it was issued to.</param>
/// <param name="AccountId">The account behind the grant.</param>
/// <param name="Character">The character it acts as.</param>
/// <param name="Scopes">What it carries: at most the grant's.</param>
/// <param name="ExpiresAt">When it stops working.</param>
/// <param name="RedirectUri">A code's: the <c>redirect_uri</c> it was sent to, which the exchange must repeat.</param>
/// <param name="CodeChallenge">A code's: the PKCE <c>code_challenge</c> the exchange's <c>code_verifier</c> must match.</param>
public sealed record OAuthToken(
	byte[] Hash,
	OAuthTokenKind Kind,
	string GrantId,
	string ClientId,
	string AccountId,
	DBRef Character,
	IReadOnlyList<string> Scopes,
	DateTimeOffset ExpiresAt,
	string? RedirectUri = null,
	string? CodeChallenge = null);
