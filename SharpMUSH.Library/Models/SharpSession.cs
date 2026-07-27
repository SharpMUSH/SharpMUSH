namespace SharpMUSH.Library.Models;

/// <summary>
/// A persisted web account session. The token is the primary key; sessions are
/// revoked (deleted) instantly by token, account, or origin IP for ban enforcement.
/// </summary>
/// <remarks>
/// The session also carries the character its holder acts as. That binding is what makes the token
/// the acting identity rather than a hint the client re-asserts per request: a browser tab keeps its
/// token in tab-scoped sessionStorage, so restoring the credential after a reload restores who you
/// are, and two tabs holding two tokens act as two characters without either knowing about the
/// other.
/// </remarks>
public class SharpSession
{
	public required string Token { get; set; }
	public required string AccountId { get; set; }
	public long ExpiryUnixMs { get; set; }
	public long TtlMs { get; set; }
	public required string OriginIp { get; set; }

	/// <summary>Dbref number of the character this session acts as; null when the account owns none.</summary>
	public int? CharacterKey { get; set; }

	/// <summary>
	/// Creation time of the bound character. Paired with <see cref="CharacterKey"/> because a dbref
	/// alone is ambiguous across a recycled object — the same pair the switch endpoint validates.
	/// </summary>
	public long? CharacterCreationTime { get; set; }
}
