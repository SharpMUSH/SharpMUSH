namespace SharpMUSH.Library.Models.Portal;

/// <summary>An authorization request's query, as the consent page read it from its address (RFC 6749 §4.1.1).</summary>
public sealed record OAuthAuthorizeRequest(
	string? ResponseType,
	string? ClientId,
	string? RedirectUri,
	string? Scope,
	string? State,
	string? CodeChallenge,
	string? CodeChallengeMethod);

/// <summary>The player's answer on the consent page (<c>POST api/oauth/authorize</c>).</summary>
public sealed record OAuthDecisionRequest(OAuthAuthorizeRequest Request, bool Approve);

/// <summary>An app, as the consent page and the app lists show it.</summary>
/// <param name="Hosted">A UI package the portal hosts, rather than an app elsewhere.</param>
public sealed record OAuthAppView(string ClientId, string Name, string? Description, string? Homepage, bool Hosted)
{
	/// <summary>An app that described itself at its client id address, which staff have not allowed.</summary>
	public bool Unreviewed { get; init; }

	/// <summary>The host a self-described app's details were read from, or null for an app staff registered.</summary>
	public string? DescribedAt { get; init; }
}

/// <summary>
/// What the consent page does with a request (<c>GET api/oauth/authorize</c>): ask the player (<see cref="App"/> set),
/// send the browser back to the app (<see cref="Location"/> set), or show why it cannot (<see cref="Message"/> set).
/// </summary>
/// <param name="Scopes">What the app asks for.</param>
/// <param name="AlreadyAllowed">The character already allowed all of it.</param>
/// <param name="Character">The character the app would act as.</param>
public sealed record OAuthAuthorizeView(
	OAuthAppView? App,
	IReadOnlyList<string> Scopes,
	bool AlreadyAllowed,
	string? Character,
	string? Location,
	string? Message);

/// <summary>Where the consent page sends the browser after the player answered, or why it cannot.</summary>
public sealed record OAuthDecisionView(string? Location, string? Message);

/// <summary>A character's yes to an app, as the account page lists it.</summary>
/// <param name="Character">The character's objid.</param>
/// <param name="CharacterName">The character's name now.</param>
public sealed record OAuthGrantView(
	string Id,
	string ClientId,
	string AppName,
	string Character,
	string CharacterName,
	IReadOnlyList<string> Scopes,
	DateTimeOffset CreatedAt,
	DateTimeOffset? LastUsedAt);

/// <summary>A registered app, as staff see it. Its secret is never shown again after it is made.</summary>
/// <param name="Confidential">It has a secret it must send to the token endpoint.</param>
public sealed record OAuthClientView(
	string Id,
	string Name,
	string? Description,
	string? Homepage,
	IReadOnlyList<string> RedirectUris,
	IReadOnlyList<string> Scopes,
	bool Confidential,
	DateTimeOffset CreatedAt)
{
	/// <summary>allowed, unreviewed or blocked. Only a self-described app is ever unreviewed or blocked.</summary>
	public string Status { get; init; } = OAuthClientStatuses.Allowed;

	/// <summary>The host a self-described app's details were read from, or null for an app staff registered.</summary>
	public string? DescribedAt { get; init; }

	/// <summary>When a self-described app's details were last read.</summary>
	public DateTimeOffset? MetadataFetchedAt { get; init; }
}

/// <summary>The values of <see cref="OAuthClientView.Status"/> and <see cref="OAuthClientReview.Status"/>.</summary>
public static class OAuthClientStatuses
{
	public const string Allowed = "allowed";
	public const string Unreviewed = "unreviewed";
	public const string Blocked = "blocked";

	/// <summary>Only for a review: remove the app, so its details are read afresh the next time a player is sent its way.</summary>
	public const string Forget = "forget";
}

/// <summary>Staff's decision about a self-described app (<c>POST api/oauth/clients/review</c>).</summary>
/// <param name="ClientId">The app's https client id.</param>
/// <param name="Status">allowed, unreviewed, blocked or forget.</param>
public sealed record OAuthClientReview(string ClientId, string Status);

/// <summary>
/// How the game treats apps that describe themselves at an https client id (<c>api/oauth/policy</c>).
/// </summary>
/// <param name="Policy">registered (refused), review (staff allow each first) or open (accepted, marked not reviewed).</param>
/// <param name="BlockedDomains">Domains whose self-described apps are refused, each with its subdomains.</param>
public sealed record OAuthAppPolicyView(string Policy, IReadOnlyList<string> BlockedDomains);

/// <summary>What staff enter to register or change an app. <see cref="Confidential"/> counts only when registering.</summary>
public sealed record OAuthClientEdit(
	string Name,
	string? Description,
	string? Homepage,
	IReadOnlyList<string> RedirectUris,
	IReadOnlyList<string> Scopes,
	bool Confidential = false);

/// <summary>A new app, or a new secret, with the secret shown this once.</summary>
public sealed record OAuthClientCreated(OAuthClientView Client, string? Secret);

/// <summary>Who an app's token acts as (<c>GET api/oauth/me</c>).</summary>
/// <param name="Character">The character's objid.</param>
/// <param name="Name">The character's name.</param>
/// <param name="App">The app's client id.</param>
public sealed record OAuthMeView(string Character, string Name, string App, IReadOnlyList<string> Scopes);

/// <summary>A hosted package's access token, kept by the portal, never given to the package's frame.</summary>
public sealed record OAuthHostedToken(string AccessToken, int ExpiresIn, IReadOnlyList<string> Scopes);

/// <summary>A hosted package has not been allowed what it asks for: the portal asks the player.</summary>
public sealed record OAuthHostedConsent(OAuthAppView App, IReadOnlyList<string> Scopes, string Character);

/// <summary>A scope an app may ask for (<c>GET api/oauth/scopes</c>).</summary>
/// <param name="Category">The custom permission category it stands for, or null for a built-in scope.</param>
/// <param name="Description">The category's description, or null for a built-in scope, which the portal describes itself.</param>
public sealed record OAuthScopeView(string Scope, string? Category, string? Description);
