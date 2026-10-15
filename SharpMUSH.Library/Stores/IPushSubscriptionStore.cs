using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>
/// Web push subscriptions (<see cref="PushSubscription"/>), keyed by <see cref="PushSubscription.Id"/> and listed
/// per account.
/// </summary>
public interface IPushSubscriptionStore
{
	/// <summary>
	/// Stores <paramref name="subscription"/>. An endpoint already stored is replaced, whichever account held it:
	/// the endpoint is the browser's, and the browser now belongs to this account. A new one is refused when the
	/// account already holds <paramref name="maxPerAccount"/>; that is checked in the write itself.
	/// </summary>
	ValueTask<PushSubscriptionSaveOutcome> SavePushSubscriptionAsync(PushSubscription subscription, int maxPerAccount,
		CancellationToken cancellationToken = default);

	/// <summary>The subscription with this id, whichever account holds it, or null.</summary>
	ValueTask<Found<PushSubscription>> GetPushSubscriptionAsync(string id, CancellationToken cancellationToken = default);

	/// <summary>The account's subscriptions, oldest first.</summary>
	ValueTask<IReadOnlyList<PushSubscription>> GetPushSubscriptionsAsync(string accountId,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Removes a subscription: with <paramref name="accountId"/>, only that account's. False when there was no
	/// such subscription.
	/// </summary>
	ValueTask<bool> RemovePushSubscriptionAsync(string id, string? accountId = null,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Records how a push to the subscription went: <paramref name="failure"/> null for delivered, else why not.
	/// Nothing is written when the subscription is gone.
	/// </summary>
	ValueTask RecordPushResultAsync(string id, DateTimeOffset at, string? failure,
		CancellationToken cancellationToken = default);
}
