namespace SharpMUSH.Library.Models;

/// <summary>Nonsecret, persisted registration settings. Client secrets live only in ISecretStore.</summary>
public sealed record ExternalProviderSettings
{
	public bool Enabled { get; init; }
	public string RegistrationId { get; init; } = "";
	public string ClientId { get; init; } = "";
	public string PublicOrigin { get; init; } = "";
	public string MicrosoftAudience { get; init; } = "common";
	public string[] AllowedTenants { get; init; } = [];
	public string Generation { get; init; } = "";
}

/// <summary>Safe administrator view. No external token or client secret is ever serialized.</summary>
public sealed record ExternalProviderStatus(string Id, string DisplayName, ExternalProviderSettings Settings,
 bool Ready, bool RestartRequired, string Message, string CallbackUrl, string? SecretName, int AffectedAccounts);
public sealed record ExternalProviderSaveRequest(ExternalProviderSettings Settings, bool AcknowledgeAffectedAccounts);
