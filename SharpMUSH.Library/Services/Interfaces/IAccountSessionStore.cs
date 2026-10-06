namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Multi-use session token store for web account authentication.
/// <para>
/// After a successful account login via the API, an <see cref="IAccountSessionStore"/> token
/// is issued and returned to the browser. Subsequent API calls (character list, create character,
/// request OTT) authenticate by presenting this token. Tokens expire after 15 minutes of
/// inactivity and are renewed on each successful use.
/// </para>
/// </summary>
public interface IAccountSessionStore
{
	/// <summary>
	/// A validated session: the account it belongs to, and the character it acts as (null when the
	/// account owns none). Returned as a unit because the acting character is part of the credential,
	/// not something the caller may supply alongside it.
	/// </summary>
	public readonly record struct SessionIdentity(string AccountId, int? CharacterKey, long? CharacterCreationTime);

	/// <summary>
	/// Create a new session token bound to <paramref name="accountId"/> with the given TTL,
	/// recording the <paramref name="originIp"/> the session was created from.
	/// </summary>
	Task<string> CreateTokenAsync(string accountId, TimeSpan ttl, string originIp,
		int? characterKey = null, long? characterCreationTime = null, CancellationToken ct = default);

	/// <summary>
	/// Validates a token. If valid and unexpired, returns the bound account and acting character and
	/// slides the expiry window by the original TTL. Returns <c>null</c> if unknown, expired, a remembered
	/// login (see <see cref="CreateRememberedLoginAsync"/>), or revoked
	/// while this very call was in flight — a token a ban has already taken away must not authenticate
	/// the request that was holding it.
	/// </summary>
	Task<SessionIdentity?> ValidateAsync(string token, CancellationToken ct = default);

	/// <summary>
	/// Creates a remembered login for <paramref name="accountId"/>: a credential that lasts
	/// <paramref name="ttl"/> past its last use, kept by the browser in an HttpOnly cookie. Only
	/// <see cref="RedeemRememberedLoginAsync"/> accepts it; <see cref="ValidateAsync"/> refuses it, so it
	/// never authenticates a request by itself. Revoked like any session, by token, account or origin IP.
	/// </summary>
	Task<string> CreateRememberedLoginAsync(string accountId, TimeSpan ttl, string originIp, CancellationToken ct = default);

	/// <summary>
	/// The account a remembered login belongs to, sliding its expiry by its TTL; <c>null</c> if the token is
	/// unknown, expired, revoked, or a tab session rather than a remembered login.
	/// </summary>
	Task<string?> RedeemRememberedLoginAsync(string token, CancellationToken ct = default);

	/// <summary>Explicitly invalidates a token (logout).</summary>
	Task RevokeAsync(string token, CancellationToken ct = default);

	/// <summary>Invalidates every session token bound to the account (disable/ban).</summary>
	Task RevokeAllForAccountAsync(string accountId, CancellationToken ct = default);

	/// <summary>Invalidates every session token created from the given origin IP (ban enforcement).</summary>
	Task RevokeAllForIpAsync(string originIp, CancellationToken ct = default);

	/// <summary>
	/// The distinct origin IPs of the sessions that currently exist, so ban enforcement can decide which
	/// of them a glob or CIDR sitelock rule matches.
	/// </summary>
	/// <remarks>
	/// <see cref="RevokeAllForIpAsync"/> takes one literal address, and sitelock rules are patterns. A
	/// rule like <c>10.0.0.0/8</c> or <c>*.example.net</c> therefore had nothing to revoke unless the
	/// banned party happened to be connected at that instant, because the only addresses ban enforcement
	/// knew about were the ones it read off live connections. This is how it learns about the sessions
	/// that exist without one.
	/// </remarks>
	Task<string[]> GetKnownOriginIpsAsync(CancellationToken ct = default);

	/// <summary>
	/// Deletes sessions that expired without being presented again, at most <paramref name="maxCount"/>
	/// in this call, and reports how many went and how many expired ones are still waiting.
	/// </summary>
	Task<SessionSweep> SweepExpiredAsync(int maxCount, CancellationToken ct = default);

	/// <summary>One <see cref="SweepExpiredAsync"/> step: sessions deleted, and the expired backlog left behind.</summary>
	public readonly record struct SessionSweep(int Deleted, long Remaining);
}
