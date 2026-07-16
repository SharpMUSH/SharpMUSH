namespace SharpMUSH.Library.Models;

/// <summary>
/// A persisted web account session. The token is the primary key; sessions are
/// revoked (deleted) instantly by token, account, or origin IP for ban enforcement.
/// </summary>
public class SharpSession
{
	public required string Token { get; set; }
	public required string AccountId { get; set; }
	public long ExpiryUnixMs { get; set; }
	public long TtlMs { get; set; }
	public required string OriginIp { get; set; }
}
