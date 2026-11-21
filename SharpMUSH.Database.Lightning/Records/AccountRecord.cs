namespace SharpMUSH.Database.Lightning.Records;

/// <summary>A stored web account. <c>Status</c> is <c>AccountStatus.ToString()</c>.</summary>
public sealed record AccountRecord
{
	public string Username { get; init; } = "";
	public string? Email { get; init; }
	public string? PasswordHash { get; init; }
	public bool AllowCharacterPasswordAccountLogin { get; init; } = true;
	public long CredentialEpoch { get; init; }
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
/// An account value, keyed account key + 0x00 + name: its JSON and its own view, write and delete locks (empty when unset).
/// </summary>
public sealed record AccountValueRecord
{
	public string Json { get; init; } = "null";
	public string View { get; init; } = "";
	public string Write { get; init; } = "";
	public string Delete { get; init; } = "";
}

/// <summary>An account value rule, keyed by its name pattern: the locks matching values take.</summary>
public sealed record AccountValueRuleRecord
{
	public string View { get; init; } = "";
	public string Write { get; init; } = "";
	public string Delete { get; init; } = "";
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

/// <summary>
/// A web push subscription, keyed by the SHA-256 of its endpoint, which can run past what an LMDB key holds.
/// Times are Unix milliseconds.
/// </summary>
public sealed record PushSubscriptionRecord
{
	public string AccountKey { get; init; } = "";
	public string Endpoint { get; init; } = "";
	public byte[] P256dh { get; init; } = [];
	public byte[] Auth { get; init; } = [];
	public string Device { get; init; } = "";
	public string KeyId { get; init; } = "";
	public long CreatedAtMs { get; init; }
	public long? LastSuccessAtMs { get; init; }
	public long? LastFailureAtMs { get; init; }
	public string? LastFailure { get; init; }
}
