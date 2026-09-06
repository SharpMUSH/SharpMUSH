using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// DB-backed account session store. Sessions persist across restarts and are revoked
/// (deleted) by token, account, or origin IP for immediate ban enforcement.
/// </summary>
public sealed class DatabaseAccountSessionStore(ISessionRecordStore database) : IAccountSessionStore
{
	/// <summary>
	/// How far the rolling expiry must have drifted before <see cref="ValidateAsync"/> pays for a write,
	/// as a fraction of the session's own TTL.
	/// </summary>
	/// <remarks>
	/// <para>Sliding on every request means a single portal page load — half a dozen concurrent calls on one
	/// bearer — writes the same document half a dozen times simultaneously. Serialising those writes (the
	/// exclusive transaction in the ArangoDB provider) stops them failing, but on its own it just converts
	/// the conflict into a lock convoy: every authenticated request in the process would queue behind one
	/// collection lock. The write has to stop happening, not merely stop colliding.</para>
	/// <para>The quantity being compared is exactly "time since the expiry was last persisted", so this
	/// coalesces the slide into at most one write per session per 2% of its TTL, which removes essentially
	/// all of them. The cost is that a session's real expiry can trail a perfectly continuous rolling window
	/// by up to that same 2%.</para>
	/// <para>Relative to TTL rather than an absolute duration: what a user notices is the fraction of their
	/// window lost, not the seconds. A fixed 30s slack would be a rounding error against a 7-day remember-me
	/// token and a quarter of a 2-minute one, so the same constant would be simultaneously too lax and far
	/// too strict. Expressed as a fraction it holds the same guarantee — "you never lose more than 2% of your
	/// window" — for every TTL the server issues.</para>
	/// </remarks>
	private const double SlidePersistThresholdFractionOfTtl = 0.02;

	public async Task<string> CreateTokenAsync(string accountId, TimeSpan ttl, string originIp,
		int? characterKey = null, long? characterCreationTime = null, CancellationToken ct = default)
	{
		var token = Guid.NewGuid().ToString("N");
		await database.UpsertSessionAsync(new SharpSession
		{
			Token = token,
			AccountId = accountId,
			OriginIp = originIp,
			ExpiryUnixMs = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeMilliseconds(),
			TtlMs = (long)ttl.TotalMilliseconds,
			CharacterKey = characterKey,
			CharacterCreationTime = characterCreationTime
		}, ct);
		return token;
	}

	public async Task<IAccountSessionStore.SessionIdentity?> ValidateAsync(string token, CancellationToken ct = default)
	{
		var s = await database.GetSessionAsync(token, ct);
		if (s is null) return null;

		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		// ExpiryUnixMs is the instant the session expires at, not the last instant it is usable.
		if (now >= s.ExpiryUnixMs)
		{
			await database.DeleteSessionAsync(token, ct);
			return null;
		}

		var identity = new IAccountSessionStore.SessionIdentity(s.AccountId, s.CharacterKey, s.CharacterCreationTime);

		// Slide the rolling window, but only persist a slide that has moved materially — see
		// SlidePersistThresholdFractionOfTtl. The drift is also the time since the last persisted slide,
		// so a burst of concurrent requests on one bearer produces at most one write between them.
		var slid = now + s.TtlMs;
		if (slid - s.ExpiryUnixMs < s.TtlMs * SlidePersistThresholdFractionOfTtl)
			return identity;

		// TouchSessionExpiryAsync, not UpsertSessionAsync: the read above and this write are not atomic, and
		// the upsert's insert branch would reinstate a session that a revocation deleted in between — a
		// logout, an account ban, or a sitelock rule — keeping a revoked token alive for up to another full
		// TTL. The conditional update does nothing when the document is gone, so the revocation stands.
		//
		// Losing that race also fails this validation. The false is not "no write was needed", it is the
		// database stating that the session was already gone at the instant of the write; the caller learns
		// that for free, and authenticating on a token that a ban has already revoked is exactly the
		// outcome the ban exists to prevent. Only CreateTokenAsync may insert a session.
		if (!await database.TouchSessionExpiryAsync(token, slid, ct))
			return null;

		return identity;
	}

	public Task RevokeAsync(string token, CancellationToken ct = default)
		=> database.DeleteSessionAsync(token, ct).AsTask();

	public Task RevokeAllForAccountAsync(string accountId, CancellationToken ct = default)
		=> database.DeleteSessionsForAccountAsync(accountId, ct).AsTask();

	public Task RevokeAllForIpAsync(string originIp, CancellationToken ct = default)
		=> database.DeleteSessionsForIpAsync(originIp, ct).AsTask();

	public Task<string[]> GetKnownOriginIpsAsync(CancellationToken ct = default)
		=> database.GetSessionOriginIpsAsync(ct).AsTask();
}
