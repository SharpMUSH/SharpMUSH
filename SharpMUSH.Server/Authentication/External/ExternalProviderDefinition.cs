namespace SharpMUSH.Server.Authentication.External;

/// <summary>Fixed upstream endpoints and scopes; operator configuration cannot turn this into an arbitrary HTTP client.</summary>
public sealed record ExternalProviderDefinition(string Id, string DisplayName, string Issuer, bool OpenIdConnect,
	bool RequiresClientSecret, string AuthorizationEndpoint, string TokenEndpoint, string UserInformationEndpoint, string[] Scopes)
{
	public string Scheme => "External-" + Id;
	public string CallbackPath => "/signin-external-" + Id;
	public string SecretName => "auth." + Id + ".clientsecret";

	public static IReadOnlyList<ExternalProviderDefinition> All { get; } = Array.AsReadOnly<ExternalProviderDefinition>(
	[
		new("discord", "Discord", "https://discord.com", false, true, "https://discord.com/oauth2/authorize",
			"https://discord.com/api/oauth2/token", "https://discord.com/api/users/@me", ["identify"]),
		new("google", "Google", "https://accounts.google.com", true, true, "", "", "", ["openid", "profile"]),
		new("microsoft", "Microsoft", "https://login.microsoftonline.com", true, true, "", "", "", ["openid", "profile"]),
		new("grapevine", "Grapevine", "https://grapevine.haus", false, false, "https://grapevine.haus/oauth/authorize",
			"https://grapevine.haus/oauth/token", "https://grapevine.haus/users/me", ["profile"]),
		// No scope: only /user is read and the token is discarded, so an identity needs no private access.
		new("github", "GitHub", "https://github.com", false, true, "https://github.com/login/oauth/authorize",
			"https://github.com/login/oauth/access_token", "https://api.github.com/user", [])
	]);

	public static ExternalProviderDefinition Get(string id) => All.Single(provider => provider.Id == id);
	public static bool IsKnown(string id) => All.Any(provider => provider.Id == id);
}
