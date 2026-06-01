namespace SharpMUSH.Library.Models;

/// <summary>
/// A web/account-layer identity that owns zero or more MUSH characters.
/// Stored in <c>node_accounts</c> — has no MUSH dbref and no in-game presence.
/// Characters are linked via <c>edge_account_owns_character</c> graph edges.
/// </summary>
public class SharpAccount
{
	/// <summary>Database document ID: "node_accounts/&lt;key&gt;"</summary>
	public string? Id { get; set; }

	/// <summary>Unique username for login. Also used as the display name.</summary>
	public required string Username { get; set; }

	/// <summary>Optional email address. If set, must be globally unique. Used for login and future password recovery.</summary>
	public string? Email { get; set; }

	public required string PasswordHash { get; set; }

	public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

	public long UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

	/// <summary>Only meaningful when <see cref="Email"/> is set. Reserved for future email-verification flow.</summary>
	public bool IsVerified { get; set; }

	public bool IsDisabled { get; set; }
}
