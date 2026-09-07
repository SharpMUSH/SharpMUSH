using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>
/// Persisted account-session records keyed by token.
/// </summary>
public interface ISessionRecordStore
{
	/// <summary>Creates or replaces a session document keyed by its token.</summary>
	ValueTask UpsertSessionAsync(SharpSession session, CancellationToken cancellationToken = default);

	/// <summary>Returns the session for a token, or null if absent.</summary>
	ValueTask<SharpSession?> GetSessionAsync(string token, CancellationToken cancellationToken = default);

	/// <summary>
	/// Slides an existing session's expiry to <paramref name="expiryUnixMs"/>. Returns <c>true</c> if the
	/// session was still there and was updated, <c>false</c> if it was gone.
	/// </summary>
	/// <remarks>
	/// This exists because <see cref="UpsertSessionAsync"/> must not be used to renew a session. Renewal
	/// is read-then-write, and the upsert's insert branch reinstates a document that a revocation deleted
	/// in between — logout, <see cref="DeleteSessionsForAccountAsync"/>, or a ban — leaving a revoked
	/// session alive for up to a full TTL. Renewal therefore has to be a conditional update that does
	/// nothing at all when the document is absent, which is what this is. Only session creation may
	/// insert.
	/// </remarks>
	ValueTask<bool> TouchSessionExpiryAsync(string token, long expiryUnixMs,
		CancellationToken cancellationToken = default);

	ValueTask DeleteSessionAsync(string token, CancellationToken cancellationToken = default);
	ValueTask DeleteSessionsForAccountAsync(string accountId, CancellationToken cancellationToken = default);
	ValueTask DeleteSessionsForIpAsync(string originIp, CancellationToken cancellationToken = default);

	/// <summary>
	/// The distinct origin IPs of the sessions that currently exist.
	/// </summary>
	/// <remarks>
	/// Ban enforcement needs this because sitelock rules are patterns — globs and CIDR blocks — while
	/// <see cref="DeleteSessionsForIpAsync"/> is exact equality on one address. Matching a pattern
	/// inside the query would mean writing the same glob/CIDR semantics three times, once per query
	/// language, and AQL/Cypher/SurrealQL do not agree on how. Returning the candidate addresses lets
	/// <c>SitelockMatcher</c> stay the single implementation of what a rule matches.
	/// </remarks>
	ValueTask<string[]> GetSessionOriginIpsAsync(CancellationToken cancellationToken = default);
}
