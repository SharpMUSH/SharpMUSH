namespace SharpMUSH.Library.Models;

/// <summary>
/// One browser's web push subscription for an account: where the browser's push service takes messages for
/// it, and the keys a message is encrypted with. One per browser per account; every character of the account
/// is pushed through it.
/// </summary>
/// <param name="Id">The SHA-256 of <paramref name="Endpoint"/>, base64url: the subscription's name in the API.</param>
/// <param name="AccountId">The account it belongs to.</param>
/// <param name="Endpoint">The push service URL the browser gave. Only hosts <c>PushEndpoints</c> allows are stored.</param>
/// <param name="P256dh">The browser's public key, an uncompressed P-256 point.</param>
/// <param name="Auth">The browser's authentication secret, 16 bytes.</param>
/// <param name="Device">What the holder sees it called, from the browser's user agent ("Firefox on Linux").</param>
/// <param name="KeyId">Which VAPID key it was made with (<c>PushKeyId</c>); after the key changes it no longer delivers.</param>
/// <param name="CreatedAt">When it was stored.</param>
/// <param name="LastSuccessAt">When a push last reached the push service, or null.</param>
/// <param name="LastFailureAt">When a push last failed, or null.</param>
/// <param name="LastFailure">Why it last failed, or null.</param>
public sealed record PushSubscription(
	string Id,
	string AccountId,
	string Endpoint,
	byte[] P256dh,
	byte[] Auth,
	string Device,
	string KeyId,
	DateTimeOffset CreatedAt,
	DateTimeOffset? LastSuccessAt,
	DateTimeOffset? LastFailureAt,
	string? LastFailure);

/// <summary>What <see cref="IPushSubscriptionStore.SavePushSubscriptionAsync"/> did.</summary>
public enum PushSubscriptionSaveOutcome
{
	/// <summary>Stored as a new subscription.</summary>
	Added,

	/// <summary>The endpoint was already stored (for this account or another) and now holds the new keys and account.</summary>
	Replaced,

	/// <summary>Not stored: the account already holds as many subscriptions as it may.</summary>
	AccountFull
}
