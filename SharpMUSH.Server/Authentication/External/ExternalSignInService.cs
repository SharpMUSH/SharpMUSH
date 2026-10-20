using System.Buffers.Text;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Logging;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Server.Authentication.External;

/// <summary>
/// Signing in through another service (Discord, Google, ...) with OAuth 2.0's authorization code flow and PKCE: the
/// browser goes to the service, the service sends it back with a code, and the server trades the code for a token
/// and reads who the person is with it. The server talks to the service itself, with the provider's client secret,
/// so nothing the browser carries back is trusted for who someone is.
/// </summary>
/// <remarks>
/// <para>A service identity signs in to the account it is linked to. One that is linked to none can make a new
/// account with no password, or be linked to an account from its settings. An identity is never matched to an
/// account by its email: a service that lets someone claim an address they do not hold would hand them the account
/// that has it.</para>
///
/// <para>The flow ends in the portal, not in a redirect carrying a session: the service sends the browser back to
/// the callback, the callback sends it on to the portal with the flow's id, and the portal finishes the flow with the
/// binding its tab kept (<see cref="ExternalSignInFlows"/>).</para>
/// </remarks>
public sealed class ExternalSignInService(
	ExternalSignInProviders providers,
	ExternalSignInFlows flows,
	IAccountStore accounts,
	IHttpClientFactory httpClientFactory,
	IConfiguration configuration,
	TimeProvider time,
	ILogger<ExternalSignInService> logger)
{
	/// <summary>The named client the server talks to services with.</summary>
	public const string HttpClientName = "external-sign-in";

	/// <summary>The route the service sends the browser back to, under the server's own address.</summary>
	public static string CallbackPath(string providerId) => $"/api/auth/external/{providerId}/callback";

	/// <summary>The portal page that finishes a flow.</summary>
	public const string PortalPath = "/login/external";

	/// <summary>The longest subject kept: Google's are 21 digits, Discord's 19, and OpenID Connect allows 255.</summary>
	public const int MaxSubjectLength = 255;

	/// <summary>The longest name kept from a service.</summary>
	public const int MaxNameLength = 64;

	private const string Unavailable = "Signing in with that service is not available.";
	private const string Expired = "The sign-in took too long or was already used. Try again.";
	private const string Busy = "The server is handling too many sign-ins. Try again shortly.";

	/// <summary>A flow back from the service, with its provider as it is set up now.</summary>
	public sealed record ReturnedFlow(string Id, ExternalSignInProvider Provider, ExternalSignInFlows.Returned Flow,
		ExternalIdentity Identity);

	/// <summary>
	/// The address of the service's sign-in page for <paramref name="providerId"/>, holding a new flow. With
	/// <paramref name="linkAccountId"/>, the flow links the service to that account instead of signing in.
	/// </summary>
	public async ValueTask<Result<string>> BeginAsync(string providerId, string? binding, string? linkAccountId,
		HttpRequest request)
	{
		if (binding is not { Length: >= 16 and <= 256 })
			return new Error<string>("The sign-in request was malformed.");

		if (await providers.GetAsync(providerId) is not ExternalSignInProvider provider
			|| !await providers.IsReadyAsync(provider)
			|| ExternalSignInProviders.EndpointsOf(provider) is not { } endpoints)
			return new Error<string>(Unavailable);

		if (Addresses(request) is not (var serverBase, var portal))
			return new Error<string>("Signing in with another service is not available from this address.");

		var verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
		var redirectUri = serverBase + CallbackPath(provider.Id);
		var pending = new ExternalSignInFlows.Pending(provider.Id, verifier, ExternalSignInFlows.HashBinding(binding),
			linkAccountId, redirectUri, portal);

		if (flows.Add(pending) is not { } state)
			return new Error<string>(Busy);

		var query = new Dictionary<string, string>
		{
			["response_type"] = "code",
			["client_id"] = provider.ClientId,
			["redirect_uri"] = redirectUri,
			["scope"] = endpoints.Scopes,
			["state"] = state,
			["code_challenge"] = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
			["code_challenge_method"] = "S256",
		};
		return endpoints.AuthorizeUrl + (endpoints.AuthorizeUrl.Contains('?') ? "&" : "?")
			+ string.Join('&', query.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}"));
	}

	/// <summary>
	/// The address to register with a service, as this server is reached by <paramref name="request"/>; null when
	/// the asking page is not one of the game's.
	/// </summary>
	public string? RedirectUriFor(string providerId, HttpRequest request)
		=> Addresses(request) is var (serverBase, _) ? serverBase + CallbackPath(providerId) : null;

	/// <summary>
	/// Where the service sends the browser back to, and the portal the browser goes on to. The portal is normally
	/// served by this server, and then both are the page's own origin, as the browser says it: behind a proxy whose
	/// forwarded scheme is not trusted, the request alone reads as plain http. A portal served from elsewhere (the
	/// standalone client dev server) must be listed in <c>Cors:AllowedOrigins</c>, and the service sends the browser
	/// back to this server's own address.
	/// </summary>
	private (string ServerBase, string Portal)? Addresses(HttpRequest request)
	{
		var own = $"{request.Scheme}://{request.Host}";
		var origin = request.Headers.Origin.FirstOrDefault() is { Length: > 0 } header ? header.TrimEnd('/') : own;
		if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
			return null;

		if (string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase))
			return (origin, origin);

		var listed = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
		return listed.Any(o => string.Equals(o.TrimEnd('/'), origin, StringComparison.OrdinalIgnoreCase))
			? (own, origin)
			: null;
	}

	/// <summary>
	/// Answers the service sending the browser back: trades <paramref name="code"/> for who the person is, and
	/// answers with the portal address that finishes the flow. Every outcome, a refusal too, goes on to the portal,
	/// which says what happened.
	/// </summary>
	public async ValueTask<string> CallbackAsync(string providerId, string? state, string? code, string? error,
		CancellationToken ct = default)
	{
		if (flows.Take<ExternalSignInFlows.Pending>(state) is not { } pending || pending.Provider != providerId)
			return $"{PortalPath}?error=expired";

		var provider = await providers.GetAsync(providerId) is ExternalSignInProvider found ? found : null;
		var name = provider?.Name ?? providerId;
		var (identity, failure) = (provider, error, code) switch
		{
			(null, _, _) => (null, Unavailable),
			(_, "access_denied", _) => (null, $"Signing in with {name} was cancelled."),
			(_, { Length: > 0 }, _) => (null, $"{name} did not sign you in."),
			(_, _, { Length: > 0 }) => await IdentifyAsync(provider, pending, code, ct),
			_ => (null, $"{name} did not sign you in."),
		};

		if (error is { Length: > 0 })
			logger.LogInformation("Sign-in with {Provider} refused by the service: {Error}", providerId, LogSanitizer.Sanitize(error));

		var returned = new ExternalSignInFlows.Returned(providerId, pending.BindingHash, pending.LinkAccountId, identity, failure);
		return flows.Add(returned) is { } id
			? $"{pending.ReturnOrigin}{PortalPath}?flow={id}"
			: $"{pending.ReturnOrigin}{PortalPath}?error=busy";
	}

	/// <summary>The flow back from the service, for the tab holding <paramref name="binding"/>; left in place.</summary>
	public async ValueTask<Result<ReturnedFlow>> ReturnedAsync(string? flowId, string? binding)
	{
		if (flows.Peek<ExternalSignInFlows.Returned>(flowId) is not { } flow || !ExternalSignInFlows.Binds(flow.BindingHash, binding))
			return new Error<string>(Expired);

		if (flow.Identity is not { } identity)
		{
			flows.Remove(flowId!);
			return new Error<string>(flow.Failure ?? Unavailable);
		}

		if (await providers.GetAsync(flow.Provider) is not ExternalSignInProvider provider || !await providers.IsReadyAsync(provider))
		{
			flows.Remove(flowId!);
			return new Error<string>(Unavailable);
		}

		return new ReturnedFlow(flowId!, provider, flow, identity);
	}

	/// <summary>Ends the flow: it cannot be finished again.</summary>
	public void Finish(ReturnedFlow flow) => flows.Remove(flow.Id);

	/// <summary>
	/// The account the identity signs in to, its use recorded; <see cref="NotFound"/> when it is linked to none, or
	/// only to a deleted account.
	/// </summary>
	public async ValueTask<Found<SharpAccount>> LinkedAccountAsync(ReturnedFlow flow, CancellationToken ct = default)
	{
		if (await accounts.GetAccountExternalLoginAsync(flow.Provider.Id, flow.Identity.Subject, ct) is not { } link
			|| await accounts.GetAccountByIdAsync(link.AccountId, ct) is not { Status: not AccountStatus.Deleted } account)
			return new NotFound();

		await accounts.RecordAccountExternalLoginUseAsync(flow.Provider.Id, flow.Identity.Subject, flow.Identity.Name,
			time.GetUtcNow(), ct);
		return account;
	}

	/// <summary>Links the identity to the account that started the flow.</summary>
	public ValueTask<ExternalLoginAddOutcome> LinkAsync(ReturnedFlow flow, string accountId, CancellationToken ct = default)
	{
		var now = time.GetUtcNow();
		return accounts.AddAccountExternalLoginAsync(
			new AccountExternalLogin(accountId, flow.Provider.Id, flow.Identity.Subject, flow.Identity.Name, now, now), ct);
	}

	/// <summary>Makes a new account with no password, signed in to by the identity.</summary>
	public async ValueTask<ExternalAccountCreation> CreateAccountAsync(ReturnedFlow flow, string username, string? email,
		CancellationToken ct = default)
	{
		if (SystemAccount.IsReserved(username))
			return new Error<string>($"Username '{username}' is reserved.");

		var now = time.GetUtcNow();
		return await accounts.CreateAccountWithExternalLoginAsync(username, email,
			new AccountExternalLogin(string.Empty, flow.Provider.Id, flow.Identity.Subject, flow.Identity.Name, now, now), ct);
	}

	/// <summary>How <paramref name="account"/> can be signed in to: its password, its service sign-ins, and the services it could link.</summary>
	public async ValueTask<AccountSignIns> SignInsAsync(SharpAccount account, CancellationToken ct = default)
	{
		var all = await providers.ListAsync();
		var ready = await providers.ReadyAsync();
		var links = await accounts.GetAccountExternalLoginsAsync(account.Id!, ct);

		var linked = links.Select(link => all.FirstOrDefault(p => p.Id == link.Provider) is { } provider
				? new LinkedSignIn(link.Provider, provider.Name, provider.Kind, link.Name, link.CreatedAt, link.LastUsedAt,
					ready.Any(r => r.Id == link.Provider))
				: new LinkedSignIn(link.Provider, link.Provider, null, link.Name, link.CreatedAt, link.LastUsedAt, false))
			.ToList();
		var available = ready.Where(provider => links.All(link => link.Provider != provider.Id))
			.Select(provider => new ExternalSignInProviderInfo(provider.Id, provider.Name, provider.Kind))
			.ToList();

		return new AccountSignIns(!string.IsNullOrEmpty(account.PasswordHash), linked, available);
	}

	/// <summary>
	/// Whether the account still has a way in without the service sign-in <paramref name="withoutProvider"/> or the
	/// passkey <paramref name="withoutPasskey"/>: a password, another passkey, or a sign-in through a service that is
	/// set up now. An account made through a service has no password, and taking its only way in would leave no one
	/// able to reach it.
	/// </summary>
	public async ValueTask<bool> KeepsAWayInAsync(SharpAccount account, string? withoutProvider = null,
		byte[]? withoutPasskey = null, CancellationToken ct = default)
	{
		if (!string.IsNullOrEmpty(account.PasswordHash))
			return true;

		var passkeys = await accounts.GetAccountPasskeysAsync(account.Id!, ct);
		if (passkeys.Any(passkey => withoutPasskey is null || !passkey.CredentialId.AsSpan().SequenceEqual(withoutPasskey)))
			return true;

		var ready = await providers.ReadyAsync();
		return (await accounts.GetAccountExternalLoginsAsync(account.Id!, ct))
			.Any(link => link.Provider != withoutProvider && ready.Any(provider => provider.Id == link.Provider));
	}

	/// <summary>
	/// Removes one of the account's ways in, a passkey or a service sign-in, unless it is the last one
	/// (<see cref="KeepsAWayInAsync"/>). Removals from one account run one at a time, so two at once cannot each see
	/// the other's way in and take both.
	/// </summary>
	public async ValueTask<WayInRemoval> RemoveWayInAsync(SharpAccount account, Func<ValueTask<bool>> remove,
		string? withoutProvider = null, byte[]? withoutPasskey = null, CancellationToken ct = default)
	{
		var gate = _removalGates[(int)((uint)StringComparer.Ordinal.GetHashCode(account.Id!) % (uint)_removalGates.Length)];
		await gate.WaitAsync(ct);
		try
		{
			if (!await KeepsAWayInAsync(account, withoutProvider, withoutPasskey, ct))
				return WayInRemoval.OnlyWayIn;

			return await remove() ? WayInRemoval.Removed : WayInRemoval.NotFound;
		}
		finally
		{
			gate.Release();
		}
	}

	private readonly SemaphoreSlim[] _removalGates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

	/// <summary>Removes the account's sign-in through <paramref name="providerId"/>; false when it has none.</summary>
	public ValueTask<bool> UnlinkAsync(string accountId, string providerId, CancellationToken ct = default)
		=> accounts.RemoveAccountExternalLoginAsync(accountId, providerId, ct);

	/// <summary>A username to offer for a new account: the service's name, without what a username cannot hold.</summary>
	public static string SuggestUsername(ExternalIdentity identity)
	{
		var name = new string(identity.Name.Where(c => c != '@' && !char.IsControl(c)).ToArray()).Trim();
		return name.Length > 0 ? name : identity.Subject;
	}

	/// <summary>Trades the code for a token and reads the profile with it.</summary>
	private async ValueTask<(ExternalIdentity? Identity, string? Failure)> IdentifyAsync(ExternalSignInProvider provider,
		ExternalSignInFlows.Pending pending, string code, CancellationToken ct)
	{
		if (ExternalSignInProviders.EndpointsOf(provider) is not { } endpoints
			|| await providers.SecretAsync(provider.Id) is not string secret)
			return (null, Unavailable);

		var refused = $"{provider.Name} did not sign you in.";
		try
		{
			var http = httpClientFactory.CreateClient(HttpClientName);

			using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, endpoints.TokenUrl)
			{
				Content = new FormUrlEncodedContent(new Dictionary<string, string>
				{
					["grant_type"] = "authorization_code",
					["code"] = code,
					["redirect_uri"] = pending.RedirectUri,
					["client_id"] = provider.ClientId,
					["client_secret"] = secret,
					["code_verifier"] = pending.Verifier,
				}),
			};
			tokenRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			using var tokenResponse = await http.SendAsync(tokenRequest, ct);
			if (!tokenResponse.IsSuccessStatusCode)
			{
				logger.LogInformation("Sign-in with {Provider}: the token request answered {Status}", provider.Id, (int)tokenResponse.StatusCode);
				return (null, refused);
			}

			using var token = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync(ct));
			if (Text(token.RootElement, "access_token") is not { Length: > 0 } accessToken)
			{
				logger.LogInformation("Sign-in with {Provider}: the token answer held no access token", provider.Id);
				return (null, refused);
			}

			using var profileRequest = new HttpRequestMessage(HttpMethod.Get, endpoints.UserInfoUrl);
			profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
			profileRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			using var profileResponse = await http.SendAsync(profileRequest, ct);
			if (!profileResponse.IsSuccessStatusCode)
			{
				logger.LogInformation("Sign-in with {Provider}: the profile request answered {Status}", provider.Id, (int)profileResponse.StatusCode);
				return (null, refused);
			}

			using var profile = JsonDocument.Parse(await profileResponse.Content.ReadAsStringAsync(ct));
			return Identity(profile.RootElement, endpoints) is { } identity
				? (identity, null)
				: (null, refused);
		}
		catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
			&& !ct.IsCancellationRequested)
		{
			logger.LogWarning("Sign-in with {Provider} failed: {Message}", provider.Id, ex.Message);
			return (null, $"Could not reach {provider.Name}. Try again shortly.");
		}
	}

	/// <summary>Who the profile says the person is; null when it names no one.</summary>
	public static ExternalIdentity? Identity(JsonElement profile, SignInProviderEndpoints endpoints)
	{
		if (profile.ValueKind != JsonValueKind.Object
			|| Text(profile, endpoints.SubjectField) is not { Length: > 0 and <= MaxSubjectLength } subject)
			return null;

		var name = endpoints.NameFields.Split(' ', StringSplitOptions.RemoveEmptyEntries)
			.Select(field => Text(profile, field)?.Trim())
			.FirstOrDefault(value => !string.IsNullOrEmpty(value)) ?? subject;
		if (name.Length > MaxNameLength) name = name[..MaxNameLength];

		var verified = endpoints.EmailVerifiedField is { } verifiedField
			&& profile.TryGetProperty(verifiedField, out var flag)
			&& (flag.ValueKind == JsonValueKind.True || (flag.ValueKind == JsonValueKind.String && flag.GetString() == "true"));
		var email = verified && endpoints.EmailField is { } emailField && Text(profile, emailField) is { Length: > 0 } address
			&& address.Contains('@')
				? address
				: null;

		return new ExternalIdentity(subject, name, email);
	}

	/// <summary>A field as text: a string as it is, a number as written. GitHub's ids are numbers.</summary>
	private static string? Text(JsonElement element, string field)
		=> element.TryGetProperty(field, out var value)
			? value.ValueKind switch
			{
				JsonValueKind.String => value.GetString(),
				JsonValueKind.Number => value.GetRawText(),
				_ => null
			}
			: null;
}

/// <summary>What <see cref="ExternalSignInService.RemoveWayInAsync"/> did.</summary>
public enum WayInRemoval
{
	Removed,
	NotFound,
	OnlyWayIn
}
