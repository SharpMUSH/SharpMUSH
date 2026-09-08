namespace SharpMUSH.Database.Lightning.Records;

/// <summary>Mirrors <c>SurrealDatabase.SessionDbRecord</c>; the session token is the LMDB key.</summary>
public sealed record SessionRecord
{
	public string AccountId { get; init; } = "";
	public long ExpiryUnixMs { get; init; }
	public long TtlMs { get; init; }
	public string OriginIp { get; init; } = "";
	public int? CharacterKey { get; init; }
	public long? CharacterCreationTime { get; init; }
}
