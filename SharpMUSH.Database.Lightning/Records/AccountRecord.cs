namespace SharpMUSH.Database.Lightning.Records;

/// <summary>Mirrors <c>SurrealDatabase.AccountDbRecord</c>. <c>Status</c> is <c>AccountStatus.ToString()</c>.</summary>
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
