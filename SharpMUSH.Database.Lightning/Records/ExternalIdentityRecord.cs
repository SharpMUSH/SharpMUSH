namespace SharpMUSH.Database.Lightning.Records;

/// <summary>Full immutable identity tuple accompanies its hashed key so a collision cannot change ownership.</summary>
public sealed record ExternalIdentityRecord
{
	public string AccountKey { get; init; } = "";
	public string Registration { get; init; } = "";
	public string Issuer { get; init; } = "";
	public string Subject { get; init; } = "";
	public string? Label { get; init; }
	public long CreatedAtMs { get; init; }
	public long? LastUsedAtMs { get; init; }
}
