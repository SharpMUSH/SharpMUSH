using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Applications;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.OAuth;

/// <summary>
/// The authorization server behind <c>api/oauth</c>: the apps staff register, the grants players give them, and the
/// codes and tokens issued under those grants. It speaks the authorization code flow of RFC 6749 with PKCE (RFC 7636,
/// <c>S256</c> only, for every app), refresh tokens that are good once each, and revocation (RFC 7009).
/// </summary>
public interface IOAuthService
{
	/// <summary>Every registered app, oldest first.</summary>
	ValueTask<IReadOnlyList<OAuthClient>> GetClientsAsync(CancellationToken ct = default);

	/// <summary>Every scope an app may ask for: the built-in ones, then one per custom permission category.</summary>
	ValueTask<IReadOnlyList<OAuthScopeView>> GetScopesAsync(CancellationToken ct = default);

	/// <summary>The registered app or hosted package with this client id.</summary>
	ValueTask<Found<OAuthApp>> FindAppAsync(string clientId, CancellationToken ct = default);

	/// <summary>Registers an app; a <paramref name="confidential"/> one gets a secret, returned this once.</summary>
	ValueTask<Result<RegisteredOAuthClient>> RegisterClientAsync(OAuthClientDraft draft, bool confidential, CancellationToken ct = default);

	/// <summary>Changes an app's name, description, homepage, addresses and scopes. Grants it already has keep their scopes.</summary>
	ValueTask<FoundResult<OAuthClient>> UpdateClientAsync(string id, OAuthClientDraft draft, CancellationToken ct = default);

	/// <summary>Gives the app a new secret, returned this once; the old one stops working.</summary>
	ValueTask<Found<RegisteredOAuthClient>> ResetClientSecretAsync(string id, CancellationToken ct = default);

	/// <summary>Removes the app, every grant players gave it, and every token it holds.</summary>
	ValueTask<bool> DeleteClientAsync(string id, CancellationToken ct = default);

	/// <summary>
	/// Checks an authorization request and says what to ask the player, before they answer: the scopes asked for, less
	/// the groups in which <paramref name="character"/> holds nothing (<see cref="DelegatedScopes.Relevant"/>).
	/// </summary>
	ValueTask<AuthorizationCheck> CheckAuthorizationAsync(OAuthAuthorizeRequest request, string accountId, DBRef character,
		CancellationToken ct = default);

	/// <summary>
	/// The player's answer to <paramref name="request"/> for <paramref name="character"/>: a yes stores the grant and sends a
	/// code back to the app, a no sends <c>access_denied</c>.
	/// </summary>
	ValueTask<AuthorizationDecision> DecideAsync(OAuthAuthorizeRequest request, string accountId, DBRef character, bool approved,
		CancellationToken ct = default);

	/// <summary>The token endpoint: <c>grant_type</c> <c>authorization_code</c> or <c>refresh_token</c>.</summary>
	ValueTask<TokenOutcome> TokenAsync(IReadOnlyDictionary<string, string> form, ClientCredentials client, CancellationToken ct = default);

	/// <summary>The revocation endpoint: forgets the token if <paramref name="client"/> holds it, and says nothing either way.</summary>
	ValueTask RevokeTokenAsync(string token, ClientCredentials client, CancellationToken ct = default);

	/// <summary>What an access token lets its bearer do, or NotFound when it is not a live access token.</summary>
	ValueTask<Found<DelegatedAccess>> ValidateAccessTokenAsync(string token, CancellationToken ct = default);

	/// <summary>
	/// An access token for the hosted package <paramref name="packageId"/> acting as <paramref name="character"/>, when the
	/// character has allowed it what it asks for; otherwise what to ask the player.
	/// </summary>
	ValueTask<HostedTokenOutcome> IssueHostedTokenAsync(string accountId, DBRef character, string packageId, CancellationToken ct = default);

	/// <summary>Stores <paramref name="character"/>'s yes to the hosted package <paramref name="packageId"/>.</summary>
	ValueTask<Found<OAuthGrant>> AllowHostedAsync(string accountId, DBRef character, string packageId, CancellationToken ct = default);

	/// <summary>The grants the account's characters gave, oldest first.</summary>
	ValueTask<IReadOnlyList<OAuthGrant>> GetGrantsAsync(string accountId, CancellationToken ct = default);

	/// <summary>Takes a grant back, with every token issued under it: with <paramref name="accountId"/>, only that account's.</summary>
	ValueTask<bool> RevokeGrantAsync(string grantId, string? accountId = null, CancellationToken ct = default);

	/// <summary>Takes back every grant the account gave (a ban).</summary>
	ValueTask<int> RevokeAccountAsync(string accountId, CancellationToken ct = default);

	/// <summary>Deletes up to <paramref name="maxCount"/> expired codes and tokens.</summary>
	ValueTask<int> SweepExpiredAsync(int maxCount, CancellationToken ct = default);
}

/// <summary>
/// What staff decide about apps that describe themselves at an https <c>client_id</c> (<see cref="ClientIdMetadata"/>),
/// under the game's <c>oauth_app_policy</c> and <c>oauth_blocked_domains</c>.
/// </summary>
public interface IOAuthAppReview
{
	/// <summary>
	/// Allows, blocks or sends back for review the self-described app <paramref name="clientId"/>. Blocking takes back
	/// every grant players gave it, with every token it holds. NotFound for an id that is not a self-described app the
	/// game has read.
	/// </summary>
	ValueTask<Found<OAuthClient>> SetClientStatusAsync(string clientId, OAuthClientStatus status, CancellationToken ct = default);
}

/// <inheritdoc cref="IOAuthService" />
public sealed class OAuthService(IOAuthStore store, UiPackageStore packages, IRoleRegistryService registry,
	Lazy<IAdministrativeCapabilityService> capabilities, TimeProvider time)
	: IOAuthService, IOAuthAppReview
{
	private readonly IClientMetadataFetcher? _metadata;
	private readonly IOptionsWrapper<SharpMUSHOptions>? _options;

	/// <summary>
	/// The authorization server that also accepts apps describing themselves (<see cref="ClientIdMetadata"/>), as the
	/// game's <c>oauth_app_policy</c> allows. Without these, only apps staff register or allow are accepted and no
	/// <c>client_id</c> address is ever read.
	/// </summary>
	public OAuthService(IOAuthStore store, UiPackageStore packages, IRoleRegistryService registry,
		Lazy<IAdministrativeCapabilityService> capabilities, TimeProvider time, IClientMetadataFetcher metadata,
		IOptionsWrapper<SharpMUSHOptions> options)
		: this(store, packages, registry, capabilities, time)
	{
		_metadata = metadata;
		_options = options;
	}

	/// <summary>The policy and block list in force now; the safest, registered apps only, without options.</summary>
	private OAuthOptions AppOptions => _options?.CurrentValue.OAuth ?? new OAuthOptions { AppPolicy = OAuthOptions.Registered };

	/// <summary>Whether players may be asked about, and tokens used by, an app in <paramref name="status"/> under <paramref name="policy"/>.</summary>
	private static bool Accepted(OAuthClientStatus status, string policy)
		=> status == OAuthClientStatus.Allowed || (status == OAuthClientStatus.Unreviewed && policy == OAuthOptions.Open);
	public const string AccessTokenPrefix = "smat_";
	public const string RefreshTokenPrefix = "smrt_";
	public const string CodePrefix = "smac_";
	public const string SecretPrefix = "smcs_";

	public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(1);
	public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromHours(1);
	public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

	/// <summary>How stale a grant's last-used time may get before a request pays for a write.</summary>
	private static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);

	public const int MaxRedirectUris = 10;
	public const int MaxUriLength = 512;
	public const int MaxNameLength = 80;
	public const int MaxDescriptionLength = 500;

	/// <summary>When each grant was last recorded as used, so a burst of requests writes once.</summary>
	private readonly ConcurrentDictionary<string, long> _touched = new(StringComparer.Ordinal);

	/// <summary>Whether <paramref name="token"/> looks like one of this server's access tokens.</summary>
	public static bool IsAccessToken(string? token) => token?.StartsWith(AccessTokenPrefix, StringComparison.Ordinal) == true;

	public ValueTask<IReadOnlyList<OAuthClient>> GetClientsAsync(CancellationToken ct = default) => store.GetOAuthClientsAsync(ct);

	public async ValueTask<IReadOnlyList<OAuthScopeView>> GetScopesAsync(CancellationToken ct = default)
		=>
		[
			.. DelegatedScopes.All.Select(scope => new OAuthScopeView(scope, null, null)),
			.. (await registry.GetCategoriesAsync(CategoryKind.Permission, ct))
				.Select(category => new OAuthScopeView(DelegatedScopes.CategoryScope(category.Name), category.Name, category.Description))
				.Where(view => view.Scope.Length > 0 && !DelegatedScopes.IsKnown(view.Scope))
				.DistinctBy(view => view.Scope, StringComparer.Ordinal)
				.OrderBy(view => view.Scope, StringComparer.Ordinal),
		];

	public async ValueTask<Found<OAuthApp>> FindAppAsync(string clientId, CancellationToken ct = default)
	{
		if (clientId.StartsWith(OAuthApp.PackagePrefix, StringComparison.Ordinal))
		{
			var id = clientId[OAuthApp.PackagePrefix.Length..];
			return await packages.GetAsync(id, ct) is { RequestedScopes.Count: > 0 } definition
				? new OAuthApp(clientId, definition.Name ?? definition.Id, null, null, [], definition.RequestedScopes, null, Hosted: true)
				: new NotFound();
		}

		return await store.GetOAuthClientAsync(clientId, ct) switch
		{
			OAuthClient client => OAuthApp.Of(client),
			NotFound none => none,
		};
	}

	public async ValueTask<Result<RegisteredOAuthClient>> RegisterClientAsync(OAuthClientDraft draft, bool confidential,
		CancellationToken ct = default)
	{
		if (Problem(draft) is { } problem) return new Error<string>(problem);
		var secret = confidential ? SecretPrefix + RandomToken(32) : null;
		var client = new OAuthClient("smc_" + RandomToken(12), draft.Name.Trim(), Blank(draft.Description), Blank(draft.Homepage),
			[.. draft.RedirectUris.Select(uri => uri.Trim())], DelegatedScopes.Union([DelegatedScopes.Profile], draft.Scopes),
			secret is null ? null : HexHash(secret), time.GetUtcNow());
		await store.SaveOAuthClientAsync(client, ct);
		return new RegisteredOAuthClient(client, secret);
	}

	public async ValueTask<FoundResult<OAuthClient>> UpdateClientAsync(string id, OAuthClientDraft draft, CancellationToken ct = default)
	{
		// A self-described app's details are its own document's; staff allow or block it, never edit it.
		if (ClientIdMetadata.IsClientIdUrl(id) || await store.GetOAuthClientAsync(id, ct) is not OAuthClient existing) return new NotFound();
		if (Problem(draft) is { } problem) return new Error<string>(problem);
		var client = existing with
		{
			Name = draft.Name.Trim(),
			Description = Blank(draft.Description),
			Homepage = Blank(draft.Homepage),
			RedirectUris = [.. draft.RedirectUris.Select(uri => uri.Trim())],
			Scopes = DelegatedScopes.Union([DelegatedScopes.Profile], draft.Scopes)
		};
		await store.SaveOAuthClientAsync(client, ct);
		return client;
	}

	public async ValueTask<Found<RegisteredOAuthClient>> ResetClientSecretAsync(string id, CancellationToken ct = default)
	{
		// A self-described app is public: it never has a secret.
		if (ClientIdMetadata.IsClientIdUrl(id) || await store.GetOAuthClientAsync(id, ct) is not OAuthClient existing) return new NotFound();
		var secret = SecretPrefix + RandomToken(32);
		var client = existing with { SecretHash = HexHash(secret) };
		await store.SaveOAuthClientAsync(client, ct);
		return new RegisteredOAuthClient(client, secret);
	}

	public async ValueTask<bool> DeleteClientAsync(string id, CancellationToken ct = default)
	{
		await _clientWrites.WaitAsync(ct);
		try { return await store.DeleteOAuthClientAsync(id, CancellationToken.None); }
		finally { _clientWrites.Release(); }
	}

	/// <summary>
	/// Staff's decisions about an app (allow, block, forget) and the copy kept after a description is read go one at a
	/// time, so a read that finishes as staff decide never writes back the status it read before their decision.
	/// </summary>
	private readonly SemaphoreSlim _clientWrites = new(1, 1);

	/// <summary>Why staff cannot register <paramref name="draft"/>, or null.</summary>
	private static string? Problem(OAuthClientDraft draft)
	{
		if (string.IsNullOrWhiteSpace(draft.Name) || draft.Name.Trim().Length > MaxNameLength)
			return $"An app needs a name of at most {MaxNameLength} characters.";
		if (draft.Description?.Length > MaxDescriptionLength)
			return $"A description may be at most {MaxDescriptionLength} characters.";
		if (Blank(draft.Homepage) is { } homepage
				&& (homepage.Length > MaxUriLength || !Uri.TryCreate(homepage, UriKind.Absolute, out var page) || page.Scheme is not ("https" or "http")))
			return "The homepage must be an http or https address.";
		if (draft.RedirectUris.Count is 0 or > MaxRedirectUris)
			return $"An app needs between 1 and {MaxRedirectUris} redirect addresses.";
		if (draft.RedirectUris.FirstOrDefault(uri => !IsRedirectUri(uri.Trim())) is { } bad)
			return $"'{bad}' cannot be a redirect address: use https, http on 127.0.0.1, [::1] or localhost, or an app's own scheme such as com.example.app:/callback, with no #fragment.";
		if (draft.Scopes.FirstOrDefault(scope => !DelegatedScopes.IsToken(scope)) is { } unknown)
			return $"'{unknown}' is not a scope: the scopes are {string.Join(", ", DelegatedScopes.All)}, or a permission category's name in lower case.";
		return null;
	}

	/// <summary>
	/// An address a code may be sent to (RFC 8252 §7): https; http to the machine itself, for a desktop app listening on
	/// a port of its own; or a private scheme with a dot in it, for an app the system opens by its scheme. Never a fragment.
	/// </summary>
	public static bool IsRedirectUri(string uri)
	{
		if (uri.Length is 0 or > MaxUriLength || !Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || parsed.Fragment.Length > 0
				|| uri.Contains('#'))
			return false;
		return parsed.Scheme switch
		{
			"https" => true,
			"http" => parsed.IsLoopback,
			"javascript" or "data" or "file" or "vbscript" => false,
			var scheme => scheme.Contains('.'),
		};
	}

	/// <summary>
	/// Whether <paramref name="requested"/> is one of <paramref name="registered"/>: the same string, or, for a desktop
	/// app listening on the machine itself, the same loopback address on another port (RFC 8252 §7.3). A desktop app
	/// gets a free port from the system each time, so <c>http://127.0.0.1/callback</c> registered lets
	/// <c>http://127.0.0.1:51234/callback</c> through. Only an IP literal (<c>127.0.0.1</c>, <c>[::1]</c>) moves its
	/// port; <c>localhost</c> is a name and has to match exactly.
	/// </summary>
	public static bool IsRegisteredRedirect(IReadOnlyCollection<string> registered, string requested)
		=> registered.Contains(requested, StringComparer.Ordinal)
			|| (WithoutLoopbackPort(requested) is { } asked
				&& registered.Any(uri => WithoutLoopbackPort(uri) is { } known && string.Equals(known, asked, StringComparison.Ordinal)));

	/// <summary>
	/// <paramref name="uri"/> as written, without its port, when it is <c>http</c> to a loopback IP literal with no user
	/// information; otherwise null. The string is cut rather than rebuilt from a parsed address, so the path and query
	/// are compared exactly as they were written.
	/// </summary>
	private static string? WithoutLoopbackPort(string uri)
	{
		const string scheme = "http://";
		if (!uri.StartsWith(scheme, StringComparison.Ordinal) || !IsRedirectUri(uri)
				|| !Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || !parsed.IsLoopback || parsed.UserInfo.Length > 0
				|| parsed.HostNameType is not (UriHostNameType.IPv4 or UriHostNameType.IPv6))
			return null;
		var end = uri.IndexOfAny(['/', '?'], scheme.Length) is var cut and >= 0 ? cut : uri.Length;
		var authority = uri[scheme.Length..end];
		var portAt = authority.LastIndexOf(':');
		if (portAt > authority.LastIndexOf(']'))
		{
			if (!authority[(portAt + 1)..].All(char.IsAsciiDigit)) return null;
			authority = authority[..portAt];
		}
		return scheme + authority + uri[end..];
	}

	public async ValueTask<AuthorizationCheck> CheckAuthorizationAsync(OAuthAuthorizeRequest request, string accountId,
		DBRef character, CancellationToken ct = default)
		=> await ValidateAsync(request, ct) switch
		{
			ValidAuthorization valid => await PromptAsync(valid, await RelevantAsync(valid.Scopes, accountId, character, ct),
				character, ct),
			AuthorizationRedirect redirect => redirect,
			AuthorizationRefused refused => refused,
		};

	/// <remarks>
	/// The player is sent straight back without a question only to an address the app registered as written. A loopback
	/// address on a port the app did not register is asked about every time (RFC 8252 §8.6): any program on the player's
	/// machine can listen on a port, and without the question it could take a code for an app the player allowed before.
	/// </remarks>
	private async ValueTask<AuthorizationPrompt> PromptAsync(ValidAuthorization valid, IReadOnlyList<string> scopes, DBRef character,
		CancellationToken ct)
		=> new(valid.App, scopes, valid.App.RedirectUris.Contains(valid.RedirectUri, StringComparer.Ordinal)
			&& await store.FindOAuthGrantAsync(character, valid.App.ClientId, ct) is OAuthGrant grant
			&& scopes.All(grant.Scopes.Contains));

	/// <summary><paramref name="scopes"/> less the groups in which the character holds nothing, read fresh.</summary>
	private async ValueTask<IReadOnlyList<string>> RelevantAsync(IReadOnlyList<string> scopes, string accountId, DBRef character,
		CancellationToken ct)
	{
		var held = await capabilities.Value.GetGrantedScopesAsync(new(accountId, character, character), ct);
		var categories = (await registry.GetCustomPermissionsAsync(ct))
			.DistinctBy(permission => permission.Scope, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(permission => permission.Scope, permission => permission.Category, StringComparer.OrdinalIgnoreCase);
		return DelegatedScopes.Relevant(scopes, held, categories);
	}

	public async ValueTask<AuthorizationDecision> DecideAsync(OAuthAuthorizeRequest request, string accountId, DBRef character,
		bool approved, CancellationToken ct = default)
		=> await ValidateAsync(request, ct) switch
		{
			ValidAuthorization valid when !approved => Redirect(valid.RedirectUri, request.State,
				("error", "access_denied"), ("error_description", "The player did not allow it.")),
			ValidAuthorization valid => await ApprovedAsync(valid, request.State, accountId, character, ct),
			AuthorizationRedirect redirect => redirect,
			AuthorizationRefused refused => refused,
		};

	private async ValueTask<AuthorizationDecision> ApprovedAsync(ValidAuthorization valid, string? state, string accountId,
		DBRef character, CancellationToken ct)
	{
		var scopes = await RelevantAsync(valid.Scopes, accountId, character, ct);
		var grant = await GrantAsync(accountId, character, valid.App, scopes, ct);
		var code = CodePrefix + RandomToken(32);
		var stored = await store.AddOAuthTokenAsync(new OAuthToken(Hash(Encoding.UTF8.GetBytes(code)), OAuthTokenKind.Code,
			grant.Id, valid.App.ClientId, accountId, character, scopes, time.GetUtcNow() + CodeLifetime,
			valid.RedirectUri, valid.CodeChallenge), ct);
		// Only a revocation between the two writes loses the grant, and then there is nothing to send a code for.
		return stored
			? Redirect(valid.RedirectUri, state, ("code", code))
			: Redirect(valid.RedirectUri, state, ("error", "access_denied"), ("error_description", "The grant was revoked."));
	}

	/// <summary>The character's grant for <paramref name="app"/>, widened to <paramref name="scopes"/>.</summary>
	private async ValueTask<OAuthGrant> GrantAsync(string accountId, DBRef character, OAuthApp app, IReadOnlyList<string> scopes,
		CancellationToken ct)
	{
		var existing = await store.FindOAuthGrantAsync(character, app.ClientId, ct) is OAuthGrant found ? found : null;
		return await store.SaveOAuthGrantAsync(new OAuthGrant("smg_" + RandomToken(12), accountId, character, app.ClientId, app.Name,
			DelegatedScopes.Union(existing?.Scopes ?? [], scopes), time.GetUtcNow(), null), ct);
	}

	private readonly record struct ValidAuthorization(OAuthApp App, string RedirectUri, IReadOnlyList<string> Scopes, string CodeChallenge);

	private union Validation(ValidAuthorization, AuthorizationRedirect, AuthorizationRefused);

	/// <summary>
	/// RFC 6749 §4.1.2.1: an unknown app or an unregistered address is told to the player and goes nowhere; anything
	/// else wrong goes back to the app as an error.
	/// </summary>
	private async ValueTask<Validation> ValidateAsync(OAuthAuthorizeRequest request, CancellationToken ct)
		=> await AppForAuthorizationAsync(request.ClientId, ct) switch
		{
			OAuthApp app => await ValidateForAsync(app, request, ct),
			AuthorizationRefused refused => refused,
		};

	private union Resolution(OAuthApp, AuthorizationRefused);

	private static AuthorizationRefused UnknownApp => new("No app with that client_id is registered on this game.");

	/// <summary>
	/// The app an authorization request names. A registered app or hosted package is looked up. An https
	/// <c>client_id</c> is an app describing itself (<see cref="ClientIdMetadata"/>): unless its domain is blocked or the
	/// policy is <c>registered</c> (and staff have not allowed it), its description is read, at most once per
	/// <see cref="ClientIdMetadata.RefreshAfter"/>, and stored for staff to see; it is asked about only once allowed, or
	/// at once while the policy is <c>open</c>.
	/// </summary>
	private async ValueTask<Resolution> AppForAuthorizationAsync(string? clientId, CancellationToken ct)
	{
		if (string.IsNullOrEmpty(clientId)) return UnknownApp;
		if (!ClientIdMetadata.IsClientIdUrl(clientId))
			return await FindAppAsync(clientId, ct) is OAuthApp { Hosted: false, Status: OAuthClientStatus.Allowed } registered
				? registered
				: UnknownApp;

		var options = AppOptions;
		var policy = OAuthOptions.PolicyOf(options);
		var host = ClientIdMetadata.HostOf(clientId);
		if (ClientIdMetadata.IsBlocked(clientId, options.BlockedDomains))
			return new AuthorizationRefused($"This game does not accept apps from {host}.");
		var stored = await store.GetOAuthClientAsync(clientId, ct) is OAuthClient known ? known : null;
		if (stored is { Status: OAuthClientStatus.Blocked })
			return new AuthorizationRefused($"Staff on this game blocked {stored.Name} ({host}).");
		if (policy == OAuthOptions.Registered && stored is not { Status: OAuthClientStatus.Allowed })
			return new AuthorizationRefused("This game accepts only apps its staff registered or allowed.");

		var client = stored;
		var now = time.GetUtcNow();
		// Under registered, an app staff allowed keeps the copy they saw: no address is read.
		if (_metadata is not null && policy != OAuthOptions.Registered && Due(clientId, stored, now))
		{
			if (stored is null && await TooManyWaitingAsync(host, options, ct) is { } full) return full;
			var reserved = !_reading.ContainsKey(clientId);
			if (reserved && !TryTakeReadPlace(host))
				return stored is null
					? new AuthorizationRefused($"The game is reading other apps' descriptions; try {host} again in a moment.")
					: Accepted(stored.Status, policy) ? OAuthApp.Of(stored) : new AuthorizationRefused($"{stored.Name} ({host}) is waiting for staff on this game to allow it.");
			switch (await SharedReadAsync(clientId, host, now, reserved))
			{
				case OAuthClient fresh:
					if (await KeepAsync(clientId, stored, fresh, now, ct) is not { } kept) return UnknownApp;
					client = kept;
					break;
				// A description that cannot be read now leaves the copy read before in place.
				case Error<string> error when stored is null:
					return new AuthorizationRefused($"The app's description at {clientId} could not be used: {error.Value}");
			}
		}

		if (client is null) return UnknownApp;
		if (client.Status == OAuthClientStatus.Blocked)
			return new AuthorizationRefused($"Staff on this game blocked {client.Name} ({host}).");
		return Accepted(client.Status, policy)
			? OAuthApp.Of(client)
			: new AuthorizationRefused($"{client.Name} ({host}) is waiting for staff on this game to allow it.");
	}

	/// <summary>How long a description that could not be read is left alone before it is tried again.</summary>
	public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(5);

	/// <summary>The most apps still waiting for staff that one host may have.</summary>
	public const int MaxWaitingPerHost = 5;

	/// <summary>When each address's description last failed to be read.</summary>
	private readonly ConcurrentDictionary<string, DateTimeOffset> _failedAt = new(StringComparer.Ordinal);

	/// <summary>Reads in flight, so many requests naming one address read it once.</summary>
	private readonly ConcurrentDictionary<string, Lazy<Task<Result<OAuthClient>>>> _reading = new(StringComparer.Ordinal);

	/// <summary>The most addresses whose failed read is remembered; past it, the oldest are forgotten first.</summary>
	public const int MaxRememberedFailures = 1024;

	/// <summary>The most reads in flight at once, across every app, and from one host.</summary>
	public const int MaxReadsInFlight = 8;

	public const int MaxReadsInFlightPerHost = 2;

	/// <summary>Reads in flight per host, to keep one site's slow answers from taking every place.</summary>
	private readonly ConcurrentDictionary<string, int> _readsPerHost = new(StringComparer.Ordinal);

	private int _readsInFlight;

	private bool Due(string clientId, OAuthClient? stored, DateTimeOffset now)
		=> (stored?.MetadataFetchedAt is not { } fetched || now - fetched >= ClientIdMetadata.RefreshAfter)
			&& !(_failedAt.TryGetValue(clientId, out var failed) && now - failed < RetryAfterFailure);

	private void RememberFailure(string clientId, DateTimeOffset now)
	{
		if (_failedAt.Count >= MaxRememberedFailures)
		{
			foreach (var expired in _failedAt.Where(pair => now - pair.Value >= RetryAfterFailure).Select(pair => pair.Key).ToList())
				_failedAt.TryRemove(expired, out _);
			foreach (var oldest in _failedAt.OrderBy(pair => pair.Value).Take(_failedAt.Count - MaxRememberedFailures + 1)
					.Select(pair => pair.Key).ToList())
				_failedAt.TryRemove(oldest, out _);
		}

		_failedAt[clientId] = now;
	}

	/// <summary>
	/// Reads the description once for every request that needs it now. The read is not tied to one player's request:
	/// the reader stops on its own after its deadline.
	/// </summary>
	private async Task<Result<OAuthClient>> SharedReadAsync(string clientId, string host, DateTimeOffset now, bool reserved)
	{
		var mine = new Lazy<Task<Result<OAuthClient>>>(() => ReadDescriptionAsync(clientId, now, CancellationToken.None).AsTask());
		var entry = _reading.GetOrAdd(clientId, mine);
		var owner = ReferenceEquals(entry, mine);
		// A read already in flight for this address was joined: a place taken for a new one goes back. Only the request
		// whose entry was kept starts the read, and it gives back its place when the read ends.
		if (reserved && !owner) ReleaseReadPlace(host);
		try
		{
			var result = await entry.Value;
			if (result is Error<string>) RememberFailure(clientId, time.GetUtcNow());
			else _failedAt.TryRemove(clientId, out _);
			return result;
		}
		finally
		{
			if (owner)
			{
				_reading.TryRemove(new KeyValuePair<string, Lazy<Task<Result<OAuthClient>>>>(clientId, entry));
				if (reserved) ReleaseReadPlace(host);
			}
		}
	}

	private bool TryTakeReadPlace(string host)
	{
		if (Interlocked.Increment(ref _readsInFlight) > MaxReadsInFlight)
		{
			Interlocked.Decrement(ref _readsInFlight);
			return false;
		}

		if (_readsPerHost.AddOrUpdate(host, 1, (_, count) => count + 1) > MaxReadsInFlightPerHost)
		{
			ReleaseReadPlace(host);
			return false;
		}

		return true;
	}

	private void ReleaseReadPlace(string host)
	{
		Interlocked.Decrement(ref _readsInFlight);
		if (_readsPerHost.AddOrUpdate(host, 0, (_, count) => count - 1) <= 0)
			_readsPerHost.TryRemove(new KeyValuePair<string, int>(host, 0));
	}

	/// <summary>
	/// Stores a description just read, under staff's decision as it stands now, not as it stood before the read: an app
	/// blocked or forgotten while it was read stays so. An allowed app whose description now asks for more (a scope or a
	/// redirect address staff did not see) or names itself differently waits for staff again. Null when it was forgotten.
	/// </summary>
	private async ValueTask<OAuthClient?> KeepAsync(string clientId, OAuthClient? before, OAuthClient fresh, DateTimeOffset now,
		CancellationToken ct)
	{
		await _clientWrites.WaitAsync(ct);
		try { return await KeepLockedAsync(clientId, before, fresh, now); }
		finally { _clientWrites.Release(); }
	}

	private async ValueTask<OAuthClient?> KeepLockedAsync(string clientId, OAuthClient? before, OAuthClient fresh, DateTimeOffset now)
	{
		var ct = CancellationToken.None;
		var current = await store.GetOAuthClientAsync(clientId, ct) is OAuthClient known ? known : null;
		if (before is not null && current is null) return null;
		if (current is { Status: OAuthClientStatus.Blocked }) return current;
		var status = current switch
		{
			null => OAuthClientStatus.Unreviewed,
			{ Status: OAuthClientStatus.Allowed } when Widened(current, fresh) => OAuthClientStatus.Unreviewed,
			_ => current.Status,
		};
		var kept = fresh with { Status = status, CreatedAt = current?.CreatedAt ?? now };
		await store.SaveOAuthClientAsync(kept, ct);
		return kept;
	}

	private static bool Widened(OAuthClient allowed, OAuthClient fresh)
		=> fresh.Name != allowed.Name || fresh.Scopes.Except(allowed.Scopes, StringComparer.Ordinal).Any()
			|| fresh.RedirectUris.Except(allowed.RedirectUris, StringComparer.Ordinal).Any();

	/// <summary>
	/// Anyone can send a player to a new address, and every app read is kept for staff: past a limit of apps still
	/// waiting, in all or from one host, a new one waits until staff decide about the others. Apps on a blocked domain
	/// do not count.
	/// </summary>
	private async ValueTask<AuthorizationRefused?> TooManyWaitingAsync(string host, OAuthOptions options, CancellationToken ct)
	{
		var waiting = (await store.GetOAuthClientsAsync(ct))
			.Where(client => client.Status == OAuthClientStatus.Unreviewed && ClientIdMetadata.IsClientIdUrl(client.Id)
				&& !ClientIdMetadata.IsBlocked(client.Id, options.BlockedDomains))
			.ToList();
		if (waiting.Count >= ClientIdMetadata.MaxWaiting)
			return new AuthorizationRefused($"Staff on this game have too many apps to review, so {host} cannot be used until they review some.");
		if (waiting.Count(client => ClientIdMetadata.HostOf(client.Id) == host) >= MaxWaitingPerHost)
			return new AuthorizationRefused($"Staff on this game have too many apps from {host} to review, so this one cannot be used until they review some.");
		return null;
	}

	private async ValueTask<Result<OAuthClient>> ReadDescriptionAsync(string clientId, DateTimeOffset now, CancellationToken ct)
		=> await _metadata!.FetchAsync(clientId, ct) switch
		{
			string json => ClientIdMetadata.Read(clientId, json, now),
			Error<string> error => error,
		};

	/// <summary>
	/// Whether tokens of <paramref name="clientId"/> still work: a self-described app must still be accepted under the
	/// policy in force, so a token stops once staff block the app or its domain, or hold apps for review it has not passed.
	/// </summary>
	private async ValueTask<bool> StillAcceptedAsync(string clientId, CancellationToken ct)
	{
		if (!ClientIdMetadata.IsClientIdUrl(clientId)) return true;
		var options = AppOptions;
		return !ClientIdMetadata.IsBlocked(clientId, options.BlockedDomains)
			&& await store.GetOAuthClientAsync(clientId, ct) is OAuthClient client
			&& Accepted(client.Status, OAuthOptions.PolicyOf(options));
	}

	public async ValueTask<Found<OAuthClient>> SetClientStatusAsync(string clientId, OAuthClientStatus status, CancellationToken ct = default)
	{
		if (!ClientIdMetadata.IsClientIdUrl(clientId)) return new NotFound();
		await _clientWrites.WaitAsync(ct);
		try { return await SetClientStatusLockedAsync(clientId, status); }
		finally { _clientWrites.Release(); }
	}

	// Once begun, the writes finish whatever happens to the request, so a block never stops between its two steps.
	private async ValueTask<Found<OAuthClient>> SetClientStatusLockedAsync(string clientId, OAuthClientStatus status)
	{
		var ct = CancellationToken.None;
		if (await store.GetOAuthClientAsync(clientId, ct) is not OAuthClient client) return new NotFound();
		var changed = client with { Status = status };
		await store.SaveOAuthClientAsync(changed, ct);
		if (status != OAuthClientStatus.Blocked) return changed;
		// Removing the app takes every grant and token it holds with it; the row comes back blocked.
		await store.DeleteOAuthClientAsync(clientId, ct);
		await store.SaveOAuthClientAsync(changed, ct);
		return changed;
	}

	private async ValueTask<Validation> ValidateForAsync(OAuthApp app, OAuthAuthorizeRequest request, CancellationToken ct)
	{
		if (string.IsNullOrEmpty(request.RedirectUri) || !IsRegisteredRedirect(app.RedirectUris, request.RedirectUri))
			return new AuthorizationRefused($"{app.Name} did not register the address it asked to be sent back to.");

		var redirect = request.RedirectUri;
		if (request.ResponseType != "code")
			return Redirect(redirect, request.State, ("error", "unsupported_response_type"), ("error_description", "Only response_type=code is supported."));
		if (string.IsNullOrEmpty(request.CodeChallenge) || request.CodeChallengeMethod != "S256" || !IsPkceValue(request.CodeChallenge))
			return Redirect(redirect, request.State, ("error", "invalid_request"),
				("error_description", "PKCE is required: send code_challenge with code_challenge_method=S256."));
		if (DelegatedScopes.Parse(request.Scope) is not { } scopes)
			return Redirect(redirect, request.State, ("error", "invalid_scope"),
				("error_description", $"The scopes are {string.Join(", ", DelegatedScopes.All)}."));
		if (scopes.FirstOrDefault(scope => !app.Scopes.Contains(scope)) is { } beyond)
			return Redirect(redirect, request.State, ("error", "invalid_scope"),
				("error_description", $"{app.Name} may not ask for {beyond}."));
		if (await UnknownScopeAsync(scopes, ct) is { } unknown)
			return Redirect(redirect, request.State, ("error", "invalid_scope"),
				("error_description", $"{unknown} is neither a scope nor a permission category on this game."));
		return new ValidAuthorization(app, redirect, scopes, request.CodeChallenge);
	}

	/// <summary>The first of <paramref name="scopes"/> that no scope on <see cref="GetScopesAsync"/> is, or null.</summary>
	private async ValueTask<string?> UnknownScopeAsync(IReadOnlyList<string> scopes, CancellationToken ct)
	{
		if (scopes.All(DelegatedScopes.IsKnown)) return null;
		var known = (await GetScopesAsync(ct)).Select(view => view.Scope).ToHashSet(StringComparer.Ordinal);
		return scopes.FirstOrDefault(scope => !known.Contains(scope));
	}

	private static AuthorizationRedirect Redirect(string uri, string? state, params (string Name, string Value)[] parameters)
	{
		(string Name, string Value)[] all = state is null ? parameters : [.. parameters, ("state", state)];
		var query = string.Join('&', all.Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value)}"));
		return new AuthorizationRedirect(uri + (uri.Contains('?') ? '&' : '?') + query);
	}

	public async ValueTask<TokenOutcome> TokenAsync(IReadOnlyDictionary<string, string> form, ClientCredentials client,
		CancellationToken ct = default)
	{
		var credentials = client with { ClientId = client.ClientId ?? form.GetValueOrDefault("client_id") };
		if (await AuthenticateClientAsync(credentials, ct) is not OAuthApp app)
			return new TokenError(TokenError.InvalidClient, "Unknown app, or the wrong secret for it.");

		return form.GetValueOrDefault("grant_type") switch
		{
			"authorization_code" => await ExchangeCodeAsync(app, form, ct),
			"refresh_token" => await RefreshAsync(app, form, ct),
			null => new TokenError(TokenError.InvalidRequest, "grant_type is required."),
			_ => new TokenError(TokenError.UnsupportedGrantType, "grant_type must be authorization_code or refresh_token."),
		};
	}

	/// <summary>The app <paramref name="credentials"/> name, when they prove it: its secret when it has one.</summary>
	private async ValueTask<OAuthApp?> AuthenticateClientAsync(ClientCredentials credentials, CancellationToken ct)
	{
		if (string.IsNullOrEmpty(credentials.ClientId)
				|| await FindAppAsync(credentials.ClientId, ct) is not OAuthApp { Hosted: false } app
				|| !await StillAcceptedAsync(app.ClientId, ct)) return null;
		if (app.SecretHash is null) return credentials.Secret is null ? app : null;
		return credentials.Secret is { } secret
			&& CryptographicOperations.FixedTimeEquals(Convert.FromHexString(app.SecretHash), Hash(Encoding.UTF8.GetBytes(secret)))
				? app
				: null;
	}

	private async ValueTask<TokenOutcome> ExchangeCodeAsync(OAuthApp app, IReadOnlyDictionary<string, string> form, CancellationToken ct)
	{
		if (form.GetValueOrDefault("code") is not { Length: > 0 } code)
			return new TokenError(TokenError.InvalidRequest, "code is required.");
		if (form.GetValueOrDefault("code_verifier") is not { } verifier || !IsPkceValue(verifier))
			return new TokenError(TokenError.InvalidRequest, "code_verifier is required: 43 to 128 letters, digits, '-', '.', '_' or '~'.");

		var hash = Hash(Encoding.UTF8.GetBytes(code));
		if (await store.GetOAuthTokenAsync(hash, ct) is not OAuthToken { Kind: OAuthTokenKind.Code } stored
				|| stored.ExpiresAt <= time.GetUtcNow() || stored.ClientId != app.ClientId)
			return new TokenError(TokenError.InvalidGrant, "The code is unknown, used, expired, or another app's.");
		if (form.GetValueOrDefault("redirect_uri") != stored.RedirectUri)
			return new TokenError(TokenError.InvalidGrant, "redirect_uri must be the one the code was sent to.");
		var challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
		if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(challenge), Encoding.ASCII.GetBytes(stored.CodeChallenge ?? "")))
			return new TokenError(TokenError.InvalidGrant, "code_verifier does not match the code_challenge.");

		return await IssueAsync(hash, stored, stored.Scopes, ct);
	}

	private async ValueTask<TokenOutcome> RefreshAsync(OAuthApp app, IReadOnlyDictionary<string, string> form, CancellationToken ct)
	{
		if (form.GetValueOrDefault("refresh_token") is not { Length: > 0 } token)
			return new TokenError(TokenError.InvalidRequest, "refresh_token is required.");
		var hash = Hash(Encoding.UTF8.GetBytes(token));
		if (await store.GetOAuthTokenAsync(hash, ct) is not OAuthToken { Kind: OAuthTokenKind.Refresh } stored
				|| stored.ExpiresAt <= time.GetUtcNow() || stored.ClientId != app.ClientId)
			return new TokenError(TokenError.InvalidGrant, "The refresh token is unknown, used, expired, or another app's.");
		if (!await StillAcceptedAsync(app.ClientId, ct))
			return new TokenError(TokenError.InvalidGrant, "Staff on this game no longer accept this app.");

		var scopes = stored.Scopes;
		if (form.GetValueOrDefault("scope") is { } narrower)
		{
			// RFC 6749 §6: a refresh may ask for less, never for more.
			if (DelegatedScopes.Parse(narrower) is not { } asked || asked.Any(scope => !stored.Scopes.Contains(scope)))
				return new TokenError(TokenError.InvalidScope, "A refresh may only ask for scopes the refresh token carries.");
			scopes = asked;
		}

		return await IssueAsync(hash, stored, scopes, ct);
	}

	/// <summary>Trades the code or refresh token <paramref name="redeemed"/> for a new access token and refresh token.</summary>
	private async ValueTask<TokenOutcome> IssueAsync(byte[] hash, OAuthToken redeemed, IReadOnlyList<string> scopes, CancellationToken ct)
	{
		var now = time.GetUtcNow();
		var access = AccessTokenPrefix + RandomToken(32);
		var renewal = RefreshTokenPrefix + RandomToken(32);
		OAuthToken[] issued =
		[
			redeemed with { Hash = Hash(Encoding.UTF8.GetBytes(access)), Kind = OAuthTokenKind.Access, Scopes = scopes,
				ExpiresAt = now + AccessTokenLifetime, RedirectUri = null, CodeChallenge = null },
			// The refresh token keeps what the code carried, so asking for less now does not lose the rest.
			redeemed with { Hash = Hash(Encoding.UTF8.GetBytes(renewal)), Kind = OAuthTokenKind.Refresh,
				ExpiresAt = now + RefreshTokenLifetime, RedirectUri = null, CodeChallenge = null },
		];

		return await store.RedeemOAuthTokenAsync(hash, issued, ct)
			? new IssuedTokens(access, (int)AccessTokenLifetime.TotalSeconds, renewal, scopes)
			: new TokenError(TokenError.InvalidGrant, "The code or refresh token was already used, or the grant was revoked.");
	}

	public async ValueTask RevokeTokenAsync(string token, ClientCredentials client, CancellationToken ct = default)
	{
		if (await AuthenticateClientAsync(client, ct) is not OAuthApp app) return;
		var hash = Hash(Encoding.UTF8.GetBytes(token));
		if (await store.GetOAuthTokenAsync(hash, ct) is not OAuthToken stored || stored.ClientId != app.ClientId) return;
		// RFC 7009 §2.1: revoking a refresh token ends the access tokens of the same grant too. The grant, the player's yes,
		// stays: only the player takes that back.
		if (stored.Kind == OAuthTokenKind.Refresh) await store.DeleteOAuthTokensForGrantAsync(stored.GrantId, ct);
		else await store.DeleteOAuthTokenAsync(hash, ct);
	}

	public async ValueTask<Found<DelegatedAccess>> ValidateAccessTokenAsync(string token, CancellationToken ct = default)
	{
		if (!IsAccessToken(token)
				|| await store.GetOAuthTokenAsync(Hash(Encoding.UTF8.GetBytes(token)), ct) is not OAuthToken { Kind: OAuthTokenKind.Access } stored)
			return new NotFound();
		var now = time.GetUtcNow();
		if (stored.ExpiresAt <= now) return new NotFound();
		if (await store.GetOAuthGrantAsync(stored.GrantId, ct) is not OAuthGrant grant) return new NotFound();
		if (!await StillAcceptedAsync(stored.ClientId, ct)) return new NotFound();

		var last = _touched.GetValueOrDefault(grant.Id, grant.LastUsedAt?.ToUnixTimeMilliseconds() ?? 0);
		if (now.ToUnixTimeMilliseconds() - last >= TouchInterval.TotalMilliseconds)
		{
			_touched[grant.Id] = now.ToUnixTimeMilliseconds();
			await store.TouchOAuthGrantAsync(grant.Id, now, ct);
		}

		return new DelegatedAccess(stored, grant.ClientName);
	}

	public async ValueTask<HostedTokenOutcome> IssueHostedTokenAsync(string accountId, DBRef character, string packageId,
		CancellationToken ct = default)
	{
		if (await FindAppAsync(OAuthApp.PackagePrefix + packageId, ct) is not OAuthApp { Hosted: true } app) return new NotFound();
		var scopes = await RelevantAsync(app.Scopes, accountId, character, ct);
		if (await store.FindOAuthGrantAsync(character, app.ClientId, ct) is not OAuthGrant grant
				|| !scopes.All(grant.Scopes.Contains)
				|| AccountKey(grant.AccountId) != AccountKey(accountId))
			return new ConsentRequired(app, scopes);

		var access = AccessTokenPrefix + RandomToken(32);
		var stored = await store.AddOAuthTokenAsync(new OAuthToken(Hash(Encoding.UTF8.GetBytes(access)), OAuthTokenKind.Access,
			grant.Id, app.ClientId, accountId, character, scopes, time.GetUtcNow() + AccessTokenLifetime), ct);
		return stored
			? new IssuedTokens(access, (int)AccessTokenLifetime.TotalSeconds, null, scopes)
			: new ConsentRequired(app, scopes);
	}

	/// <summary>An account id without its collection, as <c>node_accounts/&lt;key&gt;</c> and the bare key both name it.</summary>
	private static string AccountKey(string accountId) => accountId[(accountId.IndexOf('/') + 1)..];

	public async ValueTask<Found<OAuthGrant>> AllowHostedAsync(string accountId, DBRef character, string packageId,
		CancellationToken ct = default)
		=> await FindAppAsync(OAuthApp.PackagePrefix + packageId, ct) is OAuthApp { Hosted: true } app
			? await GrantAsync(accountId, character, app, await RelevantAsync(app.Scopes, accountId, character, ct), ct)
			: new NotFound();

	public ValueTask<IReadOnlyList<OAuthGrant>> GetGrantsAsync(string accountId, CancellationToken ct = default)
		=> store.GetOAuthGrantsForAccountAsync(accountId, ct);

	public ValueTask<bool> RevokeGrantAsync(string grantId, string? accountId = null, CancellationToken ct = default)
		=> store.DeleteOAuthGrantAsync(grantId, accountId, ct);

	public ValueTask<int> RevokeAccountAsync(string accountId, CancellationToken ct = default)
		=> store.DeleteOAuthGrantsForAccountAsync(accountId, ct);

	public ValueTask<int> SweepExpiredAsync(int maxCount, CancellationToken ct = default)
		=> store.DeleteExpiredOAuthTokensAsync(time.GetUtcNow(), maxCount, ct);

	/// <summary>A PKCE code verifier or S256 challenge: 43 to 128 unreserved characters (RFC 7636 §4.1).</summary>
	private static bool IsPkceValue(string value)
		=> value.Length is >= 43 and <= 128 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~');

	private static string RandomToken(int bytes) => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(bytes));

	private static byte[] Hash(byte[] bytes) => SHA256.HashData(bytes);

	private static string HexHash(string secret) => Convert.ToHexString(Hash(Encoding.UTF8.GetBytes(secret)));

	private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
