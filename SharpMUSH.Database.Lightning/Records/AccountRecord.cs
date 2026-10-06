namespace SharpMUSH.Database.Lightning.Records;

/// <summary>A stored web account. <c>Status</c> is <c>AccountStatus.ToString()</c>.</summary>
public sealed record AccountRecord
{
	public string Username { get; init; } = "";
	public string? Email { get; init; }
	public string PasswordHash { get; init; } = "";
	public long CreatedAt { get; init; }
	public long UpdatedAt { get; init; }
	public bool IsVerified { get; init; }
	public bool MustChangePassword { get; init; }
	public string Status { get; init; } = "Active";
}

/// <summary>An account's ban, keyed by the account key. Times are Unix milliseconds.</summary>
public sealed record AccountBanRecord
{
	public string Reason { get; init; } = "";
	public string? BannedBy { get; init; }
	public long AtMs { get; init; }
	public long? ExpiresAtMs { get; init; }
}

/// <summary>
/// A passkey, keyed by the SHA-256 of its credential id: an id can run to a kilobyte, past what an LMDB
/// key holds, and the hash is fixed-length. Times are Unix milliseconds.
/// </summary>
public sealed record AccountPasskeyRecord
{
	public string AccountKey { get; init; } = "";
	public byte[] CredentialId { get; init; } = [];
	public byte[] PublicKey { get; init; } = [];
	public uint SignCount { get; init; }
	public string Name { get; init; } = "";
	public string[] Transports { get; init; } = [];
	public bool IsBackedUp { get; init; }
	public long CreatedAtMs { get; init; }
	public long? LastUsedAtMs { get; init; }
}
