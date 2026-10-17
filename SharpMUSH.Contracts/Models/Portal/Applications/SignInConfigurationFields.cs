namespace SharpMUSH.Library.Models.Portal.Applications;

/// <summary>Definitions shared by sign-in discovery and the hand-rendered controls.</summary>
public static class SignInConfigurationFields
{
	public static readonly ConfigurationOption Enabled = new("Enabled", "Enable sign-in", "Allow account sign-in through this provider.", ["login", "oauth"], RequiresRestart: true, LabelResourceKey: "AdmSigninEnable");
	public static readonly ConfigurationOption ClientId = new("ClientId", "Client ID", "The application client identifier registered with the sign-in provider.", ["oauth client"], RequiresRestart: true, LabelResourceKey: "AdmSigninClientId");
	public static readonly ConfigurationOption PublicOrigin = new("PublicOrigin", "Public origin", "Public website origin used to build the OAuth callback URL.", ["redirect", "callback"], RequiresRestart: true, LabelResourceKey: "AdmSigninPublicOrigin", DescriptionResourceKey: "AdmSigninPublicOriginHelp");
	public static readonly ConfigurationOption MicrosoftAudience = new("MicrosoftAudience", "Microsoft audience", "The Microsoft account audience allowed to sign in.", ["tenant"], RequiresRestart: true, LabelResourceKey: "AdmSigninAudience", DescriptionResourceKey: "AdmSigninAudienceHelp");
	public static readonly ConfigurationOption AllowedTenants = new("AllowedTenants", "Allowed tenants", "Microsoft tenant identifiers allowed to sign in.", ["organization", "directory"], RequiresRestart: true, LabelResourceKey: "AdmSigninTenants", DescriptionResourceKey: "AdmSigninTenantsHelp");

	public static ConfigurationOption ClientSecret(string name, string displayName) => new(name,
		displayName + " client secret", displayName + " account sign-in client secret.", ["oauth", "credential"],
		Route: "/admin/config/secrets", RequiresRestart: true);

	public static IReadOnlyList<ConfigurationOption> ForProvider(string id) => id == "microsoft"
		? [Enabled, ClientId, PublicOrigin, MicrosoftAudience, AllowedTenants]
		: [Enabled, ClientId, PublicOrigin];
}
