using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Opaque refresh-token store.  Each token is bound to an account and a specific
/// character (DBRef).  Refresh tokens are single-use: <see cref="ValidateAsync"/>
/// does NOT slide expiry — call <see cref="RevokeAsync"/> immediately after a
/// successful refresh to enforce one-shot semantics.
/// </summary>
public interface IRefreshTokenStore
{
	/// <summary>
	/// Creates a new refresh token bound to <paramref name="accountId"/> /
	/// <paramref name="characterRef"/> with the given TTL.
	/// Returns a 32-character lowercase hex token.
	/// </summary>
	Task<string> CreateTokenAsync(string accountId, DBRef characterRef, TimeSpan ttl,
		CancellationToken ct = default);

	/// <summary>
	/// Validates a token.  Returns the bound <c>(AccountId, CharacterRef)</c> tuple if
	/// the token exists and has not expired.  Returns <c>null</c> if unknown or expired.
	/// Does NOT revoke the token on read — the caller is responsible for revoking
	/// after issuing new tokens.
	/// </summary>
	Task<(string AccountId, DBRef CharacterRef)?> ValidateAsync(string token,
		CancellationToken ct = default);

	/// <summary>Explicitly invalidates a single token (logout / one-shot enforcement).</summary>
	Task RevokeAsync(string token, CancellationToken ct = default);

	/// <summary>Revokes ALL refresh tokens belonging to <paramref name="accountId"/>.</summary>
	Task RevokeAllForAccountAsync(string accountId, CancellationToken ct = default);
}
