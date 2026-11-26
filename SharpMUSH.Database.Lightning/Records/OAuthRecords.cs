namespace SharpMUSH.Database.Lightning.Records;

/// <summary>A registered app, keyed by its client id. Times are Unix milliseconds.</summary>
public sealed record OAuthClientRecord
{
	public string Name { get; init; } = "";
	public string? Description { get; init; }
	public string? Homepage { get; init; }
	public string[] RedirectUris { get; init; } = [];
	public string[] Scopes { get; init; } = [];
	public string? SecretHash { get; init; }
	public long CreatedAtMs { get; init; }

	/// <summary>An <c>OAuthClientStatus</c>; absent for an app staff registered, which is allowed.</summary>
	public int? Status { get; init; }

	/// <summary>When a self-described app's metadata was last read; absent for an app staff registered.</summary>
	public long? MetadataFetchedAtMs { get; init; }
}

/// <summary>A grant a character gave an app, keyed by the grant's id. Times are Unix milliseconds.</summary>
public sealed record OAuthGrantRecord
{
	public string AccountKey { get; init; } = "";
	public int CharacterKey { get; init; }
	public long CharacterCreationTime { get; init; }
	public string ClientId { get; init; } = "";
	public string ClientName { get; init; } = "";
	public string[] Scopes { get; init; } = [];
	public long CreatedAtMs { get; init; }
	public long? LastUsedAtMs { get; init; }
}

/// <summary>An issued code or token, keyed by its SHA-256. Times are Unix milliseconds.</summary>
public sealed record OAuthTokenRecord
{
	public int Kind { get; init; }
	public string GrantId { get; init; } = "";
	public string ClientId { get; init; } = "";
	public string AccountKey { get; init; } = "";
	public int CharacterKey { get; init; }
	public long CharacterCreationTime { get; init; }
	public string[] Scopes { get; init; } = [];
	public long ExpiresAtMs { get; init; }
	public string? RedirectUri { get; init; }
	public string? CodeChallenge { get; init; }
}
