using System.Text.RegularExpressions;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Authentication.External;

/// <summary>The sign-in providers staff have set up, as the world keeps them. Client secrets are in the secret store.</summary>
public sealed class ExternalSignInSettings
{
	public List<ExternalSignInProvider> Providers { get; set; } = [];
}

/// <summary>One sign-in provider. <see cref="Endpoints"/> is kept for <see cref="ExternalSignInKinds.Custom"/> only.</summary>
public sealed record ExternalSignInProvider(
	string Id,
	string Kind,
	string Name,
	string ClientId,
	bool Enabled,
	SignInProviderEndpoints? Endpoints = null);

/// <summary>
/// Reads and changes the sign-in providers. A provider's client secret is the secret <c>signin.&lt;id&gt;.secret</c>,
/// so it is set here or with <c>@secret</c>, and overridden from the environment like any other.
/// </summary>
public sealed partial class ExternalSignInProviders(IExpandedObjectDataService serverData, ISecretStore secrets)
{
	/// <summary>The longest provider id.</summary>
	public const int MaxIdLength = 32;

	/// <summary>The longest name a provider may be shown with.</summary>
	public const int MaxNameLength = 40;

	private readonly SemaphoreSlim _writeLock = new(1, 1);

	/// <summary>The secret a provider's client secret is kept under.</summary>
	public static string SecretName(string id) => $"signin.{id}.secret";

	/// <summary>Lower-case letters and digits, so the id is also a part of a secret's name.</summary>
	public static bool IsValidId(string id) => id.Length <= MaxIdLength && IdPattern().IsMatch(id);

	/// <summary>
	/// The addresses and fields of a ready-made kind. Discord and Google say whether an email is verified;
	/// GitHub and Microsoft do not, so theirs is never offered.
	/// </summary>
	public static SignInProviderEndpoints? Preset(string kind) => kind switch
	{
		ExternalSignInKinds.Discord => new("https://discord.com/oauth2/authorize", "https://discord.com/api/oauth2/token",
			"https://discord.com/api/users/@me", "identify email", "id", "global_name username", "email", "verified"),
		ExternalSignInKinds.Google => new("https://accounts.google.com/o/oauth2/v2/auth", "https://oauth2.googleapis.com/token",
			"https://openidconnect.googleapis.com/v1/userinfo", "openid profile email", "sub", "name", "email", "email_verified"),
		ExternalSignInKinds.GitHub => new("https://github.com/login/oauth/authorize", "https://github.com/login/oauth/access_token",
			"https://api.github.com/user", "read:user", "id", "name login", null, null),
		ExternalSignInKinds.Microsoft => new("https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
			"https://login.microsoftonline.com/common/oauth2/v2.0/token", "https://graph.microsoft.com/oidc/userinfo",
			"openid profile email", "sub", "name", null, null),
		_ => null
	};

	/// <summary>What a ready-made kind is called when staff give it no name.</summary>
	public static string DefaultName(string kind) => kind switch
	{
		ExternalSignInKinds.Discord => "Discord",
		ExternalSignInKinds.Google => "Google",
		ExternalSignInKinds.GitHub => "GitHub",
		ExternalSignInKinds.Microsoft => "Microsoft",
		_ => kind
	};

	/// <summary>The addresses a provider signs in through: its kind's, or its own when it is custom.</summary>
	public static SignInProviderEndpoints? EndpointsOf(ExternalSignInProvider provider)
		=> Preset(provider.Kind) ?? provider.Endpoints;

	public async ValueTask<IReadOnlyList<ExternalSignInProvider>> ListAsync()
		=> (await LoadAsync()).Providers.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();

	public async ValueTask<Found<ExternalSignInProvider>> GetAsync(string id)
		=> (await LoadAsync()).Providers.FirstOrDefault(p => p.Id == id) is { } provider
			? provider
			: new NotFound();

	/// <summary>Whether the provider's client secret can be read now.</summary>
	public async ValueTask<bool> HasSecretAsync(string id)
		=> await secrets.GetAsync(SecretName(id)) is string;

	/// <summary>The provider's client secret, or <see cref="NotFound"/> when none is set or it cannot be read.</summary>
	public async ValueTask<Found<string>> SecretAsync(string id)
		=> await secrets.GetAsync(SecretName(id)) switch
		{
			string secret => secret,
			_ => new NotFound()
		};

	/// <summary>Whether players can sign in with it now: enabled, with a client id and a readable secret.</summary>
	public async ValueTask<bool> IsReadyAsync(ExternalSignInProvider provider)
		=> provider.Enabled && provider.ClientId.Length > 0 && EndpointsOf(provider) is not null
			&& await HasSecretAsync(provider.Id);

	/// <summary>The providers the sign-in page shows.</summary>
	public async ValueTask<IReadOnlyList<ExternalSignInProvider>> ReadyAsync()
	{
		List<ExternalSignInProvider> ready = [];
		foreach (var provider in await ListAsync())
		{
			if (await IsReadyAsync(provider))
			{
				ready.Add(provider);
			}
		}

		return ready;
	}

	/// <summary>Adds or replaces the provider <paramref name="id"/>, after checking what it was given.</summary>
	public async ValueTask<Result<ExternalSignInProvider>> SaveAsync(string id, SignInProviderUpdate update)
	{
		if (Validate(id, update) is Error<string> invalid)
		{
			return invalid;
		}

		var custom = update.Kind == ExternalSignInKinds.Custom;
		var name = update.Name?.Trim() is { Length: > 0 } given ? given : DefaultName(update.Kind);
		var provider = new ExternalSignInProvider(id, update.Kind, name, update.ClientId.Trim(), update.Enabled,
			custom ? Trimmed(update.Endpoints!) : null);

		await _writeLock.WaitAsync();
		try
		{
			var data = await LoadAsync();
			data.Providers.RemoveAll(p => p.Id == id);
			data.Providers.Add(provider);
			await serverData.SetExpandedServerDataAsync(data);
		}
		finally
		{
			_writeLock.Release();
		}

		return provider;
	}

	/// <summary>Removes the provider and its stored secret. Links to it stay on accounts, unusable until it is back.</summary>
	public async ValueTask<bool> RemoveAsync(string id)
	{
		bool removed;
		await _writeLock.WaitAsync();
		try
		{
			var data = await LoadAsync();
			removed = data.Providers.RemoveAll(p => p.Id == id) > 0;
			if (removed)
			{
				await serverData.SetExpandedServerDataAsync(data);
			}
		}
		finally
		{
			_writeLock.Release();
		}

		if (removed)
		{
			await secrets.ClearAsync(SecretName(id));
		}

		return removed;
	}

	private static Result<None> Validate(string id, SignInProviderUpdate update)
	{
		if (!IsValidId(id))
		{
			return new Error<string>($"A provider's id is up to {MaxIdLength} lower-case letters and digits.");
		}

		if (!ExternalSignInKinds.All.Contains(update.Kind))
		{
			return new Error<string>($"'{update.Kind}' is not a kind of provider: use one of {string.Join(", ", ExternalSignInKinds.All)}.");
		}

		if (update.Name?.Trim().Length > MaxNameLength)
		{
			return new Error<string>($"A provider's name is at most {MaxNameLength} characters.");
		}

		if (string.IsNullOrWhiteSpace(update.ClientId))
		{
			return new Error<string>("A provider needs the client id its service gave the game.");
		}

		if (update.Kind != ExternalSignInKinds.Custom)
		{
			return new None();
		}

		if (update.Endpoints is not { } endpoints)
		{
			return new Error<string>("A custom provider needs its service's addresses.");
		}

		foreach (var (label, url) in new[]
			{
				("sign-in page", endpoints.AuthorizeUrl), ("token address", endpoints.TokenUrl), ("profile address", endpoints.UserInfoUrl)
			})
		{
			if (!IsServiceUrl(url))
			{
				return new Error<string>($"The {label} must be an https address.");
			}
		}

		return string.IsNullOrWhiteSpace(endpoints.SubjectField) || string.IsNullOrWhiteSpace(endpoints.NameFields)
			? new Error<string>("A custom provider needs the profile fields holding the person's id and name.")
			: new None();
	}

	/// <summary>An https address, or http on this machine (a service run beside the game, as in testing).</summary>
	public static bool IsServiceUrl(string? url)
		=> Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
			&& (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));

	private static SignInProviderEndpoints Trimmed(SignInProviderEndpoints endpoints) => new(
		endpoints.AuthorizeUrl.Trim(), endpoints.TokenUrl.Trim(), endpoints.UserInfoUrl.Trim(),
		endpoints.Scopes.Trim(), endpoints.SubjectField.Trim(), endpoints.NameFields.Trim(),
		endpoints.EmailField?.Trim() is { Length: > 0 } email ? email : null,
		endpoints.EmailVerifiedField?.Trim() is { Length: > 0 } verified ? verified : null);

	private async ValueTask<ExternalSignInSettings> LoadAsync()
		=> await serverData.GetExpandedServerDataAsync<ExternalSignInSettings>() ?? new ExternalSignInSettings();

	[GeneratedRegex("^[a-z0-9]+$", RegexOptions.CultureInvariant)]
	private static partial Regex IdPattern();
}
