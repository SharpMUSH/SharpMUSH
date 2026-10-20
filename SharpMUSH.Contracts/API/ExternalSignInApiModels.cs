namespace SharpMUSH.Library.API;

/// <summary>
/// The services a sign-in provider can be: one of the ready-made ones, whose addresses the server knows, or
/// <see cref="Custom"/>, any OAuth 2.0 service staff give the addresses of.
/// </summary>
public static class ExternalSignInKinds
{
	public const string Discord = "discord";
	public const string Google = "google";
	public const string GitHub = "github";
	public const string Microsoft = "microsoft";
	public const string Custom = "custom";

	/// <summary>Every kind, ready-made ones first.</summary>
	public static readonly IReadOnlyList<string> All = [Discord, Google, GitHub, Microsoft, Custom];
}

/// <summary>A sign-in provider players can use, as the sign-in page lists it.</summary>
/// <param name="Id">Its id, which its routes and links name.</param>
/// <param name="Name">What the button calls it: "Discord".</param>
/// <param name="Kind">One of <see cref="ExternalSignInKinds"/>, for its icon.</param>
public sealed record ExternalSignInProviderInfo(string Id, string Name, string Kind);

/// <summary>
/// A sign-in provider as staff set it up. The client secret is never sent back: <paramref name="SecretSet"/> says
/// whether one is stored.
/// </summary>
/// <param name="Id">Its id: lower-case letters and digits.</param>
/// <param name="Kind">One of <see cref="ExternalSignInKinds"/>.</param>
/// <param name="Name">What players see it called.</param>
/// <param name="ClientId">The client id the service gave the game.</param>
/// <param name="Enabled">Whether players can sign in with it.</param>
/// <param name="SecretSet">Whether its client secret is stored or overridden.</param>
/// <param name="Ready">Whether it is enabled, with a client id and a secret, so the sign-in page shows it.</param>
/// <param name="RedirectUri">The address to register with the service, as this server is reached from the asking page.</param>
/// <param name="SecretName">The secret its client secret is kept under.</param>
/// <param name="Endpoints">The service's addresses and fields: a ready-made kind's own, or a custom provider's.</param>
public sealed record SignInProviderSettings(
	string Id,
	string Kind,
	string Name,
	string ClientId,
	bool Enabled,
	bool SecretSet,
	bool Ready,
	string RedirectUri,
	string SecretName,
	SignInProviderEndpoints Endpoints);

/// <summary>
/// Where an OAuth 2.0 service signs people in, and which fields of its profile say who they are.
/// </summary>
/// <param name="AuthorizeUrl">The page the player is sent to.</param>
/// <param name="TokenUrl">Where the server trades the returned code for a token.</param>
/// <param name="UserInfoUrl">Where the server reads the player's profile with that token.</param>
/// <param name="Scopes">The scopes asked for, space-separated.</param>
/// <param name="SubjectField">The profile field holding the person's unchanging id.</param>
/// <param name="NameFields">The profile fields tried for a name, space-separated, first one set wins.</param>
/// <param name="EmailField">The profile field holding an email address, if any.</param>
/// <param name="EmailVerifiedField">The profile field that is true when the email is verified; without one, the email is not offered.</param>
public sealed record SignInProviderEndpoints(
	string AuthorizeUrl,
	string TokenUrl,
	string UserInfoUrl,
	string Scopes,
	string SubjectField,
	string NameFields,
	string? EmailField,
	string? EmailVerifiedField);

/// <summary>
/// <c>PUT api/admin/sign-in-providers/{id}</c>: adds or replaces a provider. <paramref name="ClientSecret"/>, when
/// given, replaces the stored one; left out, the stored one stays. <paramref name="Endpoints"/> is read for
/// <see cref="ExternalSignInKinds.Custom"/> only.
/// </summary>
public sealed record SignInProviderUpdate(
	string Kind,
	string? Name,
	string ClientId,
	bool Enabled,
	string? ClientSecret = null,
	SignInProviderEndpoints? Endpoints = null);

/// <summary>A service sign-in linked to the signed-in account.</summary>
/// <param name="Provider">The provider's id.</param>
/// <param name="ProviderName">What the provider is called, or its id when staff have removed it.</param>
/// <param name="Kind">The provider's kind, for its icon; null when it has been removed.</param>
/// <param name="Name">The name the service last gave.</param>
/// <param name="CreatedAt">When it was linked.</param>
/// <param name="LastUsedAt">When it last signed in.</param>
/// <param name="Usable">Whether it can sign in now: its provider is set up and enabled.</param>
public sealed record LinkedSignIn(
	string Provider,
	string ProviderName,
	string? Kind,
	string Name,
	DateTimeOffset CreatedAt,
	DateTimeOffset? LastUsedAt,
	bool Usable);

/// <summary><c>GET api/account/sign-ins</c>: how the account can be signed in to, besides passkeys.</summary>
/// <param name="HasPassword">Whether the account has a password of its own.</param>
/// <param name="Linked">Its service sign-ins.</param>
/// <param name="Available">The providers it could link and has not.</param>
public sealed record AccountSignIns(
	bool HasPassword,
	IReadOnlyList<LinkedSignIn> Linked,
	IReadOnlyList<ExternalSignInProviderInfo> Available);

/// <summary>
/// Starts a sign-in through a service. <paramref name="Binding"/> is a random value the tab keeps and sends again to
/// finish, so a sign-in someone else started cannot be finished in this tab.
/// </summary>
public sealed record ExternalSignInBeginRequest(string Binding);

/// <summary>Where to send the browser: the service's sign-in page.</summary>
public sealed record ExternalSignInBeginResponse(string Url);

/// <summary><c>GET api/admin/sign-in-providers/redirect-uri/{id}</c>: the address to register with a service.</summary>
public sealed record SignInProviderRedirect(string RedirectUri);
