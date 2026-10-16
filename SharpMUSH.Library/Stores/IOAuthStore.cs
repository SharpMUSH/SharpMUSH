using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>
/// Registered apps (<see cref="OAuthClient"/>), the grants players gave them (<see cref="OAuthGrant"/>), and the codes
/// and tokens issued under those grants (<see cref="OAuthToken"/>, stored by hash). Deleting a grant deletes its tokens
/// in the same write, so a token that is still stored always has its grant.
/// </summary>
public interface IOAuthStore
{
	/// <summary>Stores <paramref name="client"/>, replacing the one with its id.</summary>
	ValueTask SaveOAuthClientAsync(OAuthClient client, CancellationToken cancellationToken = default);

	ValueTask<Found<OAuthClient>> GetOAuthClientAsync(string id, CancellationToken cancellationToken = default);

	/// <summary>Every registered app, oldest first.</summary>
	ValueTask<IReadOnlyList<OAuthClient>> GetOAuthClientsAsync(CancellationToken cancellationToken = default);

	/// <summary>Removes the app with every grant made to it and every token issued to it. False when there was none.</summary>
	ValueTask<bool> DeleteOAuthClientAsync(string id, CancellationToken cancellationToken = default);

	/// <summary>
	/// Stores the grant <paramref name="grant"/>'s character gave its app. When that character already granted the app,
	/// the stored grant keeps its id and first date and takes <paramref name="grant"/>'s scopes and name. Returns the
	/// grant as stored.
	/// </summary>
	ValueTask<OAuthGrant> SaveOAuthGrantAsync(OAuthGrant grant, CancellationToken cancellationToken = default);

	ValueTask<Found<OAuthGrant>> GetOAuthGrantAsync(string id, CancellationToken cancellationToken = default);

	/// <summary>The grant <paramref name="character"/> gave the app <paramref name="clientId"/>, if any.</summary>
	ValueTask<Found<OAuthGrant>> FindOAuthGrantAsync(DBRef character, string clientId, CancellationToken cancellationToken = default);

	/// <summary>The account's grants, for every character it has, oldest first.</summary>
	ValueTask<IReadOnlyList<OAuthGrant>> GetOAuthGrantsForAccountAsync(string accountId, CancellationToken cancellationToken = default);

	/// <summary>Records that the grant was used at <paramref name="at"/>. Nothing is written when it is gone.</summary>
	ValueTask TouchOAuthGrantAsync(string id, DateTimeOffset at, CancellationToken cancellationToken = default);

	/// <summary>
	/// Removes a grant and every token issued under it: with <paramref name="accountId"/>, only that account's. False
	/// when there was no such grant.
	/// </summary>
	ValueTask<bool> DeleteOAuthGrantAsync(string id, string? accountId = null, CancellationToken cancellationToken = default);

	/// <summary>Removes every grant the account made, with their tokens (a ban). Returns how many went.</summary>
	ValueTask<int> DeleteOAuthGrantsForAccountAsync(string accountId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Stores <paramref name="token"/>. False, storing nothing, when its grant is gone: a revocation that landed first
	/// stands.
	/// </summary>
	ValueTask<bool> AddOAuthTokenAsync(OAuthToken token, CancellationToken cancellationToken = default);

	/// <summary>The token whose SHA-256 is <paramref name="hash"/>, expired or not.</summary>
	ValueTask<Found<OAuthToken>> GetOAuthTokenAsync(byte[] hash, CancellationToken cancellationToken = default);

	/// <summary>
	/// Trades a code or refresh token in: deletes the one whose SHA-256 is <paramref name="hash"/> and stores
	/// <paramref name="issued"/>, in one write. False, changing nothing, when it was already traded or revoked, so a
	/// code or refresh token is only ever good once.
	/// </summary>
	ValueTask<bool> RedeemOAuthTokenAsync(byte[] hash, IReadOnlyList<OAuthToken> issued, CancellationToken cancellationToken = default);

	/// <summary>Removes the token whose SHA-256 is <paramref name="hash"/>. False when there was none.</summary>
	ValueTask<bool> DeleteOAuthTokenAsync(byte[] hash, CancellationToken cancellationToken = default);

	/// <summary>Removes every code and token issued under the grant, keeping the grant. Returns how many went.</summary>
	ValueTask<int> DeleteOAuthTokensForGrantAsync(string grantId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Deletes up to <paramref name="maxCount"/> tokens that expired at or before <paramref name="now"/>, earliest
	/// first, and returns how many went.
	/// </summary>
	ValueTask<int> DeleteExpiredOAuthTokensAsync(DateTimeOffset now, int maxCount, CancellationToken cancellationToken = default);
}
