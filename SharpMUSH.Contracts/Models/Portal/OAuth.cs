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
public sealed record OAuthAppView(string ClientId, string Name, string? Description, string? Homepage, bool Hosted);

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
	DateTimeOffset CreatedAt);

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
